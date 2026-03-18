using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FeuerSoftware.MailAgent.Data
{
    /// <summary>
    /// Custom IConfigurationProvider that reads MailAgent settings from a SQLite database.
    /// Layered on top of appsettings.json so DB values take precedence.
    /// </summary>
    public class DatabaseConfigurationProvider : ConfigurationProvider
    {
        private readonly string _connectionString;

        public DatabaseConfigurationProvider(string connectionString)
        {
            _connectionString = connectionString;
        }

        public override void Load()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connectionString)
                .Options;

            using var db = new AppDbContext(options);

            // Ensure database and tables exist (idempotent)
            db.Database.EnsureCreated();

            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            // ── MailAgentSettings ──────────────────────────────────────────────
            var settings = db.MailAgentSettings.FirstOrDefault();
            if (settings != null)
            {
                const string s = "MailAgentOptions";
                data[$"{s}:EMailPollingIntervalSeconds"] = settings.EMailPollingIntervalSeconds.ToString();
                data[$"{s}:SecretKeyPassphrase"] = settings.SecretKeyPassphrase;
                data[$"{s}:OutputPath"] = settings.OutputPath;
                data[$"{s}:ProcessMode"] = settings.ProcessMode;
                data[$"{s}:EMailMode"] = settings.EMailMode;
                data[$"{s}:IgnoreCertificateErrors"] = settings.IgnoreCertificateErrors.ToString().ToLowerInvariant();
                if (!string.IsNullOrEmpty(settings.HeartbeatInterval))
                    data[$"{s}:HeartbeatInterval"] = settings.HeartbeatInterval;
                data[$"{s}:HeartbeatUrl"] = settings.HeartbeatUrl;
                data[$"{s}:O365ClientId"] = settings.O365ClientId;
                data[$"{s}:DisableEmailAgeThreshold"] = settings.DisableEmailAgeThreshold.ToString().ToLowerInvariant();
            }

            // ── EmailAccounts ──────────────────────────────────────────────────
            var accounts = db.EmailAccounts.ToList();
            for (int i = 0; i < accounts.Count; i++)
            {
                var a = accounts[i];
                var prefix = $"MailAgentOptions:EmailSettings:{i}";
                data[$"{prefix}:Name"] = a.Name;
                data[$"{prefix}:ApiKey"] = a.ApiKey;
                data[$"{prefix}:EMailHost"] = a.EMailHost;
                data[$"{prefix}:EMailPort"] = a.EMailPort.ToString();
                data[$"{prefix}:EMailUsername"] = a.EMailUsername;
                data[$"{prefix}:EMailPassword"] = a.EMailPassword;
                data[$"{prefix}:EMailSubjectFilter"] = a.EMailSubjectFilter;
                data[$"{prefix}:EMailSenderFilter"] = a.EMailSenderFilter;
                data[$"{prefix}:AuthenticationType"] = a.AuthenticationType;
            }

            // ── PatternSettings ────────────────────────────────────────────────
            var pattern = db.PatternSettings
                .Include(p => p.AdditionalProperties)
                .FirstOrDefault();
            if (pattern != null)
            {
                const string p2 = "ConnectPatternOptions";
                data[$"{p2}:StartPattern"] = pattern.StartPattern;
                data[$"{p2}:NumberPattern"] = pattern.NumberPattern;
                data[$"{p2}:KeywordPattern"] = pattern.KeywordPattern;
                data[$"{p2}:FactsPattern"] = pattern.FactsPattern;
                data[$"{p2}:StreetPattern"] = pattern.StreetPattern;
                data[$"{p2}:HouseNumberPattern"] = pattern.HouseNumberPattern;
                data[$"{p2}:CityPattern"] = pattern.CityPattern;
                data[$"{p2}:DistrictPattern"] = pattern.DistrictPattern;
                data[$"{p2}:ZipCodePattern"] = pattern.ZipCodePattern;
                data[$"{p2}:RicPattern"] = pattern.RicPattern;
                data[$"{p2}:LongitudePattern"] = pattern.LongitudePattern;
                data[$"{p2}:LatitudePattern"] = pattern.LatitudePattern;
                data[$"{p2}:ReporterNamePattern"] = pattern.ReporterNamePattern;
                data[$"{p2}:ReporterPhonePattern"] = pattern.ReporterPhonePattern;

                for (int i = 0; i < pattern.AdditionalProperties.Count; i++)
                {
                    var ap = pattern.AdditionalProperties[i];
                    data[$"{p2}:AdditionalProperties:{i}:Name"] = ap.Name;
                    data[$"{p2}:AdditionalProperties:{i}:Pattern"] = ap.Pattern;
                }
            }

            Data = data;
        }

        /// <summary>
        /// Imports settings from IConfiguration (e.g. appsettings.json) into the DB.
        /// Used for migrating existing installations.
        /// </summary>
        public static async Task ImportFromConfiguration(AppDbContext db, IConfiguration config, ILogger logger)
        {
            // ── MailAgentSettings ──────────────────────────────────────────────
            var agentSection = config.GetSection("MailAgentOptions");
            if (agentSection.Exists())
            {
                var existing = await db.MailAgentSettings.FindAsync(1);
                if (existing == null)
                {
                    existing = new MailAgentSettingsEntity { Id = 1 };
                    db.MailAgentSettings.Add(existing);
                }
                existing.EMailPollingIntervalSeconds = agentSection.GetValue<int>("EMailPollingIntervalSeconds", 5);
                existing.SecretKeyPassphrase = agentSection["SecretKeyPassphrase"] ?? string.Empty;
                existing.OutputPath = agentSection["OutputPath"] ?? string.Empty;
                existing.ProcessMode = agentSection["ProcessMode"] ?? "ConnectPlain";
                existing.EMailMode = agentSection["EMailMode"] ?? "Imap";
                existing.IgnoreCertificateErrors = agentSection.GetValue<bool>("IgnoreCertificateErrors", false);
                existing.HeartbeatInterval = agentSection["HeartbeatInterval"];
                existing.HeartbeatUrl = agentSection["HeartbeatUrl"] ?? string.Empty;
                existing.O365ClientId = agentSection["O365ClientId"] ?? string.Empty;
                existing.DisableEmailAgeThreshold = agentSection.GetValue<bool>("DisableEmailAgeThreshold", false);

                // Email accounts
                var emailSettingsSection = agentSection.GetSection("EmailSettings");
                var existingAccounts = db.EmailAccounts.ToList();
                db.EmailAccounts.RemoveRange(existingAccounts);
                foreach (var child in emailSettingsSection.GetChildren())
                {
                    db.EmailAccounts.Add(new EmailAccountEntity
                    {
                        Name = child["Name"] ?? string.Empty,
                        ApiKey = child["ApiKey"] ?? string.Empty,
                        EMailHost = child["EMailHost"] ?? string.Empty,
                        EMailPort = child.GetValue<int>("EMailPort", 993),
                        EMailUsername = child["EMailUsername"] ?? string.Empty,
                        EMailPassword = child["EMailPassword"] ?? string.Empty,
                        EMailSubjectFilter = child["EMailSubjectFilter"] ?? string.Empty,
                        EMailSenderFilter = child["EMailSenderFilter"] ?? string.Empty,
                        AuthenticationType = child["AuthenticationType"] ?? "Basic"
                    });
                }

                logger.LogInformation("Imported MailAgentOptions from appsettings.json.");
            }

            // ── PatternSettings ────────────────────────────────────────────────
            var patternSection = config.GetSection("ConnectPatternOptions");
            if (patternSection.Exists())
            {
                var existing = await db.PatternSettings.Include(p => p.AdditionalProperties).FirstOrDefaultAsync(p => p.Id == 1);
                if (existing == null)
                {
                    existing = new PatternSettingsEntity { Id = 1 };
                    db.PatternSettings.Add(existing);
                }
                existing.StartPattern = patternSection["StartPattern"] ?? string.Empty;
                existing.NumberPattern = patternSection["NumberPattern"] ?? string.Empty;
                existing.KeywordPattern = patternSection["KeywordPattern"] ?? string.Empty;
                existing.FactsPattern = patternSection["FactsPattern"] ?? string.Empty;
                existing.StreetPattern = patternSection["StreetPattern"] ?? string.Empty;
                existing.HouseNumberPattern = patternSection["HouseNumberPattern"] ?? string.Empty;
                existing.CityPattern = patternSection["CityPattern"] ?? string.Empty;
                existing.DistrictPattern = patternSection["DistrictPattern"] ?? string.Empty;
                existing.ZipCodePattern = patternSection["ZipCodePattern"] ?? string.Empty;
                existing.RicPattern = patternSection["RicPattern"] ?? string.Empty;
                existing.LongitudePattern = patternSection["LongitudePattern"] ?? string.Empty;
                existing.LatitudePattern = patternSection["LatitudePattern"] ?? string.Empty;
                existing.ReporterNamePattern = patternSection["ReporterNamePattern"] ?? string.Empty;
                existing.ReporterPhonePattern = patternSection["ReporterPhonePattern"] ?? string.Empty;

                existing.AdditionalProperties.Clear();
                var additionalSection = patternSection.GetSection("AdditionalProperties");
                foreach (var child in additionalSection.GetChildren())
                {
                    existing.AdditionalProperties.Add(new AdditionalPatternEntity
                    {
                        PatternSettingsId = 1,
                        Name = child["Name"] ?? string.Empty,
                        Pattern = child["Pattern"] ?? string.Empty
                    });
                }

                logger.LogInformation("Imported ConnectPatternOptions from appsettings.json.");
            }

            await db.SaveChangesAsync();
        }

        /// <summary>Seeds sensible defaults for fresh installations.</summary>
        public static async Task SeedDefaults(AppDbContext db, ILogger logger)
        {
            if (!db.MailAgentSettings.Any())
            {
                db.MailAgentSettings.Add(new MailAgentSettingsEntity
                {
                    Id = 1,
                    EMailPollingIntervalSeconds = 5,
                    ProcessMode = "ConnectPlain",
                    EMailMode = "Imap"
                });
            }

            if (!db.PatternSettings.Any())
            {
                db.PatternSettings.Add(new PatternSettingsEntity { Id = 1 });
            }

            await db.SaveChangesAsync();
            logger.LogInformation("Seeded default settings.");
        }
    }

    public class DatabaseConfigurationSource(string connectionString) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) =>
            new DatabaseConfigurationProvider(connectionString);
    }

    public static class DatabaseConfigurationExtensions
    {
        public static IConfigurationBuilder AddDatabaseConfiguration(
            this IConfigurationBuilder builder,
            string connectionString)
        {
            return builder.Add(new DatabaseConfigurationSource(connectionString));
        }
    }
}
