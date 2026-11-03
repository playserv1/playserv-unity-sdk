using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Code;
using Playserv.Wrapper;

namespace Playserv.DebugTerminal
{
    public sealed partial class PlayServDebugTerminal
    {
        private const int FunctionPreviewLimit = 2048;
        private CancellationTokenSource _codeCancellation;
        private DateTimeOffset? _activeCodeStartedAt;
        private string _activeCodeSummary = "none";
        private string _lastCodeSummary = "none";
        private PlayServFunctionTransferProgress _lastCodeProgress;

        private async Task ExecuteCodeCommandAsync(IReadOnlyList<string> parts)
        {
            var operation = parts.Count > 1 ? parts[1].ToLowerInvariant() : "status";
            switch (operation)
            {
                case "call":
                    await ExecuteCodeRequestAsync(parts, typedCall: true);
                    return;
                case "invoke":
                    await ExecuteCodeRequestAsync(parts, typedCall: false);
                    return;
                case "bytes":
                    await ExecuteBinaryCodeRequestAsync(parts, downloadToFile: false);
                    return;
                case "download":
                    await ExecuteBinaryCodeRequestAsync(parts, downloadToFile: true);
                    return;
                case "status":
                    PrintCodeStatus();
                    return;
                case "cancel":
                    CancelActiveCode(silent: false);
                    return;
                default:
                    AddLog("Usage: code <call|invoke|bytes|download|status|cancel> ...");
                    return;
            }
        }

        private async Task ExecuteBinaryCodeRequestAsync(
            IReadOnlyList<string> parts,
            bool downloadToFile)
        {
            if (_codeCancellation != null)
            {
                AddLog($"Cloud function operation is already running: {_activeCodeSummary}.");
                return;
            }

            var requiredCount = downloadToFile ? 5 : 4;
            if (parts.Count < requiredCount)
            {
                AddLog(downloadToFile
                    ? "Usage: code download <method> <slug> <path> [--input-file path] [--overwrite] [--max-response-bytes n] [request options]"
                    : "Usage: code bytes <method> <slug> [--input-file path] [--content-type value] [--max-response-bytes n] [request options]");
                return;
            }

            if (!Enum.TryParse(parts[2], true, out PlayServFunctionMethod method))
            {
                AddLog("Cloud function method must be GET, POST, PUT, PATCH, or DELETE.");
                return;
            }

            var optionsStart = downloadToFile ? 5 : 4;
            if (!DebugTerminalArguments.TryParse(
                    parts,
                    optionsStart,
                    new[] { "input-file", "content-type", "query", "version", "timeout", "max-response-bytes" },
                    new[] { "overwrite" },
                    out var arguments,
                    out var error))
            {
                AddLog(error);
                return;
            }
            if (arguments.Positionals.Count != 0)
            {
                AddLog($"Unexpected cloud function argument '{arguments.Positionals[0]}'.");
                return;
            }
            if (!arguments.TryGetInt("timeout", 0, 1, 3600, out var timeoutSeconds, out error) &&
                arguments.Has("timeout"))
            {
                AddLog(error);
                return;
            }
            var defaultLimit = downloadToFile
                ? PlayServFunctionDownloadOptions.DefaultMaxResponseBytes
                : PlayServFunctionTransferOptions.DefaultMaxResponseBytes;
            if (!arguments.TryGetLong(
                    "max-response-bytes",
                    defaultLimit,
                    1,
                    long.MaxValue,
                    out var maximumBytes,
                    out error))
            {
                AddLog(error);
                return;
            }
            if (!DebugTerminalArguments.TryParsePairs(arguments.GetAll("query"), out var query, out error))
            {
                AddLog(error);
                return;
            }

            var inputPath = arguments.Get("input-file");
#if UNITY_WEBGL && !UNITY_EDITOR
            if (downloadToFile || !string.IsNullOrWhiteSpace(inputPath))
                throw new PlatformNotSupportedException("Debug Terminal file transfers are unavailable on WebGL.");
#endif

            byte[] requestBytes = null;
            if (!string.IsNullOrWhiteSpace(inputPath))
            {
                if (!File.Exists(inputPath))
                {
                    AddLog($"Input file does not exist: {inputPath}");
                    return;
                }
                requestBytes = await Task.Run(() => File.ReadAllBytes(inputPath));
            }

            var slug = parts[3];
            var request = new PlayServFunctionRequest
            {
                Slug = slug,
                Method = method,
                Query = query,
                RawBodyBytes = requestBytes,
                ContentType = arguments.Get("content-type", "application/octet-stream"),
                Version = arguments.Get("version"),
                TimeoutSeconds = arguments.Has("timeout") ? timeoutSeconds : (int?)null
            };
            var cancellation = new CancellationTokenSource();
            _codeCancellation = cancellation;
            _activeCodeStartedAt = DateTimeOffset.UtcNow;
            _activeCodeSummary = downloadToFile
                ? $"download {method.ToString().ToUpperInvariant()} {slug}"
                : $"bytes {method.ToString().ToUpperInvariant()} {slug}";
            _lastCodeProgress = null;
            var progress = new Progress<PlayServFunctionTransferProgress>(value => _lastCodeProgress = value);
            AddLog($"Cloud function => {_activeCodeSummary}");
            try
            {
                if (downloadToFile)
                {
                    var targetPath = parts[4];
                    var result = await PlayServCode.DownloadToFileAsync(
                        request,
                        targetPath,
                        new PlayServFunctionDownloadOptions
                        {
                            MaxResponseBytes = maximumBytes,
                            OverwriteExistingFile = arguments.Has("overwrite"),
                            Progress = progress
                        },
                        cancellation.Token);
                    if (result.IsSuccess)
                    {
                        var hash = await ComputeFileSha256Async(result.FilePath, cancellation.Token);
                        _lastCodeSummary =
                            $"{_activeCodeSummary}; http={result.Response?.StatusCode}; contentType={result.Response?.ContentType}; " +
                            $"bytes={result.BytesWritten}; sha256={hash}; file={result.FilePath}";
                    }
                    else
                    {
                        _lastCodeSummary = $"{_activeCodeSummary}; error={FormatError(result.Error)}";
                    }
                }
                else
                {
                    var result = await PlayServCode.InvokeBytesAsync(
                        request,
                        new PlayServFunctionTransferOptions
                        {
                            MaxResponseBytes = maximumBytes,
                            Progress = progress
                        },
                        cancellation.Token);
                    _lastCodeSummary = result.IsSuccess
                        ? FormatBinaryFunctionResponse(_activeCodeSummary, result.Response)
                        : $"{_activeCodeSummary}; error={FormatError(result.Error)}";
                }

                _status = _lastCodeSummary.Contains("; error=", StringComparison.Ordinal)
                    ? "Cloud function failed"
                    : "Cloud function completed";
                AddLog(_lastCodeSummary);
            }
            catch (OperationCanceledException)
            {
                _lastCodeSummary = $"{_activeCodeSummary}; canceled";
                _status = "Cloud function canceled";
                AddLog(_lastCodeSummary);
            }
            finally
            {
                if (ReferenceEquals(_codeCancellation, cancellation))
                {
                    _codeCancellation = null;
                    _activeCodeStartedAt = null;
                    _activeCodeSummary = "none";
                }
                cancellation.Dispose();
            }
        }

