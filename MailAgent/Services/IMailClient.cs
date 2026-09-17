using MimeKit;

namespace FeuerSoftware.MailAgent.Services
{
    public interface IMailClient : IDisposable
    {
        Task Connect(string host, int port, string username, string password, CancellationToken cancellationToken = default);

        Task Disconnect();

        Task<IEnumerable<(MimeMessage message, string id)>> GetUnseenMails(CancellationToken cancellationToken = default);

        Task MarkMessageSeenByUID(string mailId, CancellationToken cancellationToken = default);
    }
}