using System.Diagnostics;
using FeuerSoftware.MailAgent.Services;
using MimeKit;
using Xunit;
using static FeuerSoftware.MailAgent.Tests.Services.MailServiceTestSupport;

namespace FeuerSoftware.MailAgent.Tests.Services;

// Two classes on purpose: xUnit runs classes in parallel, and each test needs real 4 s poll ticks.

public class HangingFetchTests
{
    [Fact]
    public async Task HangingFetch_IsAbortedWithinBudget_ReconnectsWithoutQuit_AndNextTickPolls()
    {
        var client = new FakeMailClient();
        var sw = Stopwatch.StartNew();
        TimeSpan? abortedAfter = null;
        client.OnGetUnseen = async (call, ct) =>
        {
            if (call == 1)
            {
                var start = sw.Elapsed;
                try { await Task.Delay(Timeout.Infinite, ct); }
                finally { abortedAfter = sw.Elapsed - start; }
            }

            return new[] { (Mail(), "1") };
        };

        using var service = CreateService(client);
        var delivered = new TaskCompletionSource<MimeMessage>();
        service.EMails.Subscribe(m => delivered.TrySetResult(m.Item1));

        await service.StartPollingAsync(CancellationToken.None);
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.NotNull(abortedAfter);
        Assert.True(abortedAfter < AbortSlack, $"hung fetch ran {abortedAfter} - budget is {FastTimeouts.FetchTick}");
        Assert.Equal(new[] { false }, client.DisconnectQuitFlags);
        Assert.Equal(2, client.ConnectCalls);
        Assert.True(client.GetUnseenCalls >= 2);

        await service.StopAsync(CancellationToken.None);
    }
}

public class HangingReconnectTests
{
    [Fact]
    public async Task HangingReconnectConnect_IsAbortedWithinBudget_AndPollingRecovers()
    {
        var client = new FakeMailClient();
        var sw = Stopwatch.StartNew();
        TimeSpan? abortedAfter = null;
        client.OnConnect = async (call, ct) =>
        {
            if (call == 2)
            {
                var start = sw.Elapsed;
                try { await Task.Delay(Timeout.Infinite, ct); }
                finally { abortedAfter = sw.Elapsed - start; }
            }
        };
        client.OnGetUnseen = (call, _) => call == 1 || !client.IsConnected
            ? throw new InvalidOperationException("connection is dead")
            : Task.FromResult<IEnumerable<(MimeMessage message, string id)>>(new[] { (Mail(), "1") });

        using var service = CreateService(client);
        var delivered = new TaskCompletionSource<MimeMessage>();
        service.EMails.Subscribe(m => delivered.TrySetResult(m.Item1));

        await service.StartPollingAsync(CancellationToken.None);
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(abortedAfter);
        Assert.True(abortedAfter < AbortSlack, $"hung connect ran {abortedAfter} - budget is {FastTimeouts.Connect}");
        Assert.True(client.ConnectCalls >= 3);

        await service.StopAsync(CancellationToken.None);
    }
}

public class MailOperationTimeoutsTests
{
    private static readonly MailOperationTimeouts D = MailOperationTimeouts.Default;

    [Fact]
    public void DeadConnection_IsDetectedFast_AndHardCapStaysBelowTwoMinutes()
    {
        Assert.InRange(D.IoInactivity, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(20));
        Assert.True(D.WorstCaseDeadConnection + TimeSpan.FromSeconds(5) < TimeSpan.FromSeconds(90));
    }

    [Fact]
    public void SlowButAliveLogin_IsNotAbortedByTheOperationalIoTimeout()
    {
        // Exchange Online sometimes answers AUTHENTICATE only after >15 s.
        Assert.True(D.ConnectIoInactivity > D.IoInactivity);
        // Connect budget covers two silent phases (connect + AUTH) without cutting the second one short.
        Assert.True(D.Connect >= D.ConnectIoInactivity * 2);
    }

    [Fact]
    public void Token_OutlivesTheNextScheduledReconnect()
    {
        Assert.True(D.TokenMinValidity > D.ScheduledReconnect);
    }

    [Fact]
    public void LockWait_CoversEveryLockHolder_AndEwsMatchesTick()
    {
        Assert.True(D.LockWait >= D.FetchTick);
        Assert.True(D.LockWait >= D.Disconnect + D.Connect);
        Assert.Equal(D.FetchTick, D.EwsRequest);
    }

    [Fact]
    public void Backoff_IsLowCapped_AndFirstAttemptIsImmediate()
    {
        Assert.Equal(TimeSpan.Zero, D.Backoff(0));
        Assert.Equal(new[] { 5, 10, 20, 30, 30 }, Enumerable.Range(1, 5).Select(n => (int)D.Backoff(n).TotalSeconds));
        Assert.True(D.ReconnectBackoffMax <= TimeSpan.FromSeconds(60));
    }
}
