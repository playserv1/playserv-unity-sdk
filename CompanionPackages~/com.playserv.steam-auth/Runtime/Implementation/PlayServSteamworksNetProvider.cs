using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Playserv.SteamAuth
{
    internal sealed class PlayServSteamworksNetProvider : IPlayServSteamAuthProvider
    {
        private static readonly object BridgeSync = new object();
        private static PlayServSteamworksNetBridge _bridge;

        public bool IsAvailable => TryGetBridge(out _);

        public bool IsInitialized => TryGetBridge(out var bridge) && bridge.IsInitialized;

        public Task<PlayServSteamCredential> GetCredentialAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!TryGetBridge(out var bridge))
            {
                throw new PlayServSteamAuthException(
                    "ticket acquisition",
                    "sdk_missing",
                    "Steamworks.NET with GetAuthTicketForWebApi support is not installed.");
            }

            if (!bridge.IsInitialized)
            {
                throw new PlayServSteamAuthException(
                    "ticket acquisition",
                    "sdk_not_initialized",
                    "Steamworks.NET must be initialized and the local user must be logged on.");
            }

            return bridge.GetCredentialAsync(ct);
        }

        private static bool TryGetBridge(out PlayServSteamworksNetBridge bridge)
        {
            lock (BridgeSync)
            {
                if (_bridge != null)
                {
                    bridge = _bridge;
                    return true;
                }

                if (!PlayServSteamworksNetBridge.TryCreate(out bridge))
                    return false;

                _bridge = bridge;
                return true;
            }
        }
    }

    internal sealed class PlayServSteamworksNetBridge
    {
        private readonly object _sync = new object();
        private readonly Dictionary<ulong, RequestState> _requests = new Dictionary<ulong, RequestState>();
        private readonly MethodInfo _getTicket;
        private readonly MethodInfo _cancelTicket;
        private readonly MethodInfo _isSteamRunning;
        private readonly MethodInfo _isLoggedOn;
        private readonly Type _callbackType;
        private readonly Type _responseType;
        private object _callbackRegistration;

        private PlayServSteamworksNetBridge(
            MethodInfo getTicket,
            MethodInfo cancelTicket,
            MethodInfo isSteamRunning,
            MethodInfo isLoggedOn,
            Type callbackType,
            Type responseType)
        {
            _getTicket = getTicket;
            _cancelTicket = cancelTicket;
            _isSteamRunning = isSteamRunning;
            _isLoggedOn = isLoggedOn;
            _callbackType = callbackType;
            _responseType = responseType;
        }

        public bool IsInitialized
        {
            get
            {
                try
                {
                    var running = (bool)_isSteamRunning.Invoke(null, null);
                    var loggedOn = (bool)_isLoggedOn.Invoke(null, null);
                    return running && loggedOn;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool TryCreate(out PlayServSteamworksNetBridge bridge)
        {
            bridge = null;
            var steamUser = FindType("Steamworks.SteamUser");
            var steamApi = FindType("Steamworks.SteamAPI");
            var response = FindType("Steamworks.GetTicketForWebApiResponse_t");
            var callbackOpen = FindType("Steamworks.Callback`1");
            if (steamUser == null || steamApi == null || response == null || callbackOpen == null)
                return false;

            var getTicket = steamUser.GetMethod(
                "GetAuthTicketForWebApi",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);
            var cancelTicket = steamUser.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(method => method.Name == "CancelAuthTicket" && method.GetParameters().Length == 1);
            var isRunning = steamApi.GetMethod("IsSteamRunning", BindingFlags.Public | BindingFlags.Static);
            var isLoggedOn = steamUser.GetMethod("BLoggedOn", BindingFlags.Public | BindingFlags.Static);
            if (getTicket == null || cancelTicket == null || isRunning == null || isLoggedOn == null)
                return false;

            try
            {
                var callbackType = callbackOpen.MakeGenericType(response);
                var create = callbackType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(method =>
                        method.Name == "Create" &&
                        method.GetParameters().Length == 1 &&
                        typeof(Delegate).IsAssignableFrom(method.GetParameters()[0].ParameterType));
                if (create == null ||
                    ReadableInstanceMember(response, "m_hAuthTicket") == null ||
                    ReadableInstanceMember(response, "m_eResult") == null ||
                    ReadableInstanceMember(response, "m_rgubTicket") == null ||
                    ReadableInstanceMember(response, "m_cubTicket") == null)
                {
                    return false;
                }

                bridge = new PlayServSteamworksNetBridge(
                    getTicket,
                    cancelTicket,
                    isRunning,
                    isLoggedOn,
                    callbackType,
                    response);
                return true;
            }
            catch
            {
                bridge = null;
                return false;
            }
        }

        public Task<PlayServSteamCredential> GetCredentialAsync(CancellationToken ct)
        {
            try
            {
                EnsureCallbackRegistered();
            }
            catch (PlayServSteamAuthException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("ticket acquisition", "unsupported_sdk", ex);
            }

            object handle;
            try
            {
                // The backend currently omits Steam's optional identity parameter when it
                // validates the ticket, so this must remain null rather than "playserv".
                handle = _getTicket.Invoke(null, new object[] { null });
            }
            catch (Exception ex)
            {
                throw CreateException("ticket acquisition", "provider_call_failed", ex);
            }

            var key = ReadHandle(handle);
            if (key == 0)
            {
                throw new PlayServSteamAuthException(
                    "ticket acquisition",
                    "invalid_ticket_handle",
                    "Steamworks.NET returned an invalid authentication ticket handle.");
            }

            var state = new RequestState(handle);
            lock (_sync)
            {
                if (_requests.ContainsKey(key))
                {
                    CancelTicket(handle);
                    throw new PlayServSteamAuthException(
                        "ticket acquisition",
                        "duplicate_ticket_handle",
                        "Steamworks.NET reused a ticket handle while its previous request was still pending.");
                }

                _requests.Add(key, state);
            }

            if (ct.CanBeCanceled)
            {
                var registration = ct.Register(() => CancelPending(key));
                state.Cancellation = registration;
                if (state.Completion.Task.IsCompleted)
                    registration.Dispose();
            }

            return state.Completion.Task;
        }

        internal static string EncodeTicket(byte[] bytes, int length)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            if (length <= 0 || length > bytes.Length)
                throw new ArgumentOutOfRangeException(nameof(length));

            var builder = new StringBuilder(length * 2);
            for (var i = 0; i < length; i++)
                builder.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private void EnsureCallbackRegistered()
        {
            lock (_sync)
            {
                if (_callbackRegistration != null)
                    return;

                var create = _callbackType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(method => method.Name == "Create" && method.GetParameters().Length == 1);
                if (create == null)
                    throw new PlayServSteamAuthException("ticket acquisition", "unsupported_sdk", "Steamworks callback API is incompatible.");

                var callbackMethod = GetType()
                    .GetMethod(nameof(OnTypedCallback), BindingFlags.NonPublic | BindingFlags.Instance)
                    .MakeGenericMethod(_responseType);
                var delegateType = create.GetParameters()[0].ParameterType;
                var callback = Delegate.CreateDelegate(delegateType, this, callbackMethod);
                _callbackRegistration = create.Invoke(null, new object[] { callback });
                if (_callbackRegistration == null)
                    throw new PlayServSteamAuthException("ticket acquisition", "unsupported_sdk", "Steamworks callback registration failed.");
            }
        }

        private void OnTypedCallback<T>(T response)
        {
            CompleteCallback(response);
        }

        private void CompleteCallback(object response)
        {
            var handle = ReadMember(response, "m_hAuthTicket");
            var key = ReadHandle(handle);
            RequestState state;
            lock (_sync)
            {
                if (!_requests.TryGetValue(key, out state))
                    return;
                _requests.Remove(key);
            }

            state.Cancellation.Dispose();
            try
            {
                var result = ReadMember(response, "m_eResult");
                if (!IsOk(result))
                {
                    CancelTicket(state.Handle);
                    state.Completion.TrySetException(new PlayServSteamAuthException(
                        "ticket acquisition",
                        "provider_rejected",
                        $"Steam returned result '{result}'."));
                    return;
                }

                var bytes = ReadMember(response, "m_rgubTicket") as byte[];
                var length = Convert.ToInt32(ReadMember(response, "m_cubTicket"), CultureInfo.InvariantCulture);
                var ticket = EncodeTicket(bytes, length);
                state.Completion.TrySetResult(new PlayServSteamCredential(ticket, () => CancelTicket(state.Handle)));
            }
            catch (Exception ex)
            {
                CancelTicket(state.Handle);
                state.Completion.TrySetException(CreateException("ticket acquisition", "invalid_provider_response", ex));
            }
        }

        private void CancelPending(ulong key)
        {
            RequestState state;
            lock (_sync)
            {
                if (!_requests.TryGetValue(key, out state))
                    return;
                _requests.Remove(key);
            }

            state.Cancellation.Dispose();
            CancelTicket(state.Handle);
            state.Completion.TrySetCanceled();
        }

        private void CancelTicket(object handle)
        {
            try
            {
                _cancelTicket.Invoke(null, new[] { handle });
            }
            catch
            {
                // Ticket release is best effort and never exposes credential material.
            }
        }

        private static bool IsOk(object result)
        {
            if (result == null)
                return false;
            if (string.Equals(result.ToString(), "OK", StringComparison.OrdinalIgnoreCase))
                return true;
            try
            {
                return Convert.ToInt32(result, CultureInfo.InvariantCulture) == 1;
            }
            catch
            {
                return false;
            }
        }

        private static ulong ReadHandle(object handle)
        {
            if (handle == null)
                return 0;
            var value = ReadMember(handle, "m_HAuthTicket") ?? ReadMember(handle, "Value");
            try
            {
                return Convert.ToUInt64(value ?? handle, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        private static object ReadMember(object source, string name)
        {
            if (source == null)
                return null;
            var type = source.GetType();
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
                return field.GetValue(source);
            return type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(source, null);
        }

        private static MemberInfo ReadableInstanceMember(Type type, string name)
        {
            return (MemberInfo)type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                   ?? type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        private static Type FindType(string fullName)
        {
            var direct = Type.GetType(fullName);
            if (direct != null)
                return direct;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName);
                if (type != null)
                    return type;
            }
            return null;
        }

        private static PlayServSteamAuthException CreateException(string operation, string code, Exception _)
        {
            return new PlayServSteamAuthException(
                operation,
                code,
                "Steamworks.NET could not complete the provider operation.");
        }

        private sealed class RequestState
        {
            public RequestState(object handle)
            {
                Handle = handle;
                Completion = new TaskCompletionSource<PlayServSteamCredential>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public object Handle { get; }

            public TaskCompletionSource<PlayServSteamCredential> Completion { get; }

            public CancellationTokenRegistration Cancellation;
        }
    }
}
