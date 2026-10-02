using FeuerSoftware.MailAgent.Options;
using FeuerSoftware.MailAgent.Services;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;

namespace FeuerSoftware.MailAgent.Tests.Services;

internal sealed class FakeMailClient : IMailClient
{
    private int _connectCalls, _getUnseenCalls;
    private readonly List<bool> _disconnectQuitFlags = new();

    public bool IsConnected { get; set; }

    public int ConnectCalls => _connectCalls;
    public int GetUnseenCalls => _getUnseenCalls;
    public IReadOnlyList<bool> DisconnectQuitFlags { get { lock (_disconnectQuitFlags) { return _disconnectQuitFlags.ToList(); } } }

    /// <summary>Called with the 1-based call number.</summary>
    public Func<int, CancellationToken, Task> OnConnect { get; set; } = (_, _) => Task.CompletedTask;

    public Func<int, CancellationToken, Task<IEnumerable<(MimeMessage message, string id)>>> OnGetUnseen { get; set; }
        = (_, _) => Task.FromResult<IEnumerable<(MimeMessage message, string id)>>(Array.Empty<(MimeMessage, string)>());

    public async Task Connect(string host, int port, string username, string password, CancellationToken cancellationToken = default)
    {
        await OnConnect(Interlocked.Increment(ref _connectCalls), cancellationToken);
        IsConnected = true;
    }

    public Task Disconnect(bool quit = true, CancellationToken cancellationToken = default)
    {
        lock (_disconnectQuitFlags) { _disconnectQuitFlags.Add(quit); }
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task<IEnumerable<(MimeMessage message, string id)>> GetUnseenMails(CancellationToken cancellationToken = default)
        => OnGetUnseen(Interlocked.Increment(ref _getUnseenCalls), cancellationToken);

    public Task MarkMessageSeenByUID(string mailId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Dispose() { }
}

internal sealed class FakeMailClientFactory(IMailClient client) : IMailClientFactory
{
    public IMailClient CreateClient(SiteEmailSetting settings) => client;
}

internal static class MailServiceTestSupport
{
    /// <summary>Tiny budgets so a hang is aborted in ~300 ms instead of minutes; poll interval stays at the 4 s minimum.</summary>
    public static readonly MailOperationTimeouts FastTimeouts = MailOperationTimeouts.Default with
    {
        FetchTick = TimeSpan.FromMilliseconds(300),
        Connect = TimeSpan.FromMilliseconds(300),
        Disconnect = TimeSpan.FromMilliseconds(300),
        ReconnectBackoffBase = TimeSpan.FromMilliseconds(50),
        ReconnectBackoffMax = TimeSpan.FromMilliseconds(200),
    };

    /// <summary>Generous upper bound for "was aborted by its budget (300 ms), not by something else".</summary>
    public static readonly TimeSpan AbortSlack = TimeSpan.FromSeconds(3);

    public static MailService CreateService(FakeMailClient client)
    {
        var options = new MailAgentOptions
        {
            EMailPollingIntervalSeconds = 4,
            DisableEmailAgeThreshold = true,
            EmailSettings = { new SiteEmailSetting { Name = "test", EMailHost = "localhost" } },
        };

        return new MailService(
            new FakeMailClientFactory(client),
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<MailService>.Instance,
            FastTimeouts);
    }

    public static MimeMessage Mail()
    {
        var message = new MimeMessage { Subject = "alarm", Date = DateTimeOffset.Now };
        message.From.Add(new MailboxAddress("sender", "sender@example.com"));
        return message;
    }
}
