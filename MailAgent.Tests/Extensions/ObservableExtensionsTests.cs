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
    public async Task ExceptionInOnNextAsync_PassesTheThrownExceptionToOnError()
    {
        var source = new Subject<int>();
        var thrown = new InvalidOperationException("boom");
        Exception? observed = null;
        var onErrorCompleted = new TaskCompletionSource();

        using var subscription = source.SubscribeAsyncSafe(
            _ => throw thrown,
            ex =>
            {
                observed = ex;
                onErrorCompleted.SetResult();
                return Task.CompletedTask;
            },
            () => { });

        source.OnNext(1);

        await onErrorCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(thrown, observed);
    }

    [Fact]
    public async Task SlowOnError_BlocksTheNextValueFromBeingProcessed()
    {
        // Regression test for the original bug: onError used to be fired-and-forgotten (async void),
        // so the next tick could start - and race a shared, non-thread-safe mail connection - while a
        // reconnect triggered from onError was still running. SubscribeAsyncSafe must fully await
        // onError (not just onNextAsync) before Concat() lets the next OnNext through.
        var source = new Subject<int>();
        var onErrorStarted = new TaskCompletionSource();
        var onErrorMayFinish = new TaskCompletionSource();
        var secondValueProcessed = new TaskCompletionSource();

        using var subscription = source.SubscribeAsyncSafe(
            value =>
            {
                if (value == 1)
                {
                    throw new InvalidOperationException("boom");
                }

                secondValueProcessed.SetResult();
                return Task.CompletedTask;
            },
            async _ =>
            {
                onErrorStarted.SetResult();
                await onErrorMayFinish.Task;
            },
            () => { });

        try
        {
            source.OnNext(1);
            await onErrorStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            source.OnNext(2);

            // The second value must NOT be processed while onError (simulating a reconnect) is
            // still running.
            var winner = await Task.WhenAny(secondValueProcessed.Task, Task.Delay(TimeSpan.FromMilliseconds(500)));
            Assert.NotSame(secondValueProcessed.Task, winner);
        }
        finally
        {
            // Release onError's gate even if the assertion above failed, so a caught regression
            // doesn't leave this test's async lambda permanently suspended.
            onErrorMayFinish.TrySetResult();
        }

        await secondValueProcessed.Task.WaitAsync(TimeSpan.FromSeconds(5));
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

        try
        {
            source.OnNext(1);
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            source.OnNext(2);

            // The second value must NOT be processed while the first is still in flight.
            var winner = await Task.WhenAny(secondStarted.Task, Task.Delay(TimeSpan.FromMilliseconds(500)));
            Assert.NotSame(secondStarted.Task, winner);
        }
        finally
        {
            // Release the first tick's gate even if the assertion above failed, so a caught
            // regression doesn't leave this test's async lambda permanently suspended.
            firstMayFinish.TrySetResult();
        }

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
