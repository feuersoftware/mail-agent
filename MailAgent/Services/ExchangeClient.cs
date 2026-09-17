using Microsoft.Exchange.WebServices.Data;
using MimeKit;
using System.Diagnostics;
using System.Net;

namespace FeuerSoftware.MailAgent.Services
{
    public class ExchangeClient : IMailClient
    {
        private readonly ILogger<ExchangeClient> _log;
        private readonly ExchangeService _exchangeService;
        private readonly PropertySet _customPropertySet = new(BasePropertySet.FirstClassProperties, ItemSchema.MimeContent);

        public ExchangeClient(ILogger<ExchangeClient> log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _exchangeService = new ExchangeService();
        }

        public System.Threading.Tasks.Task Connect(string host, int port, string username, string password, CancellationToken cancellationToken = default)
        {
            if (port != 443)
            {
                _log.LogWarning("Port is not set to default for HTTPS (443).");
            }

            _log.LogDebug($"Setting up connection to Exchange-Host '{host}' on port '{port}' with username '{username}'...");
            _exchangeService.Credentials = new NetworkCredential(username, password);
            _exchangeService.Url = new UriBuilder(scheme: "https", host, port, "/EWS/Exchange.asmx").Uri;

            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task Disconnect()
        {
            // Exchange Service has no long-term connection. We dont have to disconnect.
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public void Dispose()
        {
            // We have nothing to dispose here.
        }

        public async Task<IEnumerable<(MimeMessage message, string id)>> GetUnseenMails(CancellationToken cancellationToken = default)
        {
            try
            {
                var eMails = new List<(MimeMessage message, string id)>();
                _log.LogDebug("Checking for last 10 mails...");

                var sw1 = Stopwatch.StartNew();
                var result = await WithCancellation(_exchangeService.FindItems(WellKnownFolderName.Inbox, new ItemView(10)), cancellationToken);
                sw1.Stop();
                _log.LogDebug($"FindItems took '{sw1.ElapsedMilliseconds}ms'");

                var filteredItems = result.Items.OfType<EmailMessage>().Where(i => !i.IsRead);

                if (!filteredItems.Any())
                {
                    _log.LogDebug("Cant find any unseen mails.");
                    return eMails;
                }

                var sw2 = Stopwatch.StartNew();
                var getItemResponses = await WithCancellation(_exchangeService.BindToItems(filteredItems.Select(i => i.Id), _customPropertySet), cancellationToken);
                sw2.Stop();
                _log.LogDebug($"BindMessage took '{sw2.ElapsedMilliseconds}ms'");

                _log.LogDebug($"Found {filteredItems.Count()} items.");


                foreach (var item in getItemResponses.Select(r => r.Item).OfType<EmailMessage>())
                {
                    var messageData = item.MimeContent.Content;

                    MimeMessage message;

                    using (var stream = new MemoryStream(messageData, false))
                    {
                        message = await MimeMessage.LoadAsync(stream, cancellationToken);
                    }

                    eMails.Add((message, item.Id.UniqueId));
                }

                return eMails;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to fetch messages.");
                return new List<(MimeMessage message, string id)>();
            }
        }

        public async System.Threading.Tasks.Task MarkMessageSeenByUID(string mailId, CancellationToken cancellationToken = default)
        {
            try
            {
                var results = await WithCancellation(_exchangeService.FindItems(WellKnownFolderName.Inbox, new ItemView(5)), cancellationToken);

                var mail = results
                    .OfType<EmailMessage>()
                    .SingleOrDefault(r => r.Id.UniqueId == mailId);

                if (mail is null)
                {
                    _log.LogWarning($"Mail with Mail-ID '{mailId}' is not available anymore.");
                    return;
                }

                mail.IsRead = true;
                await WithCancellation(mail.Update(ConflictResolutionMode.AutoResolve), cancellationToken);

                _log.LogDebug($"Marked Mail with Mail-ID '{mailId}' as seen.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to mark message as seen.");
            }
        }

        /// <summary>
        /// Bounds an EWS call by <paramref name="cancellationToken"/>. The Exchange Web Services API used
        /// here has no CancellationToken-accepting overloads of its own, so a stuck call can't actually be
        /// aborted - but racing it against the token at least stops it from holding the caller's lock
        /// forever; the abandoned EWS call is left to complete or fail in the background.
        /// </summary>
        private static async System.Threading.Tasks.Task<T> WithCancellation<T>(System.Threading.Tasks.Task<T> task, CancellationToken cancellationToken)
        {
            var cancellationTask = System.Threading.Tasks.Task.Delay(Timeout.Infinite, cancellationToken);
            var completed = await System.Threading.Tasks.Task.WhenAny(task, cancellationTask).ConfigureAwait(false);
            if (completed == cancellationTask)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            return await task.ConfigureAwait(false);
        }
    }
}
