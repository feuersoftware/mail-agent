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

        public System.Threading.Tasks.Task Connect(string host, int port, string username, string password)
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

        public async Task<IEnumerable<(MimeMessage message, string id)>> GetUnseenMails()
        {
            try
            {
                var eMails = new List<(MimeMessage message, string id)>();
                _log.LogDebug("Checking for last 10 mails...");

                var sw1 = Stopwatch.StartNew();
                var result = await _exchangeService.FindItems(WellKnownFolderName.Inbox, new ItemView(10));
                sw1.Stop();
                _log.LogDebug($"FindItems took '{sw1.ElapsedMilliseconds}ms'");

                var filteredItems = result.Items.OfType<EmailMessage>().Where(i => !i.IsRead);

                if (!filteredItems.Any())
                {
                    _log.LogDebug("Cant find any unseen mails.");
                    return eMails;
                }

                var sw2 = Stopwatch.StartNew();
                var getItemResponses = await _exchangeService.BindToItems(filteredItems.Select(i => i.Id), _customPropertySet);
                sw2.Stop();
                _log.LogDebug($"BindMessage took '{sw2.ElapsedMilliseconds}ms'");

                _log.LogDebug($"Found {filteredItems.Count()} items.");


                foreach (var item in getItemResponses.Select(r => r.Item).OfType<EmailMessage>())
                {
                    var messageData = item.MimeContent.Content;

                    MimeMessage message;

                    using (var stream = new MemoryStream(messageData, false))
                    {
                        message = await MimeMessage.LoadAsync(stream);
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

        public async System.Threading.Tasks.Task MarkMessageSeenByUID(string mailId)
        {
            try
            {
                var results = await _exchangeService.FindItems(WellKnownFolderName.Inbox, new ItemView(5));

                var mail = results
                    .OfType<EmailMessage>()
                    .SingleOrDefault(r => r.Id.UniqueId == mailId);

                if (mail is null)
                {
                    _log.LogWarning($"Mail with Mail-ID '{mailId}' is not available anymore.");
                    return;
                }

                mail.IsRead = true;
                await mail.Update(ConflictResolutionMode.AutoResolve);

                _log.LogDebug($"Marked Mail with Mail-ID '{mailId}' as seen.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to mark message as seen.");
            }
        }
    }
}
