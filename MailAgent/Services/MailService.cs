using FeuerSoftware.MailAgent.Extensions;
using FeuerSoftware.MailAgent.Models;
using FeuerSoftware.MailAgent.Options;
using Microsoft.Extensions.Options;
using MimeKit;
using Nito.AsyncEx;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace FeuerSoftware.MailAgent.Services
{
    internal class MailService : IMailService, IDisposable
    {
        private sealed record MailClientEntry(IMailClient Client, AsyncLock Lock, string SiteName);

        private readonly MailAgentOptions _options;
        private readonly IMailClientFactory _mailClientFactory;
        private readonly ILogger<MailService> _log;
        private readonly Dictionary<string, DateTime> _seenMessages;
        private readonly Subject<(MimeMessage, SiteModel)> _eMailsObservable = new();
        private readonly List<IDisposable?> _eMailsSubscriptions = new();
        private readonly List<IDisposable?> _reconnectionSubscriptions = new();
        private readonly List<MailClientEntry> _mailClients = new();
        private CancellationTokenSource? _stoppingCts;

        private static readonly TimeSpan ShutdownDisconnectTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan InitialConnectRetryInterval = TimeSpan.FromMinutes(5);

        // A reconnect attempt can legitimately hold a client's lock for up to 2x
        // MailOperationTimeouts.HungCallTimeout (an independent budget for Disconnect, then for
        // Connect - see ReconnectAsync). LockWaitTimeout stays comfortably above that worst case so a
        // concurrently-waiting subscription doesn't give up on a merely-slow-not-hung holder.
        private static readonly TimeSpan LockWaitTimeout = (MailOperationTimeouts.HungCallTimeout * 2) + TimeSpan.FromSeconds(30);

        public MailService(
            [NotNull] IMailClientFactory mailClientFactory,
            [NotNull] IOptions<MailAgentOptions> options,
            [NotNull] ILogger<MailService> log)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _mailClientFactory = mailClientFactory ?? throw new ArgumentNullException(nameof(mailClientFactory));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _seenMessages = new Dictionary<string, DateTime>();
        }

        public IObservable<(MimeMessage, SiteModel)> EMails => _eMailsObservable.AsObservable();

        public async Task StartPollingAsync(CancellationToken cancellationToken)
        {
            if (_options.EMailPollingIntervalSeconds < 4)
            {
                throw new ArgumentOutOfRangeException("Setting EMailPollingIntervalSeconds lower than 4 is not supported!");
            }

            // The token the .NET Generic Host passes into IHostedService.StartAsync is disposed once
            // host startup completes and can never be cancelled again for the rest of the process's
            // lifetime - StopAsync (a separate IHostedService.StopAsync call, with its own live token)
            // is the only real shutdown signal. `_stoppingCts` is linked to `cancellationToken` for
            // correctness in case that's ever not true (e.g. in tests), but it's the one actually
            // cancelled by StopAsync below, and it's what every CreateBoundedToken call is bounded by.
            _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var stoppingToken = _stoppingCts.Token;

            foreach (var siteEmailSetting in _options.EmailSettings)
            {
                var client = _mailClientFactory.CreateClient(siteEmailSetting);
                var clientLock = new AsyncLock();
                var entry = new MailClientEntry(client, clientLock, siteEmailSetting.Name);
                _mailClients.Add(entry);

                var site = new SiteModel()
                {
                    Name = siteEmailSetting.Name,
                    ApiKey = siteEmailSetting.ApiKey,
                };

                if (await TryConnectAsync(client, siteEmailSetting, stoppingToken).ConfigureAwait(false))
                {
                    WireUpSubscriptions(entry, siteEmailSetting, site, stoppingToken);
                }
                else
                {
                    // Keep retrying in the background instead of permanently excluding this mailbox
                    // from polling for the rest of the process's lifetime - a transient startup
                    // condition (DNS, an auth-service hiccup, a slow network) shouldn't require a full
                    // restart to recover from.
                    _ = RetryConnectAsync(entry, siteEmailSetting, site, stoppingToken);
                }
            }
        }

        private async Task<bool> TryConnectAsync(IMailClient client, SiteEmailSetting siteEmailSetting, CancellationToken cancellationToken)
        {
            try
            {
                using var connectCts = CreateBoundedToken(cancellationToken);

                await client.Connect(
                    siteEmailSetting.EMailHost,
                    siteEmailSetting.EMailPort,
                    siteEmailSetting.EMailUsername,
                    siteEmailSetting.EMailPassword,
                    connectCts.Token);

                return true;
            }
            catch (Exception ex)
            {
                _log.LogCritical(ex, $"Failed to connect with mailserver. Using host '{siteEmailSetting.EMailHost}' and username '{siteEmailSetting.EMailUsername}'.");
                return false;
            }
        }

        private async Task RetryConnectAsync(MailClientEntry entry, SiteEmailSetting siteEmailSetting, SiteModel site, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(InitialConnectRetryInterval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                _log.LogInformation($"Retrying initial connect for '{siteEmailSetting.Name}'...");

                if (await TryConnectAsync(entry.Client, siteEmailSetting, cancellationToken).ConfigureAwait(false))
                {
                    WireUpSubscriptions(entry, siteEmailSetting, site, cancellationToken);
                    return;
                }
            }
        }

        private void WireUpSubscriptions(MailClientEntry entry, SiteEmailSetting siteEmailSetting, SiteModel site, CancellationToken cancellationToken)
        {
            var client = entry.Client;
            var clientLock = entry.Lock;

            var reconnectionSubscription = Observable
                .Interval(TimeSpan.FromMinutes(60))
                .SubscribeAsyncSafe(async _ =>
                {
                    using var lockWaitCts = CreateBoundedToken(cancellationToken, LockWaitTimeout);

                    using (await clientLock.LockAsync(lockWaitCts.Token).ConfigureAwait(false))
                    {
                        await ReconnectAsync(client, siteEmailSetting, cancellationToken).ConfigureAwait(false);
                    }
                },
                _log.LogAndContinue($"Failed to reconnect ({siteEmailSetting.Name})."),
                () => _log.LogWarning($"Reconnection subscription for '{siteEmailSetting.Name}' completed unexpectedly."));

            var mailSubscription = Observable
                .Interval(TimeSpan.FromSeconds(_options.EMailPollingIntervalSeconds))
                .TakeWhile(x => !cancellationToken.IsCancellationRequested)
                .SubscribeAsyncSafe(async x =>
                {
                    using var tickCts = CreateBoundedToken(cancellationToken);
                    var tickToken = tickCts.Token;

                    using (await clientLock.LockAsync(tickToken).ConfigureAwait(false))
                    {
                        ClearOutdatedAlreadySeenAt();

                        var eMails = await client.GetUnseenMails(tickToken).ConfigureAwait(false);

                        var eMailsToProcess = new List<(MimeMessage message, string id)>();

                        foreach (var eMail in eMails)
                        {
                            var alreadySeen = _seenMessages.TryGetValue(eMail.id, out var seenTimestamp);
                            var sender = eMail.message.From[0].ToString();
                            var shouldBeIgnoredSubject = !string.IsNullOrEmpty(siteEmailSetting.EMailSubjectFilter)
                                && !eMail.message.Subject.Contains(siteEmailSetting.EMailSubjectFilter, StringComparison.InvariantCultureIgnoreCase);
                            var shouldBeIgnoredSender = !string.IsNullOrEmpty(siteEmailSetting.EMailSenderFilter)
                                && !sender.Contains(siteEmailSetting.EMailSenderFilter, StringComparison.InvariantCultureIgnoreCase);

                            // Message is too old
                            if (!_options.DisableEmailAgeThreshold && (DateTimeOffset.Now - eMail.message.Date).Duration() > TimeSpan.FromMinutes(15))
                            {
                                _log.LogInformation($"Mail with subject '{eMail.message.Subject}' received delayed. EMail was sent at {eMail.message.Date.ToLocalTime()}. Ignore and mark as read.");

                                await client.MarkMessageSeenByUID(eMail.id, tickToken).ConfigureAwait(false);
                                continue;
                            }

                            if (alreadySeen && (DateTime.Now - seenTimestamp).Duration() <= TimeSpan.FromMinutes(5))
                            {
                                _log.LogInformation($"Mail with subject '{eMail.message.Subject}' and ID '{eMail.id}' already processed at '{seenTimestamp}'. Ignoring...");
                                continue;
                            }

                            if (shouldBeIgnoredSender)
                            {
                                _log.LogInformation($"Mail with subject '{eMail.message.Subject}' and sender '{sender}' failed sender-filter. Ignoring.");
                                continue;
                            }

                            if (shouldBeIgnoredSubject)
                            {
                                _log.LogInformation($"Mail with subject '{eMail.message.Subject}' failed subject-filter. Ignoring.");
                                continue;
                            }

                            _log.LogDebug("EMail passed filters.");

                            _seenMessages.Add(eMail.id, DateTime.Now);
                            eMailsToProcess.Add(eMail);
                            await client.MarkMessageSeenByUID(eMail.id, tickToken).ConfigureAwait(false);
                        }

                        foreach (var (message, id) in eMailsToProcess)
                        {
                            (MimeMessage, SiteModel) tuple = (message, site);
                            _eMailsObservable.OnNext(tuple);
                        }
                    }
                },
                async ex =>
                {
                    try
                    {
                        using var lockWaitCts = CreateBoundedToken(cancellationToken, LockWaitTimeout);

                        using (await clientLock.LockAsync(lockWaitCts.Token).ConfigureAwait(false))
                        {
                            _log.LogError(ex, $"Failed to fetch mails for site '{siteEmailSetting.Name}'.");
                            await ReconnectAsync(client, siteEmailSetting, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    catch (Exception otherEx)
                    {
                        _log.LogError(otherEx, $"Failed to handle exception from fetching mails for site '{siteEmailSetting.Name}'.");
                    }
                },
                () => _log.LogWarning($"Mail subscription for '{siteEmailSetting.Name}' completed unexpectedly."));

            _reconnectionSubscriptions.Add(reconnectionSubscription);
            _eMailsSubscriptions.Add(mailSubscription);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _eMailsObservable.OnCompleted();

            // Interrupts any in-flight polling/reconnect work bounded by CreateBoundedToken (see
            // StartPollingAsync/WireUpSubscriptions) instead of leaving it to run out its own local
            // timeout, and stops the retry-connect loop for any still-unreachable mailbox.
            _stoppingCts?.Cancel();

            foreach (var subscription in _eMailsSubscriptions)
            {
                subscription?.Dispose();
            }

            foreach (var subscription in _reconnectionSubscriptions)
            {
                subscription?.Dispose();
            }

            // Each client is disconnected in parallel, and only after acquiring its own lock (bounded
            // by the same shutdown timeout) - so shutdown never calls Disconnect concurrently with an
            // in-flight operation on the same non-thread-safe mail client. Still honors the host's own
            // shutdown token (the live "grace period ending" signal from HostOptions.ShutdownTimeout),
            // so a more urgent stop request cuts this short instead of being ignored.
            var disconnectTasks = _mailClients.Select(async entry =>
            {
                try
                {
                    using var disconnectCts = CreateBoundedToken(cancellationToken, ShutdownDisconnectTimeout);

                    using (await entry.Lock.LockAsync(disconnectCts.Token).ConfigureAwait(false))
                    {
                        await entry.Client.Disconnect(disconnectCts.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, $"Failed to cleanly disconnect '{entry.SiteName}' during shutdown.");
                }
            });

            await Task.WhenAll(disconnectTasks).ConfigureAwait(false);
        }

        public void Dispose()
        {
            foreach (var subscription in _eMailsSubscriptions)
            {
                subscription?.Dispose();
            }

            foreach (var subscription in _reconnectionSubscriptions)
            {
                subscription?.Dispose();
            }

            foreach (var entry in _mailClients)
            {
                entry.Client?.Dispose();
            }

            _stoppingCts?.Dispose();
        }

        private static CancellationTokenSource CreateBoundedToken(CancellationToken outer, TimeSpan? timeout = null)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
            cts.CancelAfter(timeout ?? MailOperationTimeouts.HungCallTimeout);
            return cts;
        }

        private async Task ReconnectAsync(IMailClient client, SiteEmailSetting siteEmailSetting, CancellationToken cancellationToken)
        {
            _log.LogInformation($"Reconnecting ({siteEmailSetting.Name})...");

            try
            {
                using var disconnectCts = CreateBoundedToken(cancellationToken);
                await client.Disconnect(disconnectCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // A connection broken enough to need reconnecting is often also broken enough that it
                // can't be disconnected cleanly - log and still attempt Connect, since that's what
                // actually matters for recovery, instead of leaving the mailbox disconnected until the
                // next scheduled attempt.
                _log.LogWarning(ex, $"Failed to cleanly disconnect '{siteEmailSetting.Name}' before reconnecting; attempting to connect anyway.");
            }

            using (var connectCts = CreateBoundedToken(cancellationToken))
            {
                await client.Connect(
                    siteEmailSetting.EMailHost,
                    siteEmailSetting.EMailPort,
                    siteEmailSetting.EMailUsername,
                    siteEmailSetting.EMailPassword,
                    connectCts.Token)
                .ConfigureAwait(false);
            }
        }

        private void ClearOutdatedAlreadySeenAt()
        {
            foreach (var item in _seenMessages.Where(kvp => (kvp.Value - DateTime.Now).Duration() > TimeSpan.FromMinutes(10)))
            {
                _seenMessages.Remove(item.Key);
            }
        }
    }
}
