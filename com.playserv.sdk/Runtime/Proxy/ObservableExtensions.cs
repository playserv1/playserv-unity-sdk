using System;

namespace Playserv.Proxy
{
    public static class ObservableExtensions
    {
        public static IDisposable Subscribe<T>(
            this IObservable<T> source,
            Action<T> onNext)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (onNext == null) throw new ArgumentNullException(nameof(onNext));

            return source.Subscribe(new AnonymousObserver<T>(onNext, null, null));
        }

        public static IDisposable Subscribe<T>(
            this IObservable<T> source,
            Action<T> onNext,
            Action<Exception> onError,
            Action onCompleted)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (onNext == null) throw new ArgumentNullException(nameof(onNext));
            if (onError == null) throw new ArgumentNullException(nameof(onError));

            return source.Subscribe(new AnonymousObserver<T>(onNext, onError, onCompleted));
        }
    }
}