using FeuerSoftware.MailAgent.Data;
using FeuerSoftware.MailAgent.Services;
using Microsoft.EntityFrameworkCore;

namespace FeuerSoftware.MailAgent.Endpoints;

public static class AuthEndpoints
{
    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Authentication");

        // POST /api/auth/o365/{id} — trigger O365 interactive auth for a specific mailbox
        group.MapPost("/o365/{id:int}", async (int id, AppDbContext db, IAuthenticationService authService, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AuthEndpoints");
            var account = await db.EmailAccounts.FindAsync(id);
            if (account is null)
                return Results.NotFound(new { message = "Mailbox not found." });

            if (!account.AuthenticationType.Equals("O365", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { message = "This mailbox does not use O365 authentication." });

            try
            {
                logger.LogInformation("Starting O365 interactive authentication for {Username} via Admin UI.", account.EMailUsername);
                await authService.GetAccessTokenAsync(account.EMailUsername);
                logger.LogInformation("O365 authentication succeeded for {Username}.", account.EMailUsername);
                return Results.Ok(new { message = "Authentication successful.", username = account.EMailUsername });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "O365 authentication failed for {Username}.", account.EMailUsername);
                return Results.Problem(
                    title: "Authentication failed",
                    detail: ex.Message,
                    statusCode: 500);
            }
        }).WithName("AuthenticateO365Mailbox");

        return app;
    }
}
