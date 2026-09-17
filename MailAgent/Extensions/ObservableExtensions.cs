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
                .Subscribe(_ => { }, ex => onError(ex), onCompleted);
        }
    }
}
