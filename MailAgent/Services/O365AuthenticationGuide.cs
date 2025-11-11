using FeuerSoftware.MailAgent.Options;

namespace FeuerSoftware.MailAgent.Services
{
    public class O365AuthenticationGuide
    {
        private readonly IAuthenticationService _authService;
        private readonly ILogger<O365AuthenticationGuide> _log;

        public O365AuthenticationGuide(
            IAuthenticationService authService,
            ILogger<O365AuthenticationGuide> log)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public async Task<bool> GuideUserThroughAuthenticationAsync(
            List<SiteEmailSetting> o365Mailboxes,
            CancellationToken cancellationToken)
        {
            if (!o365Mailboxes.Any())
            {
                return true;
            }

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
            Console.WriteLine("              OFFICE 365 AUTHENTICATION REQUIRED");
            Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
            Console.ResetColor();
            Console.WriteLine();

            Console.WriteLine($"You have {o365Mailboxes.Count} Office 365 mailbox(es) configured that require");
            Console.WriteLine("modern authentication (OAuth2). We'll guide you through the authentication");
            Console.WriteLine("process for each mailbox.");
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("What to expect:");
            Console.ResetColor();
            Console.WriteLine("  1. A browser window will open for each mailbox");
            Console.WriteLine("  2. Sign in with your Office 365 credentials");
            Console.WriteLine("  3. Accept the permission request for email access");
            Console.WriteLine("  4. The tokens will be securely stored for future use");
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("Note: This authentication is only needed once. Tokens will be automatically");
            Console.WriteLine("refreshed in the background for future runs.");
            Console.ResetColor();
            Console.WriteLine();

            Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
            Console.WriteLine();

            var allSuccess = true;

            for (int i = 0; i < o365Mailboxes.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("\n⚠ Authentication cancelled by user.");
                    Console.ResetColor();
                    return false;
                }

                var mailbox = o365Mailboxes[i];
                var success = await AuthenticateMailboxAsync(mailbox, i + 1, o365Mailboxes.Count);

                if (!success)
                {
                    allSuccess = false;
                    
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n✗ Failed to authenticate mailbox: {mailbox.Name}");
                    Console.ResetColor();
                    
                    Console.WriteLine("\nWould you like to:");
                    Console.WriteLine("  1. Retry this mailbox");
                    Console.WriteLine("  2. Skip and continue with other mailboxes");
                    Console.WriteLine("  3. Exit and fix configuration");
                    Console.Write("\nEnter choice (1-3): ");
                    
                    var choice = Console.ReadLine()?.Trim();
                    
                    switch (choice)
                    {
                        case "1":
                            i--; // Retry this mailbox
                            continue;
                        case "2":
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("⚠ Skipping this mailbox. It will not be available until authenticated.");
                            Console.ResetColor();
                            continue;
                        case "3":
                        default:
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("\n⚠ Exiting application. Please fix the configuration and try again.");
                            Console.ResetColor();
                            return false;
                    }
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ Successfully authenticated mailbox: {mailbox.Name}");
                Console.ResetColor();
                Console.WriteLine();
            }

            if (allSuccess)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
                Console.WriteLine("  ✓ All Office 365 mailboxes authenticated successfully!");
                Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
                Console.ResetColor();
                Console.WriteLine();
            }

            return allSuccess;
        }

        private async Task<bool> AuthenticateMailboxAsync(SiteEmailSetting mailbox, int current, int total)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[{current}/{total}] Authenticating: {mailbox.Name}");
            Console.ResetColor();
            Console.WriteLine($"     Username: {mailbox.EMailUsername}");
            Console.WriteLine($"     Host:     {mailbox.EMailHost}");
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("🌐 Opening browser for authentication...");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("Please complete the authentication in your browser.");
            Console.WriteLine("If the browser doesn't open automatically, check your taskbar.");
            Console.WriteLine();

            try
            {
                var username = mailbox.EMailUsername;
                await _authService.GetAccessTokenAsync(username);
                return true;
            }
            catch (Microsoft.Identity.Client.MsalException ex)
            {
                _log.LogError(ex, $"MSAL authentication failed for {mailbox.EMailUsername}");
                
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n✗ Authentication failed: {ex.Message}");
                Console.ResetColor();
                
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Common issues:");
                Console.ResetColor();
                Console.WriteLine("  • Browser window was closed before completing sign-in");
                Console.WriteLine("  • Wrong username or password entered");
                Console.WriteLine("  • Multi-factor authentication not completed");
                Console.WriteLine("  • Account doesn't have permission to access email via IMAP");
                Console.WriteLine("  • Azure AD application permissions not properly configured");
                Console.WriteLine();

                return false;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Unexpected error during authentication for {mailbox.EMailUsername}");
                
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n✗ Authentication failed: {ex.Message}");
                Console.ResetColor();

                return false;
            }
        }
    }
}
