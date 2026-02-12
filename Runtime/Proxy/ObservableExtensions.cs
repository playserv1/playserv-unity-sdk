using System;

namespace Playserv.Proxy
{
    /// <summary>
    /// Convenience extensions for subscribing to observables with delegates.
    /// </summary>
    public static class ObservableExtensions
    {
        /// <summary>
        /// Subscribes with <c>OnNext</c> callback only.
        /// </summary>
        /// <typeparam name="T">Observable item type.</typeparam>
        /// <param name="source">Observable source.</param>
        /// <param name="onNext">OnNext callback.</param>
        /// <returns>Disposable subscription handle.</returns>
        public static IDisposable Subscribe<T>(
            this IObservable<T> source,
            Action<T> onNext)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (onNext == null) throw new ArgumentNullException(nameof(onNext));

            return source.Subscribe(new AnonymousObserver<T>(onNext, null, null));
        }

        /// <summary>
        /// Subscribes with <c>OnNext</c>, <c>OnError</c> and <c>OnCompleted</c> callbacks.
        /// </summary>
        /// <typeparam name="T">Observable item type.</typeparam>
        /// <param name="source">Observable source.</param>
        /// <param name="onNext">OnNext callback.</param>
        /// <param name="onError">OnError callback.</param>
        /// <param name="onCompleted">OnCompleted callback.</param>
        /// <returns>Disposable subscription handle.</returns>
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
