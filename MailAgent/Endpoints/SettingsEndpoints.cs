using FeuerSoftware.MailAgent.Data;
using Microsoft.EntityFrameworkCore;

namespace FeuerSoftware.MailAgent.Endpoints;

public static class SettingsEndpoints
{
    public static WebApplication MapSettingsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/settings").WithTags("Settings");

        // GET /api/settings — overview
        group.MapGet("/", async (AppDbContext db) =>
        {
            var sections = new List<object>
            {
                new { Section = "general", Exists = await db.MailAgentSettings.AnyAsync() },
                new { Section = "mailboxes", Exists = await db.EmailAccounts.AnyAsync() },
                new { Section = "pattern", Exists = await db.PatternSettings.AnyAsync() }
            };
            return Results.Ok(new { sections });
        }).WithName("GetAllSettings");

        // POST /api/settings/import — import from appsettings.json
        group.MapPost("/import", async (AppDbContext db, IConfiguration configuration, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger<AppDbContext>();
            await DatabaseConfigurationProvider.ImportFromConfiguration(db, configuration, logger);
            ((IConfigurationRoot)configuration).Reload();
            return Results.Ok(new { message = "Settings imported from appsettings.json." });
        }).WithName("ImportSettings");

        MapGeneralEndpoints(group);
        MapMailboxEndpoints(group);
        MapPatternEndpoints(group);

        return app;
    }

    private static void MapGeneralEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/general", async (AppDbContext db) =>
        {
            var entity = await db.MailAgentSettings.FindAsync(1);
            return Results.Ok(entity ?? new MailAgentSettingsEntity { Id = 1 });
        }).WithName("GetGeneralSettings");

        group.MapPut("/general", async (MailAgentSettingsEntity dto, AppDbContext db, IConfiguration configuration) =>
        {
            var entity = await db.MailAgentSettings.FindAsync(1);
            if (entity is null)
            {
                dto.Id = 1;
                db.MailAgentSettings.Add(dto);
            }
            else
            {
                entity.EMailPollingIntervalSeconds = dto.EMailPollingIntervalSeconds;
                entity.SecretKeyPassphrase = dto.SecretKeyPassphrase;
                entity.OutputPath = dto.OutputPath;
                entity.ProcessMode = dto.ProcessMode;
                entity.EMailMode = dto.EMailMode;
                entity.IgnoreCertificateErrors = dto.IgnoreCertificateErrors;
                entity.HeartbeatInterval = dto.HeartbeatInterval;
                entity.HeartbeatUrl = dto.HeartbeatUrl;
                entity.DisableEmailAgeThreshold = dto.DisableEmailAgeThreshold;
            }
            await db.SaveChangesAsync();
            ((IConfigurationRoot)configuration).Reload();
            return Results.Ok(await db.MailAgentSettings.FindAsync(1));
        }).WithName("UpdateGeneralSettings");
    }

    private static void MapMailboxEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/mailboxes", async (AppDbContext db) =>
        {
            var accounts = await db.EmailAccounts.ToListAsync();
            return Results.Ok(accounts);
        }).WithName("GetMailboxes");

        group.MapPost("/mailboxes", async (EmailAccountEntity dto, AppDbContext db, IConfiguration configuration) =>
        {
            dto.Id = 0;
            db.EmailAccounts.Add(dto);
            await db.SaveChangesAsync();
            ((IConfigurationRoot)configuration).Reload();
            return Results.Created($"/api/settings/mailboxes/{dto.Id}", dto);
        }).WithName("CreateMailbox");

        group.MapPut("/mailboxes/{id:int}", async (int id, EmailAccountEntity dto, AppDbContext db, IConfiguration configuration) =>
        {
            var entity = await db.EmailAccounts.FindAsync(id);
            if (entity is null) return Results.NotFound();

            entity.Name = dto.Name;
            entity.ApiKey = dto.ApiKey;
            entity.EMailHost = dto.EMailHost;
            entity.EMailPort = dto.EMailPort;
            entity.EMailUsername = dto.EMailUsername;
            entity.EMailPassword = dto.EMailPassword;
            entity.EMailSubjectFilter = dto.EMailSubjectFilter;
            entity.EMailSenderFilter = dto.EMailSenderFilter;
            entity.AuthenticationType = dto.AuthenticationType;

            await db.SaveChangesAsync();
            ((IConfigurationRoot)configuration).Reload();
            return Results.Ok(entity);
        }).WithName("UpdateMailbox");

        group.MapDelete("/mailboxes/{id:int}", async (int id, AppDbContext db, IConfiguration configuration) =>
        {
            var entity = await db.EmailAccounts.FindAsync(id);
            if (entity is null) return Results.NotFound();
            db.EmailAccounts.Remove(entity);
            await db.SaveChangesAsync();
            ((IConfigurationRoot)configuration).Reload();
            return Results.NoContent();
        }).WithName("DeleteMailbox");
    }

    private static void MapPatternEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/pattern", async (AppDbContext db) =>
        {
            var entity = await db.PatternSettings
                .Include(p => p.AdditionalProperties)
                .FirstOrDefaultAsync(p => p.Id == 1);
            return Results.Ok(entity ?? new PatternSettingsEntity { Id = 1 });
        }).WithName("GetPatternSettings");

        group.MapPut("/pattern", async (PatternSettingsEntity dto, AppDbContext db, IConfiguration configuration) =>
        {
            var entity = await db.PatternSettings
                .Include(p => p.AdditionalProperties)
                .FirstOrDefaultAsync(p => p.Id == 1);
            if (entity is null)
            {
                dto.Id = 1;
                db.PatternSettings.Add(dto);
            }
            else
            {
                entity.StartPattern = dto.StartPattern;
                entity.NumberPattern = dto.NumberPattern;
                entity.KeywordPattern = dto.KeywordPattern;
                entity.FactsPattern = dto.FactsPattern;
                entity.StreetPattern = dto.StreetPattern;
                entity.HouseNumberPattern = dto.HouseNumberPattern;
                entity.CityPattern = dto.CityPattern;
                entity.DistrictPattern = dto.DistrictPattern;
                entity.ZipCodePattern = dto.ZipCodePattern;
                entity.RicPattern = dto.RicPattern;
                entity.LongitudePattern = dto.LongitudePattern;
                entity.LatitudePattern = dto.LatitudePattern;
                entity.ReporterNamePattern = dto.ReporterNamePattern;
                entity.ReporterPhonePattern = dto.ReporterPhonePattern;

                // Replace additional properties
                entity.AdditionalProperties.Clear();
                foreach (var ap in dto.AdditionalProperties)
                {
                    entity.AdditionalProperties.Add(new AdditionalPatternEntity
                    {
                        PatternSettingsId = 1,
                        Name = ap.Name,
                        Pattern = ap.Pattern
                    });
                }
            }
            await db.SaveChangesAsync();
            ((IConfigurationRoot)configuration).Reload();
            return Results.Ok(await db.PatternSettings.Include(p => p.AdditionalProperties).FirstOrDefaultAsync(p => p.Id == 1));
        }).WithName("UpdatePatternSettings");
    }
}
