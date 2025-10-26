using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;

namespace FeuerSoftware.MailAgent.Extensions
{
    public static class ObservableExtensions
    {
        public static IDisposable SubscribeAsyncSafe<T>(
            [NotNull] this IObservable<T> source,
            [NotNull] Func<T, Task> onNextAsync,
            [NotNull] Action<Exception> onError,
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
                        onError(ex);
                    }
                }))
                .Concat()
                .Subscribe(_ => { }, onError, onCompleted);
        }
    }
}
