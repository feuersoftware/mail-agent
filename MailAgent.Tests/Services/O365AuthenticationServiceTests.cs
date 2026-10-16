using FeuerSoftware.MailAgent.Options;
using FeuerSoftware.MailAgent.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FeuerSoftware.MailAgent.Tests.Services;

public class O365AuthenticationServiceTests
{
    private sealed class RecordingTokenStorage : ITokenStorageService
    {
        public List<string> Reads { get; } = new();

        public Task<byte[]?> GetTokenAsByteAsync(string username)
        {
            lock (Reads) { Reads.Add(username); }
            return Task.FromResult<byte[]?>(null);
        }

        public Task SaveTokenAsync(string username, string token) => Task.CompletedTask;
        public Task SaveTokenByteAsync(string username, byte[] tokenBytes) => Task.CompletedTask;
        public Task<string?> GetTokenAsync(string username) => Task.FromResult<string?>(null);
        public Task DeleteTokenAsync(string username) => Task.CompletedTask;
    }

    [Fact]
    public async Task PersistedCache_IsLoadedAtStartup_EvenThoughGetAccountsHasNoAccountHint()
    {
        // Regression: the cache was only loaded when the callback args carried an account/cache key, which
        // GetAccountsAsync (public client) never does => nothing loaded after a restart => interactive login.
        var storage = new RecordingTokenStorage();
        var service = new O365AuthenticationService(
            NullLogger<O365AuthenticationService>.Instance,
            storage,
            Microsoft.Extensions.Options.Options.Create(new MailAgentOptions { O365ClientId = Guid.NewGuid().ToString() }));

        // Silent acquisition fails (empty cache) and interactive is not allowed => throws, never opens a browser.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAccessTokenAsync("user@example.com", allowInteractive: false));

        Assert.Contains(O365AuthenticationService.TokenCacheKey, storage.Reads);
    }
}
