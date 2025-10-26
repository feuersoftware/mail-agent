using FeuerSoftware.MailAgent.Models;

namespace FeuerSoftware.MailAgent.Services
{
    public interface IConnectApiClient
    {
        Task PublishOperation(OperationModel operation, SiteModel site);
    }
}