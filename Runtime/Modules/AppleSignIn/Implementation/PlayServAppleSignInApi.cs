using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Playserv.AppleSignIn
{
    public sealed class PlayServAppleSignInApi : IPlayServAppleSignInApi
    {
        private const string SignInOperation = "SignIn";
        private const string QuickLoginOperation = "QuickLogin";
        private const string CredentialStateOperation = "GetCredentialState";

        private static readonly object SyncRoot = new object();
        private static int _nextRequestId;

        private readonly Dictionary<int, SignInRequestState> _signInRequests =
            new Dictionary<int, SignInRequestState>();

        private readonly Dictionary<int, CredentialStateRequestState> _credentialStateRequests =
            new Dictionary<int, CredentialStateRequestState>();

        public PlayServAppleSignInApi()
        {
            PlayServAppleSignInNative.CredentialReceived += CompleteSignIn;
            PlayServAppleSignInNative.SignInFailed += FailSignIn;
            PlayServAppleSignInNative.CredentialStateReceived += CompleteCredentialState;
            PlayServAppleSignInNative.CredentialsRevoked += OnCredentialsRevoked;
            PlayServAppleSignInNative.SetCredentialsRevokedCallbackEnabled(true);
        }

        public event Action CredentialsRevoked;

        public bool IsAvailable => PlayServAppleSignInNative.IsSupported();

        public PlayServAppleSignInSettings Settings => PlayServAppleSignInSettingsProvider.LoadOrDefault();

        public Task<PlayServAppleSignInCredential> SignInAsync(
            PlayServAppleSignInRequest request = null,
            CancellationToken ct = default)
        {
            return StartSignIn(SignInOperation, quickLogin: false, request: request, ct: ct);
        }

        public Task<PlayServAppleSignInCredential> QuickLoginAsync(
            PlayServAppleSignInRequest request = null,
            CancellationToken ct = default)
        {
            return StartSignIn(QuickLoginOperation, quickLogin: true, request: request, ct: ct);
        }

        public Task<PlayServAppleCredentialStateResult> GetCredentialStateAsync(
            string userId,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return FromException<PlayServAppleCredentialStateResult>(new ArgumentException("Apple user id is required.", nameof(userId)));

            if (!IsAvailable)
                return FromException<PlayServAppleCredentialStateResult>(CreatePlatformNotSupportedException());

            var requestId = NextRequestId();
            var state = new CredentialStateRequestState(CreateCredentialStateTaskCompletionSource());
            AddCredentialStateRequest(requestId, state);
            RegisterCancellation(requestId, state, ct);

            PlayServAppleSignInNative.GetCredentialState(requestId, userId.Trim());
            return state.Completion.Task;
        }

        private Task<PlayServAppleSignInCredential> StartSignIn(
            string operation,
            bool quickLogin,
            PlayServAppleSignInRequest request,
            CancellationToken ct)
        {
            if (!IsAvailable)
                return FromException<PlayServAppleSignInCredential>(CreatePlatformNotSupportedException());

            request = request ?? Settings.CreateDefaultRequest();
            var requestId = NextRequestId();
            var state = new SignInRequestState(operation, CreateSignInTaskCompletionSource());
            AddSignInRequest(requestId, state);
            RegisterCancellation(requestId, state, ct);

            var scopes = (int)request.Scopes;
            if (quickLogin)
                PlayServAppleSignInNative.QuickLogin(requestId, scopes, request.Nonce, request.State);
            else
                PlayServAppleSignInNative.SignIn(requestId, scopes, request.Nonce, request.State);

            return state.Completion.Task;
        }

        private void CompleteSignIn(int requestId, PlayServAppleSignInCredential credential)
        {
            var state = RemoveSignInRequest(requestId);
            if (state == null)
                return;

            state.Dispose();
            state.Completion.TrySetResult(credential);
        }

        private void FailSignIn(int requestId, int code, string message)
        {
            var state = RemoveSignInRequest(requestId);
            if (state == null)
                return;

            state.Dispose();
            state.Completion.TrySetException(new PlayServAppleSignInException(state.Operation, code, message));
        }

        private void CompleteCredentialState(int requestId, PlayServAppleCredentialStateResult result)
        {
            var state = RemoveCredentialStateRequest(requestId);
            if (state == null)
                return;

            state.Dispose();
            state.Completion.TrySetResult(result);
        }

        private void OnCredentialsRevoked()
        {
            CredentialsRevoked?.Invoke();
        }

        private void AddSignInRequest(int requestId, SignInRequestState state)
        {
            lock (SyncRoot)
            {
                _signInRequests[requestId] = state;
            }
        }

        private SignInRequestState RemoveSignInRequest(int requestId)
        {
            lock (SyncRoot)
            {
                if (!_signInRequests.TryGetValue(requestId, out var state))
                    return null;

                _signInRequests.Remove(requestId);
                return state;
            }
        }

        private void AddCredentialStateRequest(int requestId, CredentialStateRequestState state)
        {
            lock (SyncRoot)
            {
                _credentialStateRequests[requestId] = state;
            }
        }

        private CredentialStateRequestState RemoveCredentialStateRequest(int requestId)
        {
            lock (SyncRoot)
            {
                if (!_credentialStateRequests.TryGetValue(requestId, out var state))
                    return null;

                _credentialStateRequests.Remove(requestId);
                return state;
            }
        }

        private void RegisterCancellation(int requestId, SignInRequestState state, CancellationToken ct)
        {
            if (!ct.CanBeCanceled)
                return;

            state.Cancellation = ct.Register(() =>
            {
                var removed = RemoveSignInRequest(requestId);
                if (removed == null)
                    return;

                removed.Dispose();
                removed.Completion.TrySetCanceled();
            });
        }

        private void RegisterCancellation(int requestId, CredentialStateRequestState state, CancellationToken ct)
        {
            if (!ct.CanBeCanceled)
                return;

            state.Cancellation = ct.Register(() =>
            {
                var removed = RemoveCredentialStateRequest(requestId);
                if (removed == null)
                    return;

                removed.Dispose();
                removed.Completion.TrySetCanceled();
            });
        }

        private static int NextRequestId()
        {
            return Interlocked.Increment(ref _nextRequestId);
        }

        private static PlatformNotSupportedException CreatePlatformNotSupportedException()
        {
            return new PlatformNotSupportedException("Apple Sign In is available only on iOS 13+ player builds.");
        }

        private static TaskCompletionSource<PlayServAppleSignInCredential> CreateSignInTaskCompletionSource()
        {
            return new TaskCompletionSource<PlayServAppleSignInCredential>();
        }

        private static TaskCompletionSource<PlayServAppleCredentialStateResult> CreateCredentialStateTaskCompletionSource()
        {
            return new TaskCompletionSource<PlayServAppleCredentialStateResult>();
        }

        private static Task<T> FromException<T>(Exception exception)
        {
            var completion = new TaskCompletionSource<T>();
            completion.SetException(exception);
            return completion.Task;
        }

        private sealed class SignInRequestState : IDisposable
        {
            public SignInRequestState(string operation, TaskCompletionSource<PlayServAppleSignInCredential> completion)
            {
                Operation = operation;
                Completion = completion;
            }

            public string Operation { get; }

            public TaskCompletionSource<PlayServAppleSignInCredential> Completion { get; }

            public CancellationTokenRegistration Cancellation;

            public void Dispose()
            {
                Cancellation.Dispose();
            }
        }

        private sealed class CredentialStateRequestState : IDisposable
        {
            public CredentialStateRequestState(TaskCompletionSource<PlayServAppleCredentialStateResult> completion)
            {
                Completion = completion;
            }

            public TaskCompletionSource<PlayServAppleCredentialStateResult> Completion { get; }

            public CancellationTokenRegistration Cancellation;

            public void Dispose()
            {
                Cancellation.Dispose();
            }
        }
    }
}
