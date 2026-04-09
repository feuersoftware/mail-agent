using System.Diagnostics;
using System.Net;
using System.Text;
using Destructurama;
using FeuerSoftware.MailAgent.Data;
using FeuerSoftware.MailAgent.Endpoints;
using FeuerSoftware.MailAgent.Options;
using FeuerSoftware.MailAgent.Processors;
using FeuerSoftware.MailAgent.Services;
using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.Extensions.Http;
using Serilog;

namespace FeuerSoftware.MailAgent
{
    public static class Program
    {
        private const string DatabaseFileName = "settings.db";

        public static async Task Main(string[] args)
        {
            PrintLogo();
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var dbPath = Path.Combine(Directory.GetCurrentDirectory(), DatabaseFileName);
            var connectionString = $"Data Source={dbPath}";

            var builder = WebApplication.CreateBuilder(args);

            // Support running as Windows Service
            builder.Host.UseWindowsService();

            // Database configuration source (layered on top of appsettings.json)
            builder.Configuration.AddDatabaseConfiguration(connectionString);

            if (Debugger.IsAttached)
            {
                builder.Configuration.AddUserSecrets("FeuerSoftware_MailAgent");
            }

            // Serilog
            builder.Host.UseSerilog((hostContext, configuration) =>
            {
                configuration.ReadFrom.Configuration(hostContext.Configuration);
                configuration.Enrich.WithProperty("Environment", hostContext.HostingEnvironment.EnvironmentName);
                configuration.Destructure.ToMaximumDepth(5);
                configuration.Destructure.ToMaximumStringLength(20);
                configuration.Destructure.UsingAttributes();
            });

            // Core services
            builder.Services
                .AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString), ServiceLifetime.Scoped)
                .Configure<MailAgentOptions>(builder.Configuration.GetSection(MailAgentOptions.SectionName))
                .Configure<ConnectPatternOptions>(builder.Configuration.GetSection(ConnectPatternOptions.SectionName))
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

            builder.Services
                .AddHttpClient<HeartbeatService>(c =>
                {
                    c.Timeout = TimeSpan.FromSeconds(100);
                })
                .AddPolicyHandler(HttpPolicyExtensions
                    .HandleTransientHttpError()
                    .WaitAndRetryAsync(2, retryAttempt => TimeSpan.FromSeconds(Math.Pow(5, retryAttempt))));

            // Register the right IMailProcessor based on ProcessMode config
            var processModeStr = builder.Configuration["MailAgentOptions:ProcessMode"] ?? "ConnectPlain";
            if (Enum.TryParse<ProcessMode>(processModeStr, out var processMode))
            {
                switch (processMode)
                {
                    case ProcessMode.Pdf:
                        builder.Services.AddSingleton<IMailProcessor, PdfMailProcessor>();
                        break;
                    case ProcessMode.Text:
                        builder.Services.AddSingleton<IMailProcessor, TextMailProcessor>();
                        break;
                    case ProcessMode.ConnectEncrypted:
                        builder.Services.AddSingleton<IMailProcessor, ConnectEncryptedProcessor>();
                        break;
                    case ProcessMode.ConnectPgpAttachment:
                        builder.Services.AddSingleton<IMailProcessor, PgpAttachmentProcessor>();
                        break;
                    case ProcessMode.ConnectPlainHtml:
                        builder.Services.AddSingleton<IMailProcessor, ConnectPlainHtmlProcessor>();
                        break;
                    case ProcessMode.ConnectEncryptedHtml:
                        builder.Services.AddSingleton<IMailProcessor, ConnectEncryptedHtmlProcessor>();
                        break;
                    case ProcessMode.ConnectPlain:
                    default:
                        builder.Services.AddSingleton<IMailProcessor, ConnectPlainProcessor>();
                        break;
                }
            }
            else
            {
                builder.Services.AddSingleton<IMailProcessor, ConnectPlainProcessor>();
            }