        private async Task ExecuteCodeRequestAsync(IReadOnlyList<string> parts, bool typedCall)
        {
            if (_codeCancellation != null)
            {
                AddLog($"Cloud function operation is already running: {_activeCodeSummary}.");
                return;
            }

            var firstArgument = typedCall ? 2 : 3;
            if (parts.Count <= firstArgument)
            {
                AddLog(typedCall
                    ? "Usage: code call <slug> [--body json] [--query key=value] [--version tag] [--timeout seconds]"
                    : "Usage: code invoke <GET|POST|PUT|PATCH|DELETE> <slug> [--body json] [--query key=value] [--version tag] [--timeout seconds]");
                return;
            }

            var method = PlayServFunctionMethod.Post;
            if (!typedCall && !Enum.TryParse(parts[2], true, out method))
            {
                AddLog("Cloud function method must be GET, POST, PUT, PATCH, or DELETE.");
                return;
            }

            if (!DebugTerminalArguments.TryParse(
                    parts,
                    firstArgument + 1,
                    new[] { "body", "query", "version", "timeout" },
                    Array.Empty<string>(),
                    out var arguments,
                    out var parseError))
            {
                AddLog(parseError);
                return;
            }

            if (arguments.Positionals.Count != 0)
            {
                AddLog($"Unexpected cloud function argument '{arguments.Positionals[0]}'.");
                return;
            }

            if (!arguments.TryGetInt("timeout", 0, 1, 3600, out var timeoutSeconds, out parseError) &&
                arguments.Has("timeout"))
            {
                AddLog(parseError);
                return;
            }

            if (!DebugTerminalArguments.TryParsePairs(arguments.GetAll("query"), out var query, out parseError))
            {
                AddLog(parseError);
                return;
            }

            if (!DebugTerminalArguments.TryParseJson(arguments.Get("body"), false, out var body, out parseError))
            {
                AddLog(parseError);
                return;
            }

            var slug = parts[firstArgument];
            var cancellation = new CancellationTokenSource();
            _codeCancellation = cancellation;
            _activeCodeStartedAt = DateTimeOffset.UtcNow;
            _activeCodeSummary = $"{method.ToString().ToUpperInvariant()} {slug}";
            AddLog($"Cloud function => {_activeCodeSummary}");
            try
            {
                PlayServFunctionResponse response;
                PlayServError error;
                if (typedCall)
                {
                    var result = await PlayServCode.CallAsync<object>(
                        slug,
                        body,
                        new PlayServFunctionCallOptions
                        {
                            Query = query,
                            Version = arguments.Get("version"),
                            TimeoutSeconds = arguments.Has("timeout") ? timeoutSeconds : (int?)null
                        },
                        cancellation.Token);
                    response = result.Response;
                    error = result.Error;
                }
                else
                {
                    var result = await PlayServCode.InvokeAsync(
                        new PlayServFunctionRequest
                        {
                            Slug = slug,
                            Method = method,
                            Query = query,
                            Body = body,
                            Version = arguments.Get("version"),
                            TimeoutSeconds = arguments.Has("timeout") ? timeoutSeconds : (int?)null
                        },
                        cancellation.Token);
                    response = result.Response;
                    error = result.Error;
                }

                _lastCodeSummary = error.IsError
                    ? $"{_activeCodeSummary}; error={FormatError(error)}"
                    : FormatFunctionResponse(_activeCodeSummary, response);
                _status = error.IsError ? "Cloud function failed" : "Cloud function completed";
                AddLog(_lastCodeSummary);
            }
            catch (OperationCanceledException)
            {
                _lastCodeSummary = $"{_activeCodeSummary}; canceled";
                _status = "Cloud function canceled";
                AddLog(_lastCodeSummary);
            }
            finally
            {
                if (ReferenceEquals(_codeCancellation, cancellation))
                {
                    _codeCancellation = null;
                    _activeCodeStartedAt = null;
                    _activeCodeSummary = "none";
                }
                cancellation.Dispose();
            }
        }

