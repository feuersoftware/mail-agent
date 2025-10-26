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
                        .AddSingleton<IConnectApiClient, ConnectApiClient>();

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

                    switch (options.EMailMode)
                    {
                        case EMailMode.Imap:
                            services.AddTransient<IMailClient, ImapClient>();
                            break;
                        case EMailMode.Exchange:
                            services.AddTransient<IMailClient, ExchangeClient>();
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(MailAgentOptions.ProcessMode), "EMailMode not specified in settings.");
                    }

                    if (options.IgnoreCertificateErrors)
                    {
#pragma warning disable SYSLIB0014
                        ServicePointManager
                            .ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
#pragma warning restore SYSLIB0014
                    }
                });

            await hostBuilder.Build().RunAsync().ConfigureAwait(false);
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
