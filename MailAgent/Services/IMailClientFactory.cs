using FeuerSoftware.MailAgent.Options;

namespace FeuerSoftware.MailAgent.Services
{
    public interface IMailClientFactory
    {
        IMailClient CreateClient(SiteEmailSetting settings);
    }
}
