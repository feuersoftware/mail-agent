using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;

namespace FeuerSoftware.MailAgent.Extensions
{
    public static class ObservableExtensions
    {
        public static IDisposable SubscribeAsyncSafe<T>(
            [NotNull] this IObservable<T> source,
            [NotNull] Func<T, Task> onNextAsync,
            [NotNull] Func<Exception, Task> onError,
            [NotNull] Action onCompleted)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (onNextAsync == null) throw new ArgumentNullException(nameof(onNextAsync));
            if (onError == null) throw new ArgumentNullException(nameof(onError));
            if (onCompleted == null) throw new ArgumentNullException(nameof(onCompleted));

            return source
                .Select(arg => Observable.FromAsync(async () =>
                {
                    try
                    {
                        await onNextAsync(arg).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        await onError(ex).ConfigureAwait(false);
                    }
                }))
                .Concat()
                .Subscribe(_ => { }, ex =>
                {
                    // Only reached if the already-awaited onError(ex) above itself faults (a "double fault").
                    // IObserver<T>.OnError is synchronous, so this can't be awaited - and per Rx semantics,
                    // reaching here terminates this subscription permanently (no more ticks for this mailbox).
                    // We can't prevent that, but we still observe the task's exception so it isn't reported
                    // as an unobserved task exception.
                    _ = onError(ex).ContinueWith(t => t.Exception, TaskContinuationOptions.ExecuteSynchronously);
                }, onCompleted);
        }

        /// <summary>
        /// Builds a `Func&lt;Exception, Task&gt;` error handler for <see cref="SubscribeAsyncSafe{T}"/> that just
        /// logs and continues - the common case for subscriptions with no reconnect logic of their own.
        /// </summary>
        public static Func<Exception, Task> LogAndContinue(this ILogger logger, string message)
        {
            return ex =>
            {
                logger.LogError(ex, message);
                return Task.CompletedTask;
            };
        }
    }
}
