using FeuerSoftware.MailAgent.Models;
using MimeKit;

namespace FeuerSoftware.MailAgent.Processors
{
    public interface IMailProcessor
    {
        Task ProcessMailAsync((MimeMessage message, SiteModel site) mail);
    }
}
