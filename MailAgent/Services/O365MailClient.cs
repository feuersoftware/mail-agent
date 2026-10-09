using MailKit;
using MailKit.Security;
using MimeKit;
using System.Diagnostics.CodeAnalysis;

namespace FeuerSoftware.MailAgent.Services
{
    public class O365MailClient : IMailClient
    {
        private readonly MailKit.Net.Imap.ImapClient _client;
        private readonly ILogger<O365MailClient> _log;
        private readonly IAuthenticationService _authService;

        public O365MailClient(
            [NotNull] ILogger<O365MailClient> log,
            [NotNull] IAuthenticationService authService)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _client = new MailKit.Net.Imap.ImapClient();
        }

        public async Task Connect(string host, int port, string username, string password)
        {
            _log.LogDebug($"Connecting to O365 IMAP-Host '{host}' on port '{port}' with username '{username}' using OAuth2...");
            
            await _client.ConnectAsync(host, port, SecureSocketOptions.SslOnConnect);
            
            // Get OAuth2 access token
            var accessToken = await _authService.GetAccessTokenAsync(username);
            
            // Authenticate using OAuth2
            var oauth2 = new SaslMechanismOAuth2(username, accessToken);
            await _client.AuthenticateAsync(oauth2);
            
            _log.LogInformation($"Connected to O365 IMAP-Host '{host}' with username '{username}' using OAuth2.");
        }

        public async Task<IEnumerable<(MimeMessage message, string id)>> GetUnseenMails(MailFilter filter)
        {
            _log.LogDebug("Checking for unseen mails...");
            var eMails = new List<(MimeMessage message, string id)>();

            EnsureConnected();

            var inbox = _client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadOnly);

            var mailIds = await inbox.SearchAsync(MailKit.Search.SearchQuery.NotSeen);

            _log.LogDebug($"Found {mailIds.Count} unread mails.");

            if (mailIds.Count == 0)
            {
                return eMails;
            }

            // Fetch only headers first; download full messages only for mails that match the filters.
            var summaries = await inbox.FetchAsync(mailIds, MessageSummaryItems.Envelope | MessageSummaryItems.UniqueId);

            foreach (var summary in summaries)
            {
                var id = summary.UniqueId.Id.ToString();

                if (!filter.Matches(summary.Envelope?.Subject, summary.Envelope?.From.FirstOrDefault()?.ToString()))
                {
                    continue;
                }

                var mail = await inbox.GetMessageAsync(summary.UniqueId);

                eMails.Add((message: mail, id: id));
            }

            return eMails;
        }

        public async Task Disconnect()
        {
            await _client.DisconnectAsync(true);
            _log.LogInformation("O365 IMAP disconnected.");
        }

        public void Dispose()
        {
            _client.Dispose();
        }

        public async Task MarkMessageSeenByUID(string mailId)
        {
            EnsureConnected();
            var uid = Convert.ToUInt32(mailId);

            var inbox = _client.Inbox;

            await inbox.OpenAsync(FolderAccess.ReadWrite);

            await inbox.SetFlagsAsync(new UniqueId(uid), MessageFlags.Seen, default);

            _log.LogDebug($"Marked '{mailId}' as seen.");
        }

        private void EnsureConnected()
        {
            if (!_client.IsConnected || !_client.IsAuthenticated)
            {
                throw new InvalidOperationException("O365 IMAP-Client is not connected or not authenticated.");
            }
        }
    }
}
