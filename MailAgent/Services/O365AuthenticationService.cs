using Microsoft.Identity.Client;
using System.Diagnostics.CodeAnalysis;

namespace FeuerSoftware.MailAgent.Services
{
    public class O365AuthenticationService : IAuthenticationService
    {
        private readonly ILogger<O365AuthenticationService> _log;
        private readonly ITokenStorageService _tokenStorage;
        private readonly IPublicClientApplication _publicClientApp;

        // Storage key of the shared MSAL cache blob (see the cache callbacks in the constructor).
        internal const string TokenCacheKey = "msal-token-cache";

        // Last forced refresh per user: a connect right after one (e.g. reconnect retries during an outage)
        // must not hit Azure AD again just because the fresh token is still shorter than TokenMinValidity.
        private static readonly TimeSpan ForceRefreshCooldown = TimeSpan.FromMinutes(5);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> _lastForcedRefresh = new(StringComparer.OrdinalIgnoreCase);
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
            // One shared cache blob for all accounts. GetAccountsAsync (public client) calls the callbacks with
            // neither Account nor SuggestedCacheKey, so a per-user key would never load anything after a process
            // start (=> no account found => interactive login on every start). A shared blob is also always
            // the latest state of every account, unlike per-user snapshots that diverge from each other.
            _publicClientApp.UserTokenCache.SetBeforeAccessAsync(async args =>
            {
                var tokenData = await _tokenStorage.GetTokenAsByteAsync(TokenCacheKey);
                if (tokenData != null)
                {
                    args.TokenCache.DeserializeMsalV3(tokenData);
                }
            });
            _publicClientApp.UserTokenCache.SetAfterAccessAsync(async args =>
            {
                if (args.HasStateChanged)
                {
                    await _tokenStorage.SaveTokenByteAsync(TokenCacheKey, args.TokenCache.SerializeMsalV3());
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

                        // A still-valid cached token may expire before the next scheduled reconnect (the session
                        // then dies with "AccessTokenExpired"), so renew it now if it is too short-lived.
                        if (result.ExpiresOn - DateTimeOffset.UtcNow < MailOperationTimeouts.Default.TokenMinValidity
                            && DateTimeOffset.UtcNow - _lastForcedRefresh.GetValueOrDefault(username) > ForceRefreshCooldown)
                        {
                            _lastForcedRefresh[username] = DateTimeOffset.UtcNow;

                            try
                            {
                                result = await _publicClientApp
                                    .AcquireTokenSilent(_scopes, account)
                                    .WithForceRefresh(true)
                                    .ExecuteAsync(cancellationToken)
                                    .WaitAsync(cancellationToken);

                                _log.LogDebug($"Refreshed token for {MaskUsername(username)}, valid until {result.ExpiresOn:u}");
                            }
                            catch (MsalException ex)
                            {
                                // The cached token is still usable now; the reactive reconnect covers its expiry.
                                _log.LogWarning(ex, $"Could not renew the access token for {MaskUsername(username)}; using the cached one (valid until {result.ExpiresOn:u}).");
                            }
                        }

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
