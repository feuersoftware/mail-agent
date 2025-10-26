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
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<MailService> _log;
        private readonly Dictionary<string, DateTime> _seenMessages;
        private readonly Subject<(MimeMessage, SiteModel)> _eMailsObservable = new();
        private readonly List<IDisposable?> _eMailsSubscriptions = new();
        private readonly List<IDisposable?> _reconnectionSubscriptions = new();
        private readonly List<IMailClient> _mailClients = new();
        private readonly List<IDisposable?> _scopes = new();
        private readonly AsyncLock _asyncLock = new();
        private IDisposable? _isLocked;

        public MailService(
            [NotNull] IServiceProvider serviceProvider,
            [NotNull] IOptions<MailAgentOptions> options,
            [NotNull] ILogger<MailService> log)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _seenMessages = new Dictionary<string, DateTime>();
        }

        public IObservable<(MimeMessage, SiteModel)> EMails => _eMailsObservable.AsObservable();

        public async Task StartPollingAsync(CancellationToken cancellationToken)
        {
            foreach (var siteEmailSetting in _options.EmailSettings)
            {
                var scope = _serviceProvider.CreateScope();
                var client = scope.ServiceProvider.GetRequiredService<IMailClient>();

                var site = new SiteModel()
                {
                    Name = siteEmailSetting.Name,
                    ApiKey = siteEmailSetting.ApiKey,
                };

                try
                {
                    await client.Connect(
                        siteEmailSetting.EMailHost,
                        siteEmailSetting.EMailPort,
                        siteEmailSetting.EMailUsername,
                        siteEmailSetting.EMailPassword);
                }
                catch (Exception ex)
                {
                    _log.LogCritical(ex, $"Failed to connect with mailserver. Using host '{siteEmailSetting.EMailHost}' and username '{siteEmailSetting.EMailUsername}'.");
                    return;
                }

                var reconnectionSubscription = Observable
                    .Interval(TimeSpan.FromMinutes(60))
                    .SubscribeAsyncSafe(async _ =>
                    {
                        using (_isLocked = await _asyncLock.LockAsync())
                        {
                            _log.LogInformation($"Reconnecting ({siteEmailSetting.Name})...");
                            await client.Disconnect().ConfigureAwait(false);
                            await client.Connect(
                                siteEmailSetting.EMailHost,
                                siteEmailSetting.EMailPort,
                                siteEmailSetting.EMailUsername,
                                siteEmailSetting.EMailPassword)
                            .ConfigureAwait(false);
                        }
                    },
                    e =>
                    {
                        _log.LogError(e, "Failed to reconnect.");
                        _isLocked?.Dispose();
                    },
                    () => _log.LogDebug("Reconnection subscription completed."));

                if (_options.EMailPollingIntervalSeconds < 4)
                {
                    throw new ArgumentOutOfRangeException("Setting EMailPollingIntervalSeconds lower than 4 is not supported!");
                }

                var mailSubscription = Observable
                    .Interval(TimeSpan.FromSeconds(_options.EMailPollingIntervalSeconds))
                    .TakeWhile(x => !cancellationToken.IsCancellationRequested)
                    .SubscribeAsyncSafe(async x =>
                    {
                        using (_isLocked = await _asyncLock.LockAsync())
                        {
                            ClearOutdatedAlreadySeenAt();

                            var eMails = await client.GetUnseenMails().ConfigureAwait(false);

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
                                if ((DateTimeOffset.Now - eMail.message.Date).Duration() > TimeSpan.FromMinutes(15))
                                {
                                    _log.LogInformation($"Mail with subject '{eMail.message.Subject}' received delayed. EMail was sent at {eMail.message.Date.ToLocalTime()}. Ignore and mark as read.");

                                    await client.MarkMessageSeenByUID(eMail.id).ConfigureAwait(false);
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
                                await client.MarkMessageSeenByUID(eMail.id).ConfigureAwait(false);
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
                            _isLocked?.Dispose();

                            using (_isLocked = await _asyncLock.LockAsync())
                            {
                                _log.LogError(ex, $"Failed to fetch mails for site '{siteEmailSetting.Name}'.");
                                _log.LogInformation($"Reconnecting ({siteEmailSetting.Name})...");
                                await client.Disconnect().ConfigureAwait(false);
                                await client.Connect(
                                    siteEmailSetting.EMailHost,
                                    siteEmailSetting.EMailPort,
                                    siteEmailSetting.EMailUsername,
                                    siteEmailSetting.EMailPassword)
                                .ConfigureAwait(false);
                            }
                        }
                        catch (Exception otherEx)
                        {
                            _isLocked?.Dispose();
                            _log.LogError(otherEx, "Failed to handle Exception from fetching Mails.");
                        }
                    },
                    () => _log.LogDebug("MailSubscription completed."));

                _scopes.Add(scope);
                _mailClients.Add(client);
                _reconnectionSubscriptions.Add(reconnectionSubscription);
                _eMailsSubscriptions.Add(mailSubscription);
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _eMailsObservable.OnCompleted();

            foreach (var client in _mailClients)
            {
                await client.Disconnect().ConfigureAwait(false);
            }
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

            foreach (var scope in _scopes)
            {
                scope?.Dispose();
            }

            _isLocked?.Dispose();
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
