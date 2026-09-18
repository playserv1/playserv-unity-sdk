using System;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Http.Interfaces;
using Playserv.Proxy.Common;
using Playserv.Runtime.Abstractions;

namespace Playserv.Wrapper
{
    internal sealed partial class PlayServPlayerSession
    {
        internal long IdentityRevision { get; private set; }
        internal async Task DiscardBrowserBundleAsync(PlayerTokenBundleDto bundle)
        {
            if (string.IsNullOrWhiteSpace(bundle?.refresh_token)) return;
            try
            {
                using var budget = new AsyncOperationBudget(TimeSpan.FromSeconds(5), CancellationToken.None);
                await budget.RunAsync(_httpClient.SignOutAsync(_clientToken, bundle.refresh_token, budget.Token));
            }
            catch { }
        }
        internal Task<PlayServAuthResult> AdoptBrowserBundleAsync(PlayerTokenBundleDto bundle, bool allowSwitch, Func<bool> isCurrent, CancellationToken ct) =>
            RunOnUnityThreadAsync(async () =>
            {
                await _operationGate.WaitAsync(ct);
                try
                {
                    ThrowIfDisposed();
                    await EnsureStoredSessionLoadedAsync(ct);
                    if (!isCurrent()) throw new OperationCanceledException(ct);
                    if (!allowSwitch && !string.IsNullOrEmpty(PlayerId) && PlayerId != bundle.player_id)
                        return FailedResult(CreateAuthError(PlayServAuthErrorCode.BrowserPlayerSwitchRequired,
                            "This sign-in belongs to another player. Explicitly allow switching players to continue; progress is not merged.", null));
                    Interlocked.Increment(ref _generation);
                    try { await ApplyTokenBundleAsync(bundle, PlayServSessionKind.Registered, ct, isCurrent); }
                    catch (OperationCanceledException) { throw; }
                    catch { return FailedResult(CreateAuthError(PlayServAuthErrorCode.PersistenceFailed, "The browser session could not be saved.", null)); }
                    return new PlayServAuthResult(PlayServAuthOperationStatus.Success, CurrentSession, null, null, false);
                }
                finally { _operationGate.Release(); }
            }, ct);
    }
}
