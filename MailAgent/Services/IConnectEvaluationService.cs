using FeuerSoftware.MailAgent.Models;

namespace FeuerSoftware.MailAgent.Services
{
    public interface IConnectEvaluationService
    {
        OperationModel Evaluate(string text);
    }
}