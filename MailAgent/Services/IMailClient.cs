using MimeKit;

namespace FeuerSoftware.MailAgent.Services
{
    public interface IMailClient : IDisposable
    {
        Task Connect(string host, int port, string username, string password, CancellationToken cancellationToken = default);

        Task Disconnect(bool quit = true, CancellationToken cancellationToken = default);

        Task<IEnumerable<(MimeMessage message, string id)>> GetUnseenMails(MailFilter filter, CancellationToken cancellationToken = default);

        Task MarkMessageSeenByUID(string mailId, CancellationToken cancellationToken = default);
    }
}