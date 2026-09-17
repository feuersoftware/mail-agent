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
        private readonly MailAgentOptions _options;
        private readonly IMailClientFactory _mailClientFactory;
        private readonly ILogger<MailService> _log;
        private readonly Dictionary<string, DateTime> _seenMessages;
        private readonly Subject<(MimeMessage, SiteModel)> _eMailsObservable = new();
        private readonly List<IDisposable?> _eMailsSubscriptions = new();
        private readonly List<IDisposable?> _reconnectionSubscriptions = new();
        private readonly List<IMailClient> _mailClients = new();
        private static readonly TimeSpan ShutdownDisconnectTimeout = TimeSpan.FromSeconds(30);

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

            foreach (var siteEmailSetting in _options.EmailSettings)
            {
                var client = _mailClientFactory.CreateClient(siteEmailSetting);
                var clientLock = new AsyncLock();

                var site = new SiteModel()
                {
                    Name = siteEmailSetting.Name,
                    ApiKey = siteEmailSetting.ApiKey,
                };

                try
                {
                    using var initialConnectCts = CreateBoundedToken(cancellationToken);

                    await client.Connect(
                        siteEmailSetting.EMailHost,
                        siteEmailSetting.EMailPort,
                        siteEmailSetting.EMailUsername,
                        siteEmailSetting.EMailPassword,
                        initialConnectCts.Token);
                }
                catch (Exception ex)
                {
                    _log.LogCritical(ex, $"Failed to connect with mailserver. Using host '{siteEmailSetting.EMailHost}' and username '{siteEmailSetting.EMailUsername}'.");
                    continue;
                }

                var reconnectionSubscription = Observable
                    .Interval(TimeSpan.FromMinutes(60))
                    .SubscribeAsyncSafe(async _ =>
                    {
                        using var lockWaitCts = CreateBoundedToken(cancellationToken);

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
                            using var lockWaitCts = CreateBoundedToken(cancellationToken);

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

                _mailClients.Add(client);
                _reconnectionSubscriptions.Add(reconnectionSubscription);
                _eMailsSubscriptions.Add(mailSubscription);
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _eMailsObservable.OnCompleted();

            // Each client gets up to ShutdownDisconnectTimeout for a clean QUIT/disconnect handshake, but
            // still honors the host's own shutdown token - which is the live "grace period ending" signal
            // from the .NET Generic Host (governed by HostOptions.ShutdownTimeout), not a pre-cancelled
            // token - so a more urgent stop request still cuts this short instead of being ignored. Clients
            // are disconnected in parallel so total shutdown time doesn't scale with mailbox count.
            var disconnectTasks = _mailClients.Select(async client =>
            {
                using var disconnectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                disconnectCts.CancelAfter(ShutdownDisconnectTimeout);
                await client.Disconnect(disconnectCts.Token).ConfigureAwait(false);
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

            foreach (var client in _mailClients)
            {
                client?.Dispose();
            }
        }

        private static CancellationTokenSource CreateBoundedToken(CancellationToken outer)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
            cts.CancelAfter(MailOperationTimeouts.HungCallTimeout);
            return cts;
        }

        private async Task ReconnectAsync(IMailClient client, SiteEmailSetting siteEmailSetting, CancellationToken cancellationToken)
        {
            _log.LogInformation($"Reconnecting ({siteEmailSetting.Name})...");

            // Disconnect and Connect each get their own fresh bounded budget (derived from the raw,
            // unbounded outer token) rather than sharing one - otherwise a slow-but-successful Disconnect
            // could leave Connect with little to no time left, cancelling it mid-handshake and leaving the
            // mail client connected-but-unauthenticated until the next reconnect attempt.
            using (var disconnectCts = CreateBoundedToken(cancellationToken))
            {
                await client.Disconnect(disconnectCts.Token).ConfigureAwait(false);
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