            // Handle certificate errors
            var ignoreCerts = builder.Configuration.GetValue<bool>("MailAgentOptions:IgnoreCertificateErrors");
            if (ignoreCerts)
            {
#pragma warning disable SYSLIB0014
                ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
#pragma warning restore SYSLIB0014
            }

            // Swagger / OpenAPI
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // CORS: allow localhost origins for development (SPA proxy) and the same host in production.
            // This service runs locally as a Windows Service / daemon and is not exposed to the internet.
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy => policy
                    .SetIsOriginAllowed(origin =>
                    {
                        if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                            return uri.Host == "localhost" || uri.Host == "127.0.0.1";
                        return false;
                    })
                    .AllowCredentials()
                    .AllowAnyMethod()
                    .AllowAnyHeader());
            });

            // Kestrel on port 5050
            builder.WebHost.UseUrls("http://localhost:5050");

            var app = builder.Build();

            app.UseSwagger();
            app.UseSwaggerUI();
            app.UseCors();

            // REST API endpoints
            app.MapSettingsEndpoints();
            app.MapAuthEndpoints();

            // Serve Admin UI SPA
            app.UseStaticFiles();
            app.MapFallbackToFile("index.html");

            // Apply EF Core migrations and seed/import on first start
            using (var scope = app.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.Database.Migrate();

                var hasData = context.MailAgentSettings.Any() || context.EmailAccounts.Any();

                if (!hasData)
                {
                    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                        .CreateLogger(nameof(DatabaseConfigurationProvider));
                    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

                    var hasAppSettings = config.GetSection(MailAgentOptions.SectionName).Exists()
                                      || config.GetSection(ConnectPatternOptions.SectionName).Exists();

                    if (hasAppSettings)
                    {
                        logger.LogInformation("Found existing settings in appsettings.json — importing into database...");
                        await DatabaseConfigurationProvider.ImportFromConfiguration(context, config, logger);
                    }
                    else
                    {
                        await DatabaseConfigurationProvider.SeedDefaults(context, logger);
                    }

                    ((IConfigurationRoot)app.Configuration).Reload();
                }
            }

            // Display configuration summary
            var configValidator = app.Services.GetRequiredService<ConfigurationValidator>();
            configValidator.PrintConfigurationSummary();
            var issues = configValidator.ValidateConfiguration();
            configValidator.PrintValidationResults(issues);

            // Open browser after startup
            app.Lifetime.ApplicationStarted.Register(() =>
            {
                var url = "http://localhost:5050";
                try
                {
                    if (OperatingSystem.IsWindows())
                        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    else if (OperatingSystem.IsMacOS())
                        Process.Start("open", url);
                    else if (OperatingSystem.IsLinux())
                        Process.Start("xdg-open", url);
                }
                catch (Exception ex)
                {
                    // Browser launch is best-effort; failure should not affect service operation.
                    Log.Warning(ex, "Could not open browser at {Url}.", url);
                }
            });

            await app.RunAsync().ConfigureAwait(false);
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
       (   ///**   ,,,,,,   *,,,,                       #) Konfiguration über Admin UI auf Port 5050
      ((   ///**    ,,,,,,.  ,,,,,                         
      ((    ///**,    ,,,,,,  ,,,,                         
      #((    ///***    ,,,,,* ,,,,                      
      #(((*   *//***    ,,,,, ,,,,                      
       #((((    /****    ,,,,,,,,                          
         ((((/   /****    ,,,,,,                           
          ((((//  /****   ,,,,
            (((//  /***   ,,,                                        Version: {assembly.GetName().Version} 
             (((// /***   ,                             MailAgent für unbegrenzt viele Standorte und Organisationen
               (/////*                                  Feuer Software GmbH | Karlsbaderstr. 16 | 65760 Eschborn
                (/////
                  ////
                   //";

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(Logo);
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine();
        }
    }
}

