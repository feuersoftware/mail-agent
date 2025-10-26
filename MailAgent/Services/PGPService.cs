using FeuerSoftware.MailAgent.Options;
using Libgpgme;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;

namespace FeuerSoftware.MailAgent.Services
{
    public class PGPService : IPGPService
    {
        private readonly MailAgentOptions _options;
        private readonly ILogger<PGPService> _log;

        public PGPService(
            [NotNull] IOptions<MailAgentOptions> options,
            [NotNull] ILogger<PGPService> log)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public async Task<string> DecryptWithGnupg(Stream encryptedData)
        {
            if (encryptedData is null || encryptedData.Length <= 0)
            {
                throw new ArgumentNullException(nameof(encryptedData));
            }

            _log.LogDebug("EncryptedDataStream is {@Data}", encryptedData);

            using Context context = new()
            {
                PinentryMode = PinentryMode.Loopback,
            };

            context.SetPassphraseFunction(GetPassphraseFromConfig);

            using var cipher = new GpgmeStreamData(encryptedData);
            cipher.Seek(0, SeekOrigin.Begin);

            using var decryptedText = new GpgmeMemoryData();

            _log.LogDebug("Decrypting...");

            var decryptionResult = context.Decrypt(cipher, decryptedText);

            _log.LogDebug("Decryption done FileName: '{@FileName}' Recipients: '{@Recipients}'", decryptionResult.FileName, decryptionResult.Recipients);

            decryptedText.Seek(0, SeekOrigin.Begin);

            using var srResult = new StreamReader(decryptedText);
            var plaintext = await srResult.ReadToEndAsync();

            _log.LogDebug($"Plain text is: '{plaintext}'");

            return plaintext;
        }

        public PassphraseResult GetPassphraseFromConfig(
               Context context,
               PassphraseInfo info,
               ref char[] passphrase)
        {
            _log.LogDebug("PassphaseInfo is {@Info}", info);

            passphrase = _options.SecretKeyPassphrase.ToCharArray();

            return PassphraseResult.Success;
        }
    }
}
