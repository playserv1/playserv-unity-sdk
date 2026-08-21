using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Wrapper;

namespace Playserv.DataSubscription
{
    public enum PlayServSubscriptionState
    {
        Active = 0,
        Reconnecting = 1,
        Closed = 2,
        Terminated = 3
    }

    public sealed class PlayServSubscriptionCloseResult
    {
        internal PlayServSubscriptionCloseResult(
            bool isSuccess,
            bool wasAlreadyClosed,
            PlayServError error)
        {
            IsSuccess = isSuccess;
            WasAlreadyClosed = wasAlreadyClosed;
            Error = error;
        }

        public bool IsSuccess { get; }

        public bool WasAlreadyClosed { get; }

        public PlayServError Error { get; }

        internal static PlayServSubscriptionCloseResult Success(bool alreadyClosed = false) =>
            new PlayServSubscriptionCloseResult(true, alreadyClosed, null);

        internal static PlayServSubscriptionCloseResult Failed(PlayServError error) =>
            new PlayServSubscriptionCloseResult(false, false, error);
    }

    /// <summary>
    /// A realtime subscription whose current server snapshot can be requested explicitly.
    /// </summary>
    public interface IPlayServRefreshableSubscription
    {
        /// <summary>
        /// Requests and applies the current snapshot for this subscription.
        /// </summary>
        /// <param name="ct">Cancels waiting for the refresh without closing the subscription.</param>
        Task RefreshAsync(CancellationToken ct = default);
    }

    /// <summary>Lifecycle shared by realtime entity and collection handles.</summary>
    public interface IPlayServSubscriptionHandle : IDisposable
    {
        PlayServSubscriptionState State { get; }

        PlayServError TerminalError { get; }

        event Action<PlayServError> Failure;

        Task<PlayServSubscriptionCloseResult> CloseAsync(CancellationToken ct = default);
    }
}
