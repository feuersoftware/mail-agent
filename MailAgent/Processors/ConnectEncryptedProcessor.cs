using FeuerSoftware.MailAgent.Extensions;
using FeuerSoftware.MailAgent.Models;
using FeuerSoftware.MailAgent.Services;
using FeuerSoftware.MailAgent.Utility;
using MimeKit;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace FeuerSoftware.MailAgent.Processors
{
    public class ConnectEncryptedProcessor : IMailProcessor
    {
        private readonly IPGPService _pGPService;
        private readonly ILogger<TextMailProcessor> _log;
        private readonly IConnectEvaluationService _evaluationService;
        private readonly IConnectApiClient _connectApiClient;
        private readonly ConnectPlainProcessor _fallbackProcessor;

        public ConnectEncryptedProcessor(
            [NotNull] IPGPService pgpService,
            [NotNull] ILogger<TextMailProcessor> log,
            [NotNull] IConnectEvaluationService evaluationService,
            [NotNull] IConnectApiClient connectApiClient)
        {
            _pGPService = pgpService ?? throw new ArgumentNullException(nameof(pgpService));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _evaluationService = evaluationService ?? throw new ArgumentNullException(nameof(evaluationService));
            _connectApiClient = connectApiClient ?? throw new ArgumentNullException(nameof(connectApiClient));
            _fallbackProcessor = new(evaluationService, connectApiClient);
        }

        public async Task ProcessMailAsync((MimeMessage message, SiteModel site) siteMail)
        {
            try
            {
                var encryptedPart = (MimePart)siteMail.message.BodyParts.Single(p => p.ContentType.MimeType == "application/octet-stream");

                if (encryptedPart is null)
                {
                    throw new InvalidCastException("Cant find any encrypted part.");
                }

                _log.LogDebug("Extracted encrypted part is {@EncryptedPart}", encryptedPart);

                var encoding = encryptedPart.ContentType.Charset.GuessEncoding();

                using var bodyStream = new MemoryStream();
                encryptedPart.Content.WriteTo(bodyStream);
                bodyStream.Seek(0, SeekOrigin.Begin);

                string decryptedMessage;

                if (encryptedPart.ContentTransferEncoding == ContentEncoding.Base64)
                {
                    using var streamReader = new StreamReader(bodyStream);
                    var encryptedBase64 = await streamReader.ReadToEndAsync();
                    var plainEncrypted = Convert.FromBase64String(encryptedBase64);

                    decryptedMessage = await _pGPService.DecryptWithGnupg(new MemoryStream(plainEncrypted));
                }
                else
                {
                    decryptedMessage = await _pGPService.DecryptWithGnupg(bodyStream);
                }

                _log.LogDebug($"Decrypted message is: '{decryptedMessage}'.");

                using var decryptedStream = new MemoryStream(Encoding.UTF8.GetBytes(decryptedMessage));
                var parser = new MimeParser(decryptedStream);
                var entity = await parser.ParseMessageAsync();

                var textPart = (MimePart)entity.BodyParts.Single(p => p.ContentType.MimeType == "text/plain");

                using var plainTextStream = new MemoryStream();
                await textPart.Content.WriteToAsync(plainTextStream);

                plainTextStream.Seek(0, SeekOrigin.Begin);

                using var sr = new StreamReader(plainTextStream);
                var contentQuotedPrintableEncoded = await sr.ReadToEndAsync();

                var decryptedPartEncoding = textPart.ContentType.Charset.GuessEncoding();

                var decodedString = QuotedPrintableConverter.Decode(contentQuotedPrintableEncoded, decryptedPartEncoding);

                var operation = _evaluationService.Evaluate(decodedString);

                await _connectApiClient.PublishOperation(operation, siteMail.site);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to use ConnectEncryptedProcessor.");
                _log.LogInformation("Trying ConnectPlainProcessor as fallback.");
                await _fallbackProcessor.ProcessMailAsync(siteMail);
            }
        }
    }
}
