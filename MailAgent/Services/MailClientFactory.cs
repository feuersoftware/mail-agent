using FeuerSoftware.MailAgent.Options;
using System.Diagnostics.CodeAnalysis;

namespace FeuerSoftware.MailAgent.Services
{
    public class MailClientFactory : IMailClientFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<MailClientFactory> _log;
        private readonly MailAgentOptions _options;

        public MailClientFactory(
            [NotNull] IServiceProvider serviceProvider,
            [NotNull] ILogger<MailClientFactory> log,
            [NotNull] Microsoft.Extensions.Options.IOptions<MailAgentOptions> options)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        }

        public IMailClient CreateClient(SiteEmailSetting settings)
        {
            _log.LogDebug($"Creating mail client for {settings.Name} with authentication type {settings.AuthenticationType}");

            // For O365, always use O365MailClient regardless of EMailMode
            if (settings.AuthenticationType == AuthenticationType.O365)
            {
                var authService = _serviceProvider.GetRequiredService<IAuthenticationService>();
                var logger = _serviceProvider.GetRequiredService<ILogger<O365MailClient>>();
                return new O365MailClient(logger, authService);
            }

            // For Basic authentication, use the global EMailMode setting
            switch (_options.EMailMode)
            {
                case EMailMode.Imap:
                    var imapLogger = _serviceProvider.GetRequiredService<ILogger<ImapClient>>();
                    return new ImapClient(imapLogger);

                case EMailMode.Exchange:
                    var exchangeLogger = _serviceProvider.GetRequiredService<ILogger<ExchangeClient>>();
                    return new ExchangeClient(exchangeLogger);

                default:
                    throw new ArgumentOutOfRangeException(nameof(settings.AuthenticationType), 
                        $"Unsupported EMailMode: {_options.EMailMode}");
            }
        }
    }
}
