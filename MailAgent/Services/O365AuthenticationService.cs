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
            [NotNull] Microsoft.Extensions.Options.IOptions<Options.MailAgentOptions> options)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _tokenStorage = tokenStorage ?? throw new ArgumentNullException(nameof(tokenStorage));
            var mailAgentOptions = options?.Value ?? throw new ArgumentNullException(nameof(options));

            var effectiveClientId = string.IsNullOrWhiteSpace(mailAgentOptions.O365ClientId)
                ? BuildSecrets.FallbackClientId
                : mailAgentOptions.O365ClientId;

            // Create the MSAL public client application
            _publicClientApp = PublicClientApplicationBuilder
                .Create(effectiveClientId)
                .WithAuthority(AzureCloudInstance.AzurePublic, "common")
                .WithRedirectUri("http://localhost")
                .Build();
            _publicClientApp.UserTokenCache.SetBeforeAccessAsync(async args =>
            {
                var username = args.Account?.Username ?? args.SuggestedCacheKey;
                if (!string.IsNullOrWhiteSpace(username))
                {
                    var tokenData = await _tokenStorage.GetTokenAsByteAsync(username);
                    if (tokenData != null)
                    {
                        args.TokenCache.DeserializeMsalV3(tokenData);
                    }
                }
            });
            _publicClientApp.UserTokenCache.SetAfterAccessAsync(async args =>
            {
                if (args.HasStateChanged)
                {
                    var username = args.Account?.Username;
                    if (!string.IsNullOrWhiteSpace(username))
                    {
                        var tokenData = args.TokenCache.SerializeMsalV3();
                        await _tokenStorage.SaveTokenByteAsync(username, tokenData);
                    }
                }
            });
        }

        public async Task<string> GetAccessTokenAsync(string username, bool allowInteractive, CancellationToken cancellationToken = default)
        {
            try
            {
                // Try to get token silently first
                var accounts = await _publicClientApp.GetAccountsAsync().WaitAsync(cancellationToken);
                var account = accounts.FirstOrDefault(a => a.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
                if (account != null)
                {
                    try
                    {
                        // Also raced against cancellationToken from the outside (not just passed into
                        // ExecuteAsync): AcquireTokenSilent goes through the same token-cache callbacks as
                        // GetAccountsAsync above, which do uncancellable disk/DPAPI I/O that MSAL's own
                        // cancellation handling can't reach - ExecuteAsync(cancellationToken) alone only
                        // bounds MSAL's network round-trip, not a stalled cache read.
                        var result = await _publicClientApp
                            .AcquireTokenSilent(_scopes, account)
                            .ExecuteAsync(cancellationToken)
                            .WaitAsync(cancellationToken);

                        _log.LogDebug($"Acquired token silently for {MaskUsername(username)}");
                        return result.AccessToken;
                    }
                    catch (MsalUiRequiredException)
                    {
                        _log.LogInformation($"UI interaction required for {MaskUsername(username)}. Attempting interactive authentication.");
                    }
                }

                // If silent acquisition fails, try interactive - but only when the caller has explicitly
                // opted in (the interactive first-run setup flow in O365AuthenticationGuide does; automatic
                // background reconnects via O365MailClient.Connect never do). This is a caller-provided flag
                // rather than a runtime "are we headless" guess, because a hung interactive browser flow
                // would otherwise block whatever per-mailbox lock is held around a background reconnect
                // forever - exactly the bug this method's cancellation support exists to prevent.
                if (!allowInteractive)
                {
                    throw new InvalidOperationException(
                        $"Silent token acquisition failed for {MaskUsername(username)} and interactive authentication was not requested for this call. Re-authenticate the account interactively first.");
                }

                // Same rationale as the AcquireTokenSilent call above: race it externally too, since the
                // token-cache callbacks it goes through can't be interrupted by ExecuteAsync's own token.
                var interactiveResult = await _publicClientApp
                    .AcquireTokenInteractive(_scopes)
                    .WithLoginHint(username)
                    .WithPrompt(Prompt.SelectAccount)
                    .ExecuteAsync(cancellationToken)
                    .WaitAsync(cancellationToken);

                _log.LogInformation($"Acquired token interactively for {MaskUsername(username)}");
                return interactiveResult.AccessToken;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Failed to acquire access token for {MaskUsername(username)}");
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
                    _log.LogInformation($"Authenticating user: {MaskUsername(username)}");
                    await GetAccessTokenAsync(username, allowInteractive: true, cancellationToken);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, $"Failed to initialize authentication for {MaskUsername(username)}");
                    throw;
                }
            }

            _log.LogInformation("O365 authentication initialization completed.");
        }

        private static string MaskUsername(string username)
        {
            // Mask the username for logging to avoid exposing full email addresses
            if (string.IsNullOrEmpty(username) || username.Length < 5)
            {
                return "***";
            }

            var atIndex = username.IndexOf('@');
            if (atIndex <= 0)
            {
                return username.Substring(0, 3) + "***";
            }

            return username.Substring(0, Math.Min(3, atIndex)) + "***@" + username.Substring(atIndex + 1);
        }
    }
}
