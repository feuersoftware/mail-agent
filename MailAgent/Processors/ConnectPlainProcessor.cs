using FeuerSoftware.MailAgent.Models;
using FeuerSoftware.MailAgent.Services;
using MimeKit;
using System.Diagnostics.CodeAnalysis;

namespace FeuerSoftware.MailAgent.Processors
{
    public class ConnectPlainProcessor : IMailProcessor
    {
        private readonly IConnectApiClient _connectApiClient;
        private readonly IConnectEvaluationService _evaluationService;

        public ConnectPlainProcessor(
            [NotNull] IConnectEvaluationService evaluationService,
            [NotNull] IConnectApiClient connectApiClient)
        {
            _connectApiClient = connectApiClient ?? throw new ArgumentNullException(nameof(connectApiClient));
            _evaluationService = evaluationService ?? throw new ArgumentNullException(nameof(evaluationService));
        }

        public async Task ProcessMailAsync((MimeMessage message, SiteModel site) siteMail)
        {
            var text = siteMail.message.GetTextBody(MimeKit.Text.TextFormat.Plain);

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("Part text/plain is null or whitespace.");
            }

            var operation = _evaluationService.Evaluate(text);

            await _connectApiClient.PublishOperation(operation, siteMail.site);
        }
    }
}
