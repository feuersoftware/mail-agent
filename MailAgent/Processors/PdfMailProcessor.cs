using FeuerSoftware.MailAgent.Models;
using FeuerSoftware.MailAgent.Options;
using FeuerSoftware.MailAgent.Services;
using Microsoft.Extensions.Options;
using MimeKit;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace FeuerSoftware.MailAgent.Processors
{
    public class PdfMailProcessor : IMailProcessor
    {
        private readonly IPGPService _pGPService;
        private readonly MailAgentOptions _options;
        private readonly ILogger<PdfMailProcessor> _log;

        public PdfMailProcessor(
            [NotNull] IPGPService pgpService,
            [NotNull] IOptions<MailAgentOptions> options,
            [NotNull] ILogger<PdfMailProcessor> log)
        {
            _pGPService = pgpService ?? throw new ArgumentNullException(nameof(pgpService));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public async Task ProcessMailAsync((MimeMessage message, SiteModel site) siteMail)
        {
            var encryptedPart = (MimePart)siteMail.message.BodyParts.Single(p => p.ContentType.MimeType == "application/octet-stream");

            using var bodyStream = new MemoryStream();
            encryptedPart.Content.WriteTo(bodyStream);
            bodyStream.Seek(0, SeekOrigin.Begin);

            string decryptedBase64;

            if (encryptedPart.ContentTransferEncoding == ContentEncoding.Base64)
            {
                using var sr = new StreamReader(bodyStream);
                var encryptedBase64 = await sr.ReadToEndAsync();
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

            var pdfPart = (MimePart)entity.BodyParts.Single(p => p.ContentType.MimeType == "application/octet-stream");

            using var pdfStream = new MemoryStream();
            pdfPart.Content.WriteTo(pdfStream);
            pdfStream.Seek(0, SeekOrigin.Begin);

            var pdfBytes = Convert.FromBase64String(new StreamReader(pdfStream).ReadToEnd());
            var outputPath = Path.Combine(_options.OutputPath, $"Alarmdruck_{DateTime.Now:yyyyMMddhhmmss}.pdf");
            using var pdfFileStream = new FileStream(outputPath, FileMode.CreateNew);

            using var writer = new BinaryWriter(pdfFileStream);
            writer.Write(pdfBytes, 0, pdfBytes.Length);
            writer.Close();
        }
    }
}
