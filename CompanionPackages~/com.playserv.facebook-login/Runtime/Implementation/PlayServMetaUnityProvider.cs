using System;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Playserv.FacebookLogin
{
    internal sealed class PlayServMetaUnityProvider : IPlayServFacebookLoginProvider
    {
        private readonly PlayServMetaUnityBridge _bridge;

        public PlayServMetaUnityProvider()
        {
            PlayServMetaUnityBridge.TryCreate(out _bridge);
        }

        public bool IsAvailable => _bridge != null;

        public bool IsInitialized => _bridge != null && _bridge.IsInitialized;

        public Task<PlayServFacebookCredential> GetCredentialAsync(
            PlayServFacebookLoginRequest request,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_bridge == null)
            {
                throw new PlayServFacebookLoginException(
                    "credential acquisition",
                    "sdk_missing",
                    "Meta Facebook SDK for Unity with Limited Login support is not installed.");
            }

            if (!_bridge.IsInitialized)
            {
                throw new PlayServFacebookLoginException(
                    "credential acquisition",
                    "sdk_not_initialized",
                    "Call FB.Init before requesting a Facebook Limited Login credential.");
            }

            return _bridge.GetCredentialAsync(request, ct);
        }
    }

    internal sealed class PlayServMetaUnityBridge
    {
        private readonly object _sync = new object();
        private readonly PropertyInfo _mobileProperty;
        private readonly PropertyInfo _initializedProperty;
        private readonly MethodInfo _login;
        private readonly MethodInfo _currentToken;
        private readonly Type _loginResultType;
        private readonly Type _delegateOpenType;
        private readonly object _limitedTrackingValue;
        private readonly bool _usesStaticMobileFacade;
        private RequestState _active;

        private PlayServMetaUnityBridge(
            PropertyInfo mobileProperty,
            PropertyInfo initializedProperty,
            MethodInfo login,
            MethodInfo currentToken,
            Type loginResultType,
            Type delegateOpenType,
            object limitedTrackingValue,
            bool usesStaticMobileFacade)
        {
            _mobileProperty = mobileProperty;
            _initializedProperty = initializedProperty;
            _login = login;
            _currentToken = currentToken;
            _loginResultType = loginResultType;
            _delegateOpenType = delegateOpenType;
            _limitedTrackingValue = limitedTrackingValue;
            _usesStaticMobileFacade = usesStaticMobileFacade;
        }

        public bool IsInitialized
        {
            get
            {
                try
                {
                    return (bool)_initializedProperty.GetValue(null, null) &&
                           (_usesStaticMobileFacade || GetMobile() != null);
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool TryCreate(out PlayServMetaUnityBridge bridge)
        {
            bridge = null;
            var fb = FindType("Facebook.Unity.FB");
            var loginResult = FindType("Facebook.Unity.ILoginResult");
            var delegateOpen = FindType("Facebook.Unity.FacebookDelegate`1");
            if (fb == null || loginResult == null || delegateOpen == null)
                return false;

            var mobile = fb.GetProperty("Mobile", BindingFlags.Public | BindingFlags.Static);
            var initialized = fb.GetProperty("IsInitialized", BindingFlags.Public | BindingFlags.Static);
            var mobileType = mobile?.PropertyType ?? fb.GetNestedType("Mobile", BindingFlags.Public);
            var mobileFlags = mobile == null
                ? BindingFlags.Public | BindingFlags.Static
                : BindingFlags.Public | BindingFlags.Instance;
            var login = mobileType?.GetMethods(mobileFlags)
                .FirstOrDefault(method =>
                    method.Name == "LoginWithTrackingPreference" &&
                    method.GetParameters().Length == 4);
            var currentToken = mobileType?.GetMethod("CurrentAuthenticationToken", mobileFlags);
            var loginParameters = login?.GetParameters();
            var expectedDelegate = delegateOpen.MakeGenericType(loginResult);
            var limitedTracking = ResolveLimitedTrackingValue(loginParameters?[0].ParameterType);
            if (mobileType == null || initialized == null || login == null || currentToken == null ||
                limitedTracking == null ||
                loginParameters[2].ParameterType != typeof(string) ||
                loginParameters[3].ParameterType != expectedDelegate ||
                !HasReadableMember(currentToken.ReturnType, "TokenString") ||
                !HasReadableMember(currentToken.ReturnType, "Nonce"))
                return false;

            bridge = new PlayServMetaUnityBridge(
                mobile,
                initialized,
                login,
                currentToken,
                loginResult,
                delegateOpen,
                limitedTracking,
                mobile == null);
            return true;
        }

        public Task<PlayServFacebookCredential> GetCredentialAsync(
            PlayServFacebookLoginRequest request,
            CancellationToken ct)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var nonce = string.IsNullOrWhiteSpace(request.Nonce)
                ? GenerateNonce()
                : request.Nonce;
            var state = new RequestState(nonce);
            lock (_sync)
            {
                if (_active != null)
                {
                    throw new PlayServFacebookLoginException(
                        "credential acquisition",
                        "operation_in_progress",
                        "Another Facebook Limited Login operation is already in progress.");
                }
                _active = state;
            }

            if (ct.CanBeCanceled)
            {
                var registration = ct.Register(CancelActive);
                state.Cancellation = registration;
                if (state.Completion.Task.IsCompleted)
                    registration.Dispose();
            }

            try
            {
                var callbackType = _delegateOpenType.MakeGenericType(_loginResultType);
                var callbackMethod = GetType()
                    .GetMethod(nameof(OnTypedLoginResult), BindingFlags.NonPublic | BindingFlags.Instance)
                    .MakeGenericMethod(_loginResultType);
                var callback = Delegate.CreateDelegate(callbackType, this, callbackMethod);
                _login.Invoke(
                    GetMobile(),
                    new[] { _limitedTrackingValue, request.Permissions.ToArray(), nonce, callback });
            }
            catch (Exception)
            {
                if (TryRemove(state))
                {
                    state.Cancellation.Dispose();
                    state.Completion.TrySetException(new PlayServFacebookLoginException(
                        "credential acquisition",
                        "provider_call_failed",
                        "Meta Unity SDK could not start Limited Login."));
                }
            }

            return state.Completion.Task;
        }

        internal static string GenerateNonce()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create())
                random.GetBytes(bytes);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private void OnTypedLoginResult<T>(T result)
        {
            CompleteLogin(result);
        }

        private void CompleteLogin(object result)
        {
            RequestState state;
            lock (_sync)
            {
                state = _active;
                _active = null;
            }

            if (state == null)
                return;
            state.Cancellation.Dispose();

            try
            {
                if (ReadBool(result, "Cancelled", "Canceled"))
                {
                    state.Completion.TrySetException(new PlayServFacebookLoginException(
                        "credential acquisition",
                        "provider_canceled",
                        "Facebook login was canceled."));
                    return;
                }

                var error = ReadString(result, "Error", "ErrorMessage");
                if (!string.IsNullOrWhiteSpace(error))
                {
                    state.Completion.TrySetException(new PlayServFacebookLoginException(
                        "credential acquisition",
                        "provider_rejected",
                        "Meta rejected the Limited Login request."));
                    return;
                }

                var token = _currentToken.Invoke(GetMobile(), null);
                var tokenString = ReadString(token, "TokenString");
                var tokenNonce = ReadString(token, "Nonce");
                if (string.IsNullOrWhiteSpace(tokenString) || string.IsNullOrWhiteSpace(tokenNonce))
                {
                    state.Completion.TrySetException(new PlayServFacebookLoginException(
                        "credential acquisition",
                        "invalid_provider_response",
                        "Facebook did not return a Limited Login authentication token and nonce."));
                    return;
                }

                if (!string.Equals(tokenNonce, state.Nonce, StringComparison.Ordinal))
                {
                    state.Completion.TrySetException(new PlayServFacebookLoginException(
                        "credential acquisition",
                        "nonce_mismatch",
                        "Facebook returned a credential for a different nonce."));
                    return;
                }

                state.Completion.TrySetResult(new PlayServFacebookCredential(tokenString, tokenNonce));
            }
            catch (Exception)
            {
                state.Completion.TrySetException(new PlayServFacebookLoginException(
                    "credential acquisition",
                    "invalid_provider_response",
                    "Meta returned an invalid Limited Login response."));
            }
        }

        private void CancelActive()
        {
            RequestState state;
            lock (_sync)
            {
                state = _active;
                _active = null;
            }
            if (state == null)
                return;
            state.Cancellation.Dispose();
            state.Completion.TrySetCanceled();
        }

        private bool TryRemove(RequestState state)
        {
            lock (_sync)
            {
                if (!ReferenceEquals(_active, state))
                    return false;
                _active = null;
                return true;
            }
        }

        private object GetMobile() => _usesStaticMobileFacade
            ? null
            : _mobileProperty.GetValue(null, null);

        private static object ResolveLimitedTrackingValue(Type trackingType)
        {
            if (trackingType == typeof(string))
                return "limited";
            if (trackingType == null || !trackingType.IsEnum)
                return null;
            try
            {
                return Enum.Parse(trackingType, "LIMITED", ignoreCase: true);
            }
            catch
            {
                return null;
            }
        }

        private static bool ReadBool(object source, params string[] names)
        {
            var value = ReadMember(source, names);
            return value is bool boolean && boolean;
        }

        private static string ReadString(object source, params string[] names) =>
            ReadMember(source, names)?.ToString() ?? string.Empty;

        private static object ReadMember(object source, params string[] names)
        {
            if (source == null)
                return null;
            foreach (var name in names)
            {
                var property = source.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (property != null)
                    return property.GetValue(source, null);
                var field = source.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                    return field.GetValue(source);
            }
            return null;
        }

        private static bool HasReadableMember(Type type, string name)
        {
            return type != null &&
                   (type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) != null ||
                    type.GetField(name, BindingFlags.Public | BindingFlags.Instance) != null);
        }

        private static Type FindType(string fullName)
        {
            var type = Type.GetType(fullName);
            if (type != null)
                return type;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(fullName);
                if (type != null)
                    return type;
            }
            return null;
        }

        private sealed class RequestState
        {
            public RequestState(string nonce)
            {
                Nonce = nonce;
                Completion = new TaskCompletionSource<PlayServFacebookCredential>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public string Nonce { get; }

            public TaskCompletionSource<PlayServFacebookCredential> Completion { get; }

            public CancellationTokenRegistration Cancellation;
        }
    }
}
