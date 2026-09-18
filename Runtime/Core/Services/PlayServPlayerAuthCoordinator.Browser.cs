using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Http.Interfaces;
using Playserv.Proxy.Common;

namespace Playserv.Wrapper
{
    internal sealed partial class PlayServPlayerAuthCoordinator
    {
        private CancellationTokenSource _browserFlow;
        private int _browserInvalidationRevision;
        internal Func<IPlayServOAuthBrowser> BrowserFactory { get; set; } = () => new PlayServOAuthBrowser();

        internal void InvalidateBrowserLogin()
        {
            Interlocked.Increment(ref _browserInvalidationRevision);
            var flow = Volatile.Read(ref _browserFlow);
            try { flow?.Cancel(); } catch (ObjectDisposedException) { } catch (AggregateException) { }
        }

        public async Task<PlayServAuthResult> LoginBrowserAsync(string provider, PlayServBrowserLoginOptions options = null, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(provider)) throw new ArgumentException("An auth provider is required.", nameof(provider));
            var snapshot = new PlayServBrowserLoginOptions { Timeout = options?.Timeout ?? TimeSpan.FromMinutes(5), AllowPlayerSwitch = options?.AllowPlayerSwitch ?? false };
            if (snapshot.Timeout <= TimeSpan.Zero || snapshot.Timeout.TotalMilliseconds > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(options));
            if (Volatile.Read(ref _browserFlow) != null) return BrowserFailure(PlayServAuthErrorCode.BrowserFlowInProgress, "A browser sign-in is already running.");
            var settings = _getSettings();
            var invalid = ValidateManagedOperation(settings);
            if (invalid != null) return FailedResult(invalid, CurrentSession, _getState() == PlayServState.Online);
            var session = EnsureManagedSession(settings);
            settings.RuntimeTokenProvider = session;
            var clientToken = settings.ClientToken;
            var backend = settings.BackendServerAddress;
            var revision = session.IdentityRevision;
            var invalidationRevision = Volatile.Read(ref _browserInvalidationRevision);
            using var flow = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (Interlocked.CompareExchange(ref _browserFlow, flow, null) != null)
                return BrowserFailure(PlayServAuthErrorCode.BrowserFlowInProgress, "A browser sign-in is already running.");
            bool IsCurrent() => !flow.IsCancellationRequested && !_disposed && ReferenceEquals(_getSettings(), settings) &&
                settings.ClientToken == clientToken && settings.BackendServerAddress == backend &&
                ReferenceEquals(settings.RuntimeTokenProvider, session) && ReferenceEquals(_session, session) && session.IdentityRevision == revision;
            PlayerTokenBundleDto bundle = null;
            var adopted = false;
            try
            {
                // Reserve the WebGL popup synchronously, before any HTTP await loses the user gesture.
                using var browser = BrowserFactory();
                bundle = await PlayServBrowserOAuth.ClaimAsync(provider.Trim(), clientToken, _createHttpClient(settings), browser,
                    snapshot, flow.Token, discard: session.DiscardBrowserBundleAsync);
                if (!IsCurrent()) throw new OperationCanceledException(flow.Token);
                return await CoordinateSessionReplacementAsync(async (target, token) =>
                {
                    if (!ReferenceEquals(target, session)) throw new OperationCanceledException(token);
                    var result = await target.AdoptBrowserBundleAsync(bundle, snapshot.AllowPlayerSwitch, IsCurrent, token);
                    adopted = result.IsSuccess;
                    return result;
                }, "browser sign-in", null, flow.Token);
            }
            catch (PlayServBrowserAuthException error) { return BrowserFailure(error.Code, error.Message); }
            catch (OperationCanceledException) { throw; }
            catch { return BrowserFailure(PlayServAuthErrorCode.Network, "Browser sign-in could not complete."); }
            finally
            {
                Interlocked.CompareExchange(ref _browserFlow, null, flow);
                if (!_disposed && invalidationRevision == Volatile.Read(ref _browserInvalidationRevision) &&
                    ReferenceEquals(_getSettings(), settings) && settings.ClientToken == clientToken && settings.BackendServerAddress == backend &&
                    ReferenceEquals(settings.RuntimeTokenProvider, session) && session.CurrentSession.IsLoggedIn &&
                    _getState() == PlayServState.Online && _refreshLoopCancellation == null)
                    HandleConnected(settings);
                if (!adopted && bundle != null) await session.DiscardBrowserBundleAsync(bundle);
            }
        }
        private PlayServAuthResult BrowserFailure(PlayServAuthErrorCode code, string message) =>
            FailedResult(PlayServPlayerSession.CreateAuthError(code, message, null), CurrentSession, _getState() == PlayServState.Online);
    }
}
