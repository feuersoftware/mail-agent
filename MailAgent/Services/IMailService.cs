using FeuerSoftware.MailAgent.Models;
using MimeKit;

namespace FeuerSoftware.MailAgent.Services
{
    public interface IMailService
    {
        IObservable<(MimeMessage message, SiteModel site)> EMails { get; }

        Task StartPollingAsync(CancellationToken cancellationToken);

        Task StopAsync(CancellationToken cancellationToken);
    }
}