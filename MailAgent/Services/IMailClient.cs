using MimeKit;

namespace FeuerSoftware.MailAgent.Services
{
    public interface IMailClient : IDisposable
    {
        Task Connect(string host, int port, string username, string password);

        Task Disconnect();

        Task<IEnumerable<(MimeMessage message, string id)>> GetUnseenMails(MailFilter filter);

        Task MarkMessageSeenByUID(string mailId);
    }
}