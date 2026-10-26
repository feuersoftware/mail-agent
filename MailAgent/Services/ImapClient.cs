using MailKit;
using MimeKit;
using System.Diagnostics.CodeAnalysis;

namespace FeuerSoftware.MailAgent.Services
{
    public class ImapClient : IMailClient
    {
        private readonly MailKit.Net.Imap.ImapClient _client;
        private readonly ILogger<ImapClient> _log;

        public ImapClient(
            [NotNull] ILogger<ImapClient> log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _client = new MailKit.Net.Imap.ImapClient { Timeout = (int)MailOperationTimeouts.Default.IoInactivity.TotalMilliseconds };
        }

        public async Task Connect(string host, int port, string username, string password, CancellationToken cancellationToken = default)
        {
            _log.LogDebug($"Connecting to IMAP-Host '{host}' on port '{port}' with username '{username}'...");
            // Longer socket timeout only for connect + AUTH (a slow but alive login must not be aborted at 15 s).
            _client.Timeout = (int)MailOperationTimeouts.Default.ConnectIoInactivity.TotalMilliseconds;
            try
            {
                await _client.ConnectAsync(host, port, MailKit.Security.SecureSocketOptions.SslOnConnect, cancellationToken);
                await _client.AuthenticateAsync(username, password, cancellationToken);
            }
            finally
            {
                _client.Timeout = (int)MailOperationTimeouts.Default.IoInactivity.TotalMilliseconds;
            }

            _log.LogInformation($"Connected to IMAP-Host '{host}' with username '{username}'.");
        }

        public async Task<IEnumerable<(MimeMessage message, string id)>> GetUnseenMails(MailFilter filter, CancellationToken cancellationToken = default)
        {
            _log.LogDebug("Checking for unseen mails...");
            var eMails = new List<(MimeMessage message, string id)>();

            EnsureConnected();

            var inbox = _client.Inbox;
            // ReadWrite (SELECT) for polling too: on Exchange Online a session that has ever issued SELECT
            // (MarkMessageSeenByUID) stops seeing new mails via EXAMINE + SEARCH, so never mix the two modes.
            await inbox.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

            var mailIds = await inbox.SearchAsync(MailKit.Search.SearchQuery.NotSeen, cancellationToken);

            _log.LogDebug($"Found {mailIds.Count} unread mails.");

            if (mailIds.Count == 0)
            {
                return eMails;
            }

            // Fetch only the raw header block first; download full messages only for mails that match the filters.
            var summaries = await inbox.FetchAsync(mailIds, MessageSummaryItems.Headers | MessageSummaryItems.UniqueId, cancellationToken);

            // The server may send unsolicited FETCH responses; only handle the unread mails we asked for.
            var requestedIds = new HashSet<UniqueId>(mailIds);
            var matchingIds = new List<UniqueId>();
            var ignored = new List<(UniqueId id, string? subject, string? sender)>();

            foreach (var summary in summaries.Where(s => s.UniqueId.IsValid && requestedIds.Contains(s.UniqueId)))
            {
                var (subject, sender) = MailFilter.GetSubjectAndSender(summary.Headers ?? await inbox.GetHeadersAsync(summary.UniqueId, cancellationToken));

                if (filter.Matches(subject, sender))
                {
                    matchingIds.Add(summary.UniqueId);
                }
                else
                {
                    ignored.Add((summary.UniqueId, subject, sender));
                }
            }

            // Mark ignored mails as read before downloading, so a failing download can't make them reappear (and be logged) on the next poll.
            if (ignored.Count > 0)
            {
                await inbox.AddFlagsAsync(ignored.Select(i => i.id).ToList(), MessageFlags.Seen, true, cancellationToken);

                foreach (var (_, subject, sender) in ignored)
                {
                    _log.LogInformation($"Mail with subject '{subject}' and sender '{sender}' does not match {filter}. Ignored and marked as read.");
                }
            }

            foreach (var id in matchingIds)
            {
                var mail = await inbox.GetMessageAsync(id, cancellationToken);

                eMails.Add((message: mail, id: id.Id.ToString()));
            }

            return eMails;
        }

        public async Task Disconnect(bool quit = true, CancellationToken cancellationToken = default)
        {
            await _client.DisconnectAsync(quit, cancellationToken);
            _log.LogInformation("Imap disconnected.");
        }

        public void Dispose()
        {
            _client.Dispose();
        }

        public async Task MarkMessageSeenByUID(string mailId, CancellationToken cancellationToken = default)
        {
            EnsureConnected();
            var uid = Convert.ToUInt32(mailId);

            var inbox = _client.Inbox;

            await inbox.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

            await inbox.SetFlagsAsync(new UniqueId(uid), MessageFlags.Seen, false, cancellationToken);

            _log.LogDebug($"Marked '{mailId}' as seen.");
        }

        private void EnsureConnected()
        {
            if (!_client.IsConnected || !_client.IsAuthenticated)
            {
                throw new InvalidOperationException("Imap-Client is not connected or not authenticated.");
            }
        }
    }
}
