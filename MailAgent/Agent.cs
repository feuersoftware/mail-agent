using FeuerSoftware.MailAgent.Extensions;
using FeuerSoftware.MailAgent.Models;
using FeuerSoftware.MailAgent.Options;
using FeuerSoftware.MailAgent.Processors;
using FeuerSoftware.MailAgent.Services;
using Microsoft.Extensions.Options;
using MimeKit;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace FeuerSoftware.MailAgent
{
    public class Agent : BackgroundService, IDisposable
    {
        private readonly IMailProcessor _mailProcessor;
        private readonly IMailService _mailService;
        private readonly MailAgentOptions _options;
        private readonly ILogger<Agent> _log;
        private IDisposable? _mailSubscription;

        public Agent(
            [NotNull] IMailProcessor mailProcessor,
            [NotNull] IMailService mailService,
            [NotNull] IOptions<MailAgentOptions> options,
            [NotNull] ILogger<Agent> log)
        {
            _mailProcessor = mailProcessor ?? throw new ArgumentNullException(nameof(mailProcessor));
            _mailService = mailService ?? throw new ArgumentNullException(nameof(mailService));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            _log.LogInformation("Starting agent...");
            _log.LogInformation("Using following options from appsettings.json: {@Options}", _options);

            try
            {
                InitMails();

                await _mailService.StartPollingAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _log.LogCritical(ex, "Failed to start agent.");
                Environment.Exit(1);
            }

            _log.LogInformation("Agent started.");
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await _mailService.StopAsync(cancellationToken);
            _log.LogInformation("Agent stopped.");
        }

        public new void Dispose()
        {
            _mailSubscription?.Dispose();
        }

        private void InitMails()
        {
            _mailSubscription = _mailService.EMails.SubscribeAsyncSafe<(MimeMessage message, SiteModel site)>(async siteMail =>
            {
                _log.LogInformation($"Agent processing mail for site '{siteMail.site.Name}'.");
                _log.LogDebug("Agent processing {@Mail}", siteMail.message);

                try
                {
                    var sw = Stopwatch.StartNew();
                    await _mailProcessor.ProcessMailAsync(siteMail);
                    sw.Stop();
                    _log.LogDebug($"Processing Mail took {sw.ElapsedMilliseconds}ms.");
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Failed to process mail '{@Mail}'.", siteMail);
                }
            },
            _log.LogAndContinue("Failed to process mails."),
            () => _log.LogWarning("EMails subscription completed unexpectedly."));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Dead code under the .NET Generic Host today: StartAsync/StopAsync above are already
            // full IHostedService overrides that don't call base.StartAsync/base.StopAsync, so
            // BackgroundService never schedules this method via Task.Run - the host calls
            // StartAsync/StopAsync directly instead. If this override structure is ever "cleaned up"
            // to rely on ExecuteAsync again, MailService.StopAsync would then sometimes receive an
            // already-cancelled stoppingToken here (this loop only calls StopAsync after its own
            // token is already cancelled), reintroducing the "disconnect aborted before a clean QUIT
            // handshake" failure mode MailService.StopAsync's own cancellation handling exists to
            // avoid on the real, host-driven shutdown path.
            await StartAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                Thread.Sleep(TimeSpan.FromSeconds(30));
            }

            await StopAsync(stoppingToken);
        }
    }
}