        internal static string FormatFunctionResponse(string request, PlayServFunctionResponse response)
        {
            if (response == null)
                return $"{request}; response=none";
            var body = (response.Body ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
            if (body.Length > FunctionPreviewLimit)
                body = body.Substring(0, FunctionPreviewLimit) + "…";
            return $"{request}; http={response.StatusCode}; contentType={response.ContentType}; body={body}";
        }

        internal static string FormatBinaryFunctionResponse(
            string request,
            PlayServFunctionResponse response)
        {
            if (response == null)
                return $"{request}; response=none";
            var bytes = response.BodyBytes ?? Array.Empty<byte>();
            return
                $"{request}; http={response.StatusCode}; contentType={response.ContentType}; " +
                $"bytes={bytes.Length}; sha256={ComputeSha256(bytes)}";
        }

        private void PrintCodeStatus()
        {
            if (_codeCancellation == null)
            {
                AddLog($"Cloud function: idle; last={_lastCodeSummary}");
                return;
            }
            var elapsed = _activeCodeStartedAt.HasValue
                ? (DateTimeOffset.UtcNow - _activeCodeStartedAt.Value).TotalMilliseconds
                : 0d;
            var progress = _lastCodeProgress == null
                ? "none"
                : $"{_lastCodeProgress.Direction} {_lastCodeProgress.BytesTransferred}/" +
                  $"{(_lastCodeProgress.TotalBytes.HasValue ? _lastCodeProgress.TotalBytes.Value.ToString(CultureInfo.InvariantCulture) : "?")}" +
                  $" ({(_lastCodeProgress.Fraction.HasValue ? (_lastCodeProgress.Fraction.Value * 100d).ToString("F1", CultureInfo.InvariantCulture) + "%" : "unknown")})";
            AddLog($"Cloud function: running; operation={_activeCodeSummary}; elapsed={elapsed:F0}ms; progress={progress}");
        }

        private void CancelActiveCode(bool silent)
        {
            if (_codeCancellation == null)
            {
                if (!silent)
                    AddLog("No cloud function operation is running.");
                return;
            }
            _codeCancellation.Cancel();
            if (!silent)
                AddLog($"Cancellation requested for cloud function '{_activeCodeSummary}'.");
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return ToLowerHex(sha.ComputeHash(bytes ?? Array.Empty<byte>()));
        }

        private static async Task<string> ComputeFileSha256Async(
            string path,
            CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = File.OpenRead(path);
                using var sha = SHA256.Create();
                var hash = sha.ComputeHash(stream);
                cancellationToken.ThrowIfCancellationRequested();
                return ToLowerHex(hash);
            }, cancellationToken);
        }

        private static string ToLowerHex(byte[] bytes)
        {
            var builder = new StringBuilder((bytes?.Length ?? 0) * 2);
            foreach (var value in bytes ?? Array.Empty<byte>())
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }
    }
}
