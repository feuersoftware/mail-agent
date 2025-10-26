using FeuerSoftware.MailAgent.Models;
using FeuerSoftware.MailAgent.Services;
using MimeKit;
using System.Diagnostics.CodeAnalysis;

namespace FeuerSoftware.MailAgent.Processors
{
    public class ConnectPlainHtmlProcessor : IMailProcessor
    {
        private readonly IConnectApiClient _connectApiClient;
        private readonly IConnectEvaluationService _evaluationService;

        public ConnectPlainHtmlProcessor(
            [NotNull] IConnectEvaluationService evaluationService,
            [NotNull] IConnectApiClient connectApiClient)
        {
            _connectApiClient = connectApiClient ?? throw new ArgumentNullException(nameof(connectApiClient));
            _evaluationService = evaluationService ?? throw new ArgumentNullException(nameof(evaluationService));
        }

        public async Task ProcessMailAsync((MimeMessage message, SiteModel site) siteMail)
        {
            var text = siteMail.message.GetTextBody(MimeKit.Text.TextFormat.Html);

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("Part text/html is null or whitespace.");
            }

            var operation = _evaluationService.Evaluate(text);

            await _connectApiClient.PublishOperation(operation, siteMail.site);
        }
    }
}
