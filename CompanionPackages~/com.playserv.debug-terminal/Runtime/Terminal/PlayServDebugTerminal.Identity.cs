using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Playserv.Identity;
using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.DebugTerminal
{
    public sealed partial class PlayServDebugTerminal
    {
        private enum CredentialOperation
        {
            None,
            RefreshAccessToken,
            Login,
            Link,
            Merge,
            External
        }

        private CredentialOperation _credentialOperation;
        private PlayServExternalLoginMode _credentialLoginMode;
        private PlayServMergeChoice _credentialMergeChoice;
        private PlayServAuthConflict _pendingIdentityConflict;
        private string _credentialProvider = string.Empty;
        private string _credentialValue = string.Empty;
        private string _credentialProviderMode = string.Empty;
        private string _credentialNonce = string.Empty;
        private string _credentialPromptError = string.Empty;
        private bool _credentialSubmitting;
        private string _externalCredentialTitle = string.Empty;
        private string _externalCredentialLabel = string.Empty;
        private Func<string, Task> _externalCredentialCallback;

        private bool IsIdentityCredentialPromptVisible =>
            _credentialOperation != CredentialOperation.None;

        private async Task ExecuteIdentityCommandAsync(IReadOnlyList<string> parts)
        {
            var operation = parts.Count > 1 ? parts[1].ToLowerInvariant() : "session";
            switch (operation)
            {
                case "session":
                    PrintIdentitySession();
                    return;
                case "login":
                    if (!TryReadProvider(parts, out var loginProvider))
                        return;
                    var mode = parts.Count > 3 ? parts[3].ToLowerInvariant() : "preserve";
                    if (mode != "preserve" && mode != "recover")
                    {
                        AddLog("Usage: identity login <provider> [preserve|recover]");
                        return;
                    }
                    OpenCredentialPrompt(
                        CredentialOperation.Login,
                        loginProvider,
                        mode == "recover"
                            ? PlayServExternalLoginMode.RecoverProviderAccount
                            : PlayServExternalLoginMode.PreserveCurrentPlayer);
                    return;
                case "link":
                    if (!TryReadProvider(parts, out var linkProvider))
                        return;
                    OpenCredentialPrompt(CredentialOperation.Link, linkProvider);
                    return;
                case "unlink":
                    if (!TryReadProvider(parts, out var unlinkProvider))
                        return;
                    await HandleIdentityResultAsync(
                        "Identity unlink",
                        await PlayServAuth.UnlinkIdentityAsync(unlinkProvider));
                    return;
                case "merge":
                    if (_pendingIdentityConflict == null)
                    {
                        AddLog("No pending identity conflict. Run identity login/link first.");
                        return;
                    }
                    if (parts.Count < 3 || !TryParseMergeChoice(parts[2], out var choice))
                    {
                        AddLog("Usage: identity merge <current|conflicting>");
                        return;
                    }
                    _credentialMergeChoice = choice;
                    OpenCredentialPrompt(
                        CredentialOperation.Merge,
                        _pendingIdentityConflict.ProviderId);
                    return;
                default:
                    AddLog("Usage: identity <session|login|link|unlink|merge>");
                    return;
            }
        }

        private void OpenPlayerAccessTokenPrompt()
        {
            OpenCredentialPrompt(CredentialOperation.RefreshAccessToken, string.Empty);
        }

        private void OpenCredentialPrompt(
            CredentialOperation operation,
            string provider,
            PlayServExternalLoginMode loginMode = PlayServExternalLoginMode.PreserveCurrentPlayer)
        {
            if (_credentialSubmitting)
            {
                AddLog("A credential operation is already in progress.");
                return;
            }

            _credentialOperation = operation;
            _credentialLoginMode = loginMode;
            _credentialProvider = provider?.Trim() ?? string.Empty;
            _credentialValue = string.Empty;
            _credentialProviderMode = string.Empty;
            _credentialNonce = string.Empty;
            _credentialPromptError = string.Empty;
            _inputHasFocus = false;
        }

        internal void OpenExternalCredentialPrompt(
            string title,
            string label,
            Func<string, Task> callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));
            if (_credentialSubmitting || IsIdentityCredentialPromptVisible)
            {
                AddLog("A secure credential operation is already in progress.");
                return;
            }

            _credentialOperation = CredentialOperation.External;
            _externalCredentialTitle = string.IsNullOrWhiteSpace(title) ? "Secure Value" : title.Trim();
            _externalCredentialLabel = string.IsNullOrWhiteSpace(label) ? "Secret" : label.Trim();
            _externalCredentialCallback = callback;
            _credentialValue = string.Empty;
            _credentialPromptError = string.Empty;
            _inputHasFocus = false;
        }

        private void DrawIdentityCredentialPopup()
        {
            var previousDepth = GUI.depth;
            var previousColor = GUI.color;
            try
            {
                GUI.depth = -1200;
                GUI.color = new Color(0f, 0f, 0f, 0.72f);
                GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none);
                GUI.color = previousColor;

                var width = Mathf.Min(560f, Mathf.Max(340f, Screen.width - 32f));
                var compact = _credentialOperation == CredentialOperation.RefreshAccessToken ||
                              _credentialOperation == CredentialOperation.External;
                var height = compact ? 230f : 330f;
                height = Mathf.Min(height, Mathf.Max(220f, Screen.height - 32f));
                var rect = new Rect(
                    (Screen.width - width) * 0.5f,
                    (Screen.height - height) * 0.5f,
                    width,
                    height);
                GUI.Box(rect, GUIContent.none, GUI.skin.window);

                var title = _credentialOperation == CredentialOperation.RefreshAccessToken
                    ? "Refresh Player Authorization"
                    : _credentialOperation == CredentialOperation.External
                        ? _externalCredentialTitle
                        : $"Identity {_credentialOperation}";
                var titleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 18,
                    fontStyle = FontStyle.Bold
                };
                var labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 14 };
                var fieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = 14 };
                var passwordStyle = new GUIStyle(GUI.skin.textField) { fontSize = 14 };
                var errorStyle = new GUIStyle(labelStyle)
                {
                    wordWrap = true
                };
                errorStyle.normal.textColor = new Color(1f, 0.42f, 0.36f);

                var x = rect.x + 18f;
                var y = rect.y + 14f;
                var contentWidth = rect.width - 36f;
                GUI.Label(new Rect(x, y, contentWidth, 28f), title, titleStyle);
                y += 38f;

                using (new IdentityGuiEnabledScope(!_credentialSubmitting))
                {
                    if (_credentialOperation != CredentialOperation.RefreshAccessToken &&
                        _credentialOperation != CredentialOperation.External)
                    {
                        DrawCredentialField("Provider", ref _credentialProvider, x, ref y, contentWidth, fieldStyle, false);
                    }

                    var credentialLabel = _credentialOperation == CredentialOperation.RefreshAccessToken
                        ? "Player Access Token"
                        : _credentialOperation == CredentialOperation.External
                            ? _externalCredentialLabel
                            : "Provider Credential";
                    DrawCredentialField(credentialLabel, ref _credentialValue, x, ref y, contentWidth, passwordStyle, true);

                    if (_credentialOperation != CredentialOperation.RefreshAccessToken &&
                        _credentialOperation != CredentialOperation.External)
                    {
                        DrawCredentialField("Provider Mode (optional)", ref _credentialProviderMode, x, ref y, contentWidth, fieldStyle, false);
                        DrawCredentialField("Nonce (optional)", ref _credentialNonce, x, ref y, contentWidth, fieldStyle, false);
                    }
                }

                if (!string.IsNullOrWhiteSpace(_credentialPromptError))
                    GUI.Label(new Rect(x, y, contentWidth, 38f), _credentialPromptError, errorStyle);

                var buttonY = rect.yMax - 48f;
                var buttonWidth = (contentWidth - 10f) * 0.5f;
                using (new IdentityGuiEnabledScope(!_credentialSubmitting))
                {
                    if (GUI.Button(new Rect(x, buttonY, buttonWidth, 32f), "Cancel"))
                        CloseCredentialPrompt();
                    if (GUI.Button(
                            new Rect(x + buttonWidth + 10f, buttonY, buttonWidth, 32f),
                            _credentialSubmitting ? "Submitting..." : "Submit"))
                    {
                        _ = SubmitCredentialPromptAsync();
                    }
                }
            }
            finally
            {
                GUI.depth = previousDepth;
                GUI.color = previousColor;
            }
        }

        private async Task SubmitCredentialPromptAsync()
        {
            if (_credentialSubmitting)
                return;

            var credential = _credentialValue?.Trim() ?? string.Empty;
            if (credential.Length == 0)
            {
                _credentialPromptError = "Credential is required.";
                return;
            }

            _credentialSubmitting = true;
            _credentialPromptError = string.Empty;
            try
            {
                if (_credentialOperation == CredentialOperation.External)
                {
                    var callback = _externalCredentialCallback ??
                                   throw new InvalidOperationException("Secure operation callback is unavailable.");
                    await callback(credential);
                    CloseCredentialPrompt();
                    return;
                }

                if (_credentialOperation == CredentialOperation.RefreshAccessToken)
                {
                    var refreshed = await PlayServ.RefreshPlayerAuthAsync(credential);
                    _status = refreshed ? "Live player authorization refreshed" : "Player authorization refresh failed";
                    AddLog(_status);
                    if (refreshed)
                        CloseCredentialPrompt();
                    else
                        _credentialPromptError = _status;
                    return;
                }

                var proof = PlayServExternalIdentityProof.FromProviderToken(
                    _credentialProvider,
                    credential,
                    string.IsNullOrWhiteSpace(_credentialProviderMode) ? null : _credentialProviderMode,
                    string.IsNullOrWhiteSpace(_credentialNonce) ? null : _credentialNonce);

                PlayServAuthResult result;
                switch (_credentialOperation)
                {
                    case CredentialOperation.Login:
                        result = await PlayServAuth.LoginExternalAsync(proof, _credentialLoginMode);
                        break;
                    case CredentialOperation.Link:
                        result = await PlayServAuth.LinkIdentityAsync(proof);
                        break;
                    case CredentialOperation.Merge:
                        result = await PlayServAuth.MergeIdentityAsync(
                            _pendingIdentityConflict,
                            _credentialMergeChoice,
                            proof);
                        break;
                    default:
                        throw new InvalidOperationException("Unsupported credential operation.");
                }

                await HandleIdentityResultAsync($"Identity {_credentialOperation}", result);
                if (result.IsSuccess || result.Conflict != null)
                    CloseCredentialPrompt();
                else
                    _credentialPromptError = result.Error?.Message ?? "Identity operation failed.";
            }
            catch (Exception ex)
            {
                _credentialPromptError = SanitizeCredentialError(ex.Message, credential);
                AddLog($"Credential operation failed: {_credentialPromptError}");
            }
            finally
            {
                _credentialSubmitting = false;
                _credentialValue = string.Empty;
            }
        }

        internal static string SanitizeCredentialError(string message, string credential)
        {
            if (string.IsNullOrEmpty(message) || string.IsNullOrEmpty(credential))
                return message ?? string.Empty;

            return message.Replace(credential, "[REDACTED]");
        }

        private Task HandleIdentityResultAsync(string operation, PlayServAuthResult result)
        {
            if (result == null)
            {
                AddLog($"{operation} returned no result.");
                return Task.CompletedTask;
            }

            _pendingIdentityConflict = result.Conflict;
            if (result.IsSuccess)
            {
                _status = $"{operation} succeeded";
                AddLog(
                    $"{_status}; player={result.Session?.PlayerId}; session={result.Session?.Kind}; " +
                    $"transportReady={result.TransportReady}; mutation={result.IdentityMutationState}");
                return Task.CompletedTask;
            }

            if (result.Conflict != null)
            {
                _status = $"{operation} requires merge";
                AddLog(
                    $"{_status}; provider={result.Conflict.ProviderId}; kind={result.Conflict.Kind}; " +
                    $"current={result.Conflict.Current?.PlayerId}; conflicting={result.Conflict.Conflicting?.PlayerId}");
                return Task.CompletedTask;
            }

            _status = $"{operation} failed";
            AddLog($"{_status}: {FormatError(result.UnifiedError)}; mutation={result.IdentityMutationState}");
            return Task.CompletedTask;
        }

        private void PrintIdentitySession()
        {
            AddLog($"Identity session: {GetAuthSummary(includeProviders: true)}");
            if (_pendingIdentityConflict == null)
            {
                AddLog("Pending identity conflict: none");
                return;
            }

            AddLog(
                $"Pending conflict: provider={_pendingIdentityConflict.ProviderId}; " +
                $"kind={_pendingIdentityConflict.Kind}; current={_pendingIdentityConflict.Current?.PlayerId}; " +
                $"conflicting={_pendingIdentityConflict.Conflicting?.PlayerId}");
        }

        private bool TryReadProvider(IReadOnlyList<string> parts, out string provider)
        {
            provider = parts.Count > 2 ? parts[2]?.Trim() : string.Empty;
            if (!string.IsNullOrWhiteSpace(provider))
                return true;

            AddLog("Provider ID is required.");
            return false;
        }

        private static bool TryParseMergeChoice(string value, out PlayServMergeChoice choice)
        {
            if (string.Equals(value, "current", StringComparison.OrdinalIgnoreCase))
            {
                choice = PlayServMergeChoice.KeepCurrentPlayer;
                return true;
            }
            if (string.Equals(value, "conflicting", StringComparison.OrdinalIgnoreCase))
            {
                choice = PlayServMergeChoice.UseConflictingPlayer;
                return true;
            }

            choice = default;
            return false;
        }

        private void CloseCredentialPrompt()
        {
            _credentialOperation = CredentialOperation.None;
            _credentialProvider = string.Empty;
            _credentialValue = string.Empty;
            _credentialProviderMode = string.Empty;
            _credentialNonce = string.Empty;
            _credentialPromptError = string.Empty;
            _credentialSubmitting = false;
            _externalCredentialTitle = string.Empty;
            _externalCredentialLabel = string.Empty;
            _externalCredentialCallback = null;
            _focusInputNextFrame = true;
        }

        private static void DrawCredentialField(
            string label,
            ref string value,
            float x,
            ref float y,
            float width,
            GUIStyle style,
            bool password)
        {
            GUI.Label(new Rect(x, y, width, 20f), label);
            y += 20f;
            value = password
                ? GUI.PasswordField(new Rect(x, y, width, 28f), value ?? string.Empty, '*', style)
                : GUI.TextField(new Rect(x, y, width, 28f), value ?? string.Empty, style);
            y += 38f;
        }

        private readonly struct IdentityGuiEnabledScope : IDisposable
        {
            private readonly bool _previous;

            public IdentityGuiEnabledScope(bool enabled)
            {
                _previous = GUI.enabled;
                GUI.enabled = enabled;
            }

            public void Dispose()
            {
                GUI.enabled = _previous;
            }
        }
    }
}
