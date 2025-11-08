using Destructurama;
using FeuerSoftware.MailAgent.Options;
using FeuerSoftware.MailAgent.Processors;
using FeuerSoftware.MailAgent.Services;
using Serilog;
using System.Diagnostics;
using System.Net;
using Polly;
using Polly.Extensions.Http;
using System.Text;

namespace FeuerSoftware.MailAgent
{
    public static class Program
    {
        public static async Task Main()
        {
            PrintLogo();
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var hostBuilder = new HostBuilder()
                .UseWindowsService()
                .ConfigureAppConfiguration((hostContext, configuration) =>
                {
                    if (Debugger.IsAttached)
                    {
                        configuration.AddUserSecrets("FeuerSoftware_MailAgent");
                        Console.WriteLine("Expecting configuration from UserSecrets with ID 'FeuerSoftware_MailAgent'.");
                    }
                    else
                    {
                        configuration
                            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false, reloadOnChange: true);
                    }
                })
                .UseSerilog((hostContext, configuration) =>
                {
                    configuration.ReadFrom.Configuration(hostContext.Configuration);
                    configuration.Enrich.WithProperty("Environment", hostContext.HostingEnvironment.EnvironmentName);
                    configuration.Destructure.ToMaximumDepth(5);
                    configuration.Destructure.ToMaximumStringLength(20);
                    configuration.Destructure.UsingAttributes();
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services
                        .Configure<MailAgentOptions>(hostContext.Configuration.GetRequiredSection(MailAgentOptions.SectionName))
                        .Configure<ConnectPatternOptions>(hostContext.Configuration.GetRequiredSection(ConnectPatternOptions.SectionName))
                        .AddHostedService<Agent>()
                        .AddHostedService<HeartbeatService>()
                        .AddSingleton<IMailService, MailService>()
                        .AddSingleton<IPGPService, PGPService>()
                        .AddSingleton<IConnectEvaluationService, ConnectEvaluationService>()
                        .AddSingleton<IConnectApiClient, ConnectApiClient>()
                        .AddSingleton<IMailClientFactory, MailClientFactory>()
                        .AddSingleton<ITokenStorageService, TokenStorageService>()
                        .AddSingleton<IAuthenticationService, O365AuthenticationService>()
                        .AddSingleton<ConfigurationValidator>()
                        .AddSingleton<O365AuthenticationGuide>();

                    services
                       .AddHttpClient<HeartbeatService>(c =>
                       {
                           c.Timeout = TimeSpan.FromSeconds(100); // Needs to be this high
                       })
                       .AddPolicyHandler(HttpPolicyExtensions
                           .HandleTransientHttpError()
                           .WaitAndRetryAsync(2, retryAttempt => TimeSpan.FromSeconds(Math.Pow(5, retryAttempt))));

                    var options = hostContext.Configuration.GetRequiredSection(MailAgentOptions.SectionName).Get<MailAgentOptions>()!;

                    switch (options.ProcessMode)
                    {
                        case ProcessMode.Pdf:
                            services.AddSingleton<IMailProcessor, PdfMailProcessor>();
                            break;
                        case ProcessMode.Text:
                            services.AddSingleton<IMailProcessor, TextMailProcessor>();
                            break;
                        case ProcessMode.ConnectPlain:
                            services.AddSingleton<IMailProcessor, ConnectPlainProcessor>();
                            break;
                        case ProcessMode.ConnectEncrypted:
                            services.AddSingleton<IMailProcessor, ConnectEncryptedProcessor>();
                            break;
                        case ProcessMode.ConnectPgpAttachment:
                            services.AddSingleton<IMailProcessor, PgpAttachmentProcessor>();
                            break;
                        case ProcessMode.ConnectPlainHtml:
                            services.AddSingleton<IMailProcessor, ConnectPlainHtmlProcessor>();
                            break;
                        case ProcessMode.ConnectEncryptedHtml:
                            services.AddSingleton<IMailProcessor, ConnectEncryptedHtmlProcessor>();
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(MailAgentOptions.ProcessMode), "ProcessMode not specified in settings.");
                    }

                    if (options.IgnoreCertificateErrors)
                    {
#pragma warning disable SYSLIB0014
                        ServicePointManager
                            .ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
#pragma warning restore SYSLIB0014
                    }
                });

            var host = hostBuilder.Build();

            // Display configuration summary and validate settings
            var configValidator = host.Services.GetRequiredService<ConfigurationValidator>();
            var options = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MailAgentOptions>>().Value;
            
            configValidator.PrintConfigurationSummary();
            
            var issues = configValidator.ValidateConfiguration();
            configValidator.PrintValidationResults(issues);

            // Stop if there are critical configuration errors
            var hasErrors = issues.Any(i => i.Severity == IssueSeverity.Error);
            if (hasErrors)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("✗ Cannot start application due to configuration errors.");
                Console.WriteLine("  Please fix the errors above and restart the application.");
                Console.ResetColor();
                Console.WriteLine();
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return;
            }

            // Handle O365 authentication if needed
            var o365Mailboxes = options.EmailSettings
                .Where(s => s.AuthenticationType == Options.AuthenticationType.O365)
                .ToList();

            if (o365Mailboxes.Any())
            {
                var authGuide = host.Services.GetRequiredService<O365AuthenticationGuide>();
                var success = await authGuide.GuideUserThroughAuthenticationAsync(o365Mailboxes, CancellationToken.None).ConfigureAwait(false);
                
                if (!success)
                {
                    Console.WriteLine("Press any key to exit...");
                    Console.ReadKey();
                    return;
                }
            }

            // Start the application
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
            Console.WriteLine("  ✓ Starting MailAgent...");
            Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
            Console.ResetColor();
            Console.WriteLine();

            await host.RunAsync().ConfigureAwait(false);
        }

        private static void PrintLogo()
        {
            var assembly = typeof(Program).Assembly;

            string Logo = $@"
                   ,,
                  ,,,,*
                *,,,,,
               ,,,,,,,   .
             ,**,,,,,,   ,,
            **** ,,,,,   ,,,,
           /**** ,,,,,,   ,,,,                          #) Verbindungen per IMAP oder Exchange EWS möglich
           //**   ,,,,,,   ,,,,,
       (   ///**   ,,,,,,   *,,,,                       #) Konfiguration erfolgt in der Datei appsettings.json
      ((   ///**    ,,,,,,.  ,,,,,                         Prozessoren: Pdf (mit PGP), Text (mit PGP) und 
      ((    ///**,    ,,,,,,  ,,,,                         ConnectPlain (ohne PGP)
      #((    ///***    ,,,,,* ,,,,                      
      #(((*   *//***    ,,,,, ,,,,                      #) Für den Betrieb mit PGP muss das Tool GnuPG installiert sein
       #((((    /****    ,,,,,,,,                          und die entsprechenden Schlüssel in Kleopatra importiert sein.
         ((((/   /****    ,,,,,,                           
          ((((//  /****   ,,,,
            (((//  /***   ,,,                           #) Es können mehrere Mailpostfächer parallel überwacht werden.
             (((// /***   ,                                             
               (/////*                                             Version: {assembly.GetName().Version} 
                (/////                                  MailAgent für unbegrenzt viele Standorte und Organisationen
                  ////                                  Feuer Software GmbH | Karlsbaderstr. 16 | 65760 Eschborn
                   //";

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(Logo);

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine();
        }
    }
}
