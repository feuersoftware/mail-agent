using FeuerSoftware.MailAgent.Models;
using FeuerSoftware.MailAgent.Options;
using FeuerSoftware.MailAgent.Services;
using FeuerSoftware.MailAgent.Utility;
using Microsoft.Extensions.Options;
using MimeKit;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace FeuerSoftware.MailAgent.Processors
{
    public class TextMailProcessor : IMailProcessor
    {
        private readonly IPGPService _pGPService;
        private readonly MailAgentOptions _options;
        private readonly ILogger<TextMailProcessor> _log;

        public TextMailProcessor(
            [NotNull] IPGPService pgpService,
            [NotNull] IOptions<MailAgentOptions> options,
            [NotNull] ILogger<TextMailProcessor> log)
        {
            _pGPService = pgpService ?? throw new ArgumentNullException(nameof(pgpService));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public async Task ProcessMailAsync((MimeMessage message, SiteModel site) mail)
        {
            var encoding = Encoding.GetEncoding(1252);

            var encryptedPart = (MimePart)mail.message.BodyParts.Single(p => p.ContentType.MimeType == "application/octet-stream");

            using var bodyStream = new MemoryStream();
            encryptedPart.Content.WriteTo(bodyStream);
            bodyStream.Seek(0, SeekOrigin.Begin);

            string decryptedBase64;

            if (encryptedPart.ContentTransferEncoding == ContentEncoding.Base64)
            {
                using var streamReader = new StreamReader(bodyStream);
                var encryptedBase64 = await streamReader.ReadToEndAsync();
                var plainEncrypted = Convert.FromBase64String(encryptedBase64);

                decryptedBase64 = await _pGPService.DecryptWithGnupg(new MemoryStream(plainEncrypted));
            }
            else
            {
                decryptedBase64 = await _pGPService.DecryptWithGnupg(bodyStream);
            }

            _log.LogDebug($"Decrypted string is: '{decryptedBase64}'.");

            var parser = new MimeParser(new MemoryStream(Encoding.UTF8.GetBytes(decryptedBase64)));
            var entity = await parser.ParseMessageAsync();

            var textPart = (MimePart)entity.BodyParts.Single(p => p.ContentType.MimeType == "text/plain");

            var outputPath = Path.Combine(_options.OutputPath, $"Alarm_{DateTime.Now:yyyyMMddhhmmss}.txt");
            using var plainTextStream = new MemoryStream();
            await textPart.Content.WriteToAsync(plainTextStream);

            plainTextStream.Seek(0, SeekOrigin.Begin);

            using var sr = new StreamReader(plainTextStream);
            var contentQuotedPrintableEncoded = await sr.ReadToEndAsync();

            var decodedString = QuotedPrintableConverter.Decode(contentQuotedPrintableEncoded, encoding);

            await File.WriteAllTextAsync(outputPath, decodedString);

            _log.LogDebug($"Wrote decrypted string to file: '{decodedString}");
        }
    }
}
