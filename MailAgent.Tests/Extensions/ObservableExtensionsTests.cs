using System.Reactive.Subjects;
using FeuerSoftware.MailAgent.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FeuerSoftware.MailAgent.Tests.Extensions;

/// <summary>
/// SubscribeAsyncSafe sits at the center of three review rounds' worth of concurrency/error-handling
/// bugs in this branch (overlapping ticks corrupting a shared mail connection, a fire-and-forget error
/// handler letting the next tick start before a reconnect finished, an unobserved "double fault"). These
/// tests pin down the contract that fixed those bugs, since MailService itself isn't unit-testable without
/// a live mail server.
/// </summary>
public class ObservableExtensionsTests
{
    [Fact]
    public async Task OnNextAsync_IsInvokedForEachValue()
    {
        var source = new Subject<int>();
        var received = new List<int>();
        var secondValueProcessed = new TaskCompletionSource();

        using var subscription = source.SubscribeAsyncSafe(
            value =>
            {
                received.Add(value);
                if (value == 2)
                {
                    secondValueProcessed.SetResult();
                }

                return Task.CompletedTask;
            },
            _ => Task.CompletedTask,
            () => { });

        source.OnNext(1);
        source.OnNext(2);

        await secondValueProcessed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { 1, 2 }, received);
    }

    [Fact]
    public async Task ExceptionInOnNextAsync_IsRoutedToOnErrorAndAwaited()
    {
        var source = new Subject<int>();
        var thrown = new InvalidOperationException("boom");
        Exception? observed = null;
        var onErrorCompleted = new TaskCompletionSource();

        using var subscription = source.SubscribeAsyncSafe(
            _ => throw thrown,
            async ex =>
            {
                // Yield first so this only completes if the caller actually awaits onError,
                // rather than firing it and moving on (the original "async void" bug this fixed).
                await Task.Yield();
                observed = ex;
                onErrorCompleted.SetResult();
            },
            () => { });

        source.OnNext(1);

        await onErrorCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(thrown, observed);
    }

    [Fact]
    public async Task ExceptionInOnNextAsync_DoesNotStopSubsequentValues()
    {
        var source = new Subject<int>();
        var received = new List<int>();
        var secondValueProcessed = new TaskCompletionSource();

        using var subscription = source.SubscribeAsyncSafe(
            value =>
            {
                if (value == 1)
                {
                    throw new InvalidOperationException("boom");
                }

                received.Add(value);
                secondValueProcessed.SetResult();
                return Task.CompletedTask;
            },
            _ => Task.CompletedTask,
            () => { });

        source.OnNext(1);
        source.OnNext(2);

        await secondValueProcessed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { 2 }, received);
    }

    [Fact]
    public async Task Ticks_AreProcessedSequentially_NeverConcurrently()
    {
        // Regression test for the original bug: overlapping poll ticks/reconnects on the same
        // non-thread-safe mail connection is what caused "stops processing mail until restart".
        // SubscribeAsyncSafe must fully await each onNextAsync (via Concat()) before starting the next.
        var source = new Subject<int>();
        var firstStarted = new TaskCompletionSource();
        var firstMayFinish = new TaskCompletionSource();
        var secondStarted = new TaskCompletionSource();
        var running = 0;
        var overlapDetected = false;

        using var subscription = source.SubscribeAsyncSafe(
            async value =>
            {
                if (Interlocked.Increment(ref running) > 1)
                {
                    overlapDetected = true;
                }

                if (value == 1)
                {
                    firstStarted.SetResult();
                    await firstMayFinish.Task;
                }
                else
                {
                    secondStarted.SetResult();
                }

                Interlocked.Decrement(ref running);
            },
            _ => Task.CompletedTask,
            () => { });

        source.OnNext(1);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        source.OnNext(2);

        // The second value must NOT be processed while the first is still in flight.
        var winner = await Task.WhenAny(secondStarted.Task, Task.Delay(TimeSpan.FromMilliseconds(200)));
        Assert.NotSame(secondStarted.Task, winner);

        firstMayFinish.SetResult();

        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(overlapDetected);
    }

    [Fact]
    public async Task OnCompleted_FiresExactlyOnceWhenSourceCompletes()
    {
        var source = new Subject<int>();
        var completedCount = 0;
        var valueProcessed = new TaskCompletionSource();

        using var subscription = source.SubscribeAsyncSafe(
            _ =>
            {
                valueProcessed.SetResult();
                return Task.CompletedTask;
            },
            _ => Task.CompletedTask,
            () => Interlocked.Increment(ref completedCount));

        source.OnNext(1);
        await valueProcessed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        source.OnCompleted();

        Assert.Equal(1, completedCount);
    }

    [Fact]
    public void LogAndContinue_LogsTheExceptionAndReturnsACompletedTask()
    {
        var logger = new RecordingLogger();
        var handler = logger.LogAndContinue("something failed");
        var exception = new InvalidOperationException("boom");

        var task = handler(exception);

        Assert.True(task.IsCompletedSuccessfully);
        Assert.Single(logger.LoggedErrors);
        Assert.Same(exception, logger.LoggedErrors[0]);
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<Exception> LoggedErrors { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error && exception != null)
            {
                LoggedErrors.Add(exception);
            }
        }
    }
}
