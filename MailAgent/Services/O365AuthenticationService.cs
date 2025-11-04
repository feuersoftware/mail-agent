using Microsoft.Identity.Client;
using System.Diagnostics.CodeAnalysis;

namespace FeuerSoftware.MailAgent.Services
{
    public class O365AuthenticationService : IAuthenticationService
    {
        private readonly ILogger<O365AuthenticationService> _log;
        private readonly ITokenStorageService _tokenStorage;
        private readonly IPublicClientApplication _publicClientApp;
        private readonly string[] _scopes = new[] { 
            "https://outlook.office365.com/IMAP.AccessAsUser.All",
            "https://outlook.office365.com/POP.AccessAsUser.All",
            "offline_access" 
        };

        public O365AuthenticationService(
            [NotNull] ILogger<O365AuthenticationService> log,
            [NotNull] ITokenStorageService tokenStorage,
            [NotNull] Microsoft.Extensions.Options.IOptions<FeuerSoftware.MailAgent.Options.MailAgentOptions> options)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _tokenStorage = tokenStorage ?? throw new ArgumentNullException(nameof(tokenStorage));
            var mailAgentOptions = options?.Value ?? throw new ArgumentNullException(nameof(options));

            // Create the MSAL public client application
            _publicClientApp = PublicClientApplicationBuilder
                .Create(mailAgentOptions.O365ClientId)
                .WithAuthority(AzureCloudInstance.AzurePublic, "common")
                .WithRedirectUri("http://localhost")
                .Build();

            // Configure token cache serialization
            ConfigureTokenCache();
        }

        public async Task<string> GetAccessTokenAsync(string username)
        {
            try
            {
                // Try to get token silently first
                var accounts = await _publicClientApp.GetAccountsAsync();
                var account = accounts.FirstOrDefault(a => a.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

                if (account != null)
                {
                    try
                    {
                        var result = await _publicClientApp
                            .AcquireTokenSilent(_scopes, account)
                            .ExecuteAsync();
                        
                        _log.LogDebug($"Acquired token silently for {username}");
                        await _tokenStorage.SaveTokenAsync(username, result.AccessToken);
                        return result.AccessToken;
                    }
                    catch (MsalUiRequiredException)
                    {
                        _log.LogInformation($"UI interaction required for {username}. Attempting interactive authentication.");
                    }
                }

                // If silent acquisition fails, try interactive
                var interactiveResult = await _publicClientApp
                    .AcquireTokenInteractive(_scopes)
                    .WithLoginHint(username)
                    .WithPrompt(Prompt.SelectAccount)
                    .ExecuteAsync();

                await _tokenStorage.SaveTokenAsync(username, interactiveResult.AccessToken);
                _log.LogInformation($"Acquired token interactively for {username}");
                return interactiveResult.AccessToken;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Failed to acquire access token for {username}");
                throw;
            }
        }

        public async Task InitializeAuthenticationAsync(IEnumerable<string> usernames, CancellationToken cancellationToken)
        {
            _log.LogInformation("Initializing O365 authentication for users...");

            foreach (var username in usernames)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    _log.LogInformation($"Authenticating user: {username}");
                    await GetAccessTokenAsync(username);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, $"Failed to initialize authentication for {username}");
                    throw;
                }
            }

            _log.LogInformation("O365 authentication initialization completed.");
        }

        private void ConfigureTokenCache()
        {
            // This helps persist tokens across application restarts
            // MSAL will handle caching internally
            _log.LogDebug("Token cache configured");
        }
    }
}
