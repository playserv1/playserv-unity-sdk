using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Playserv.EpicAuth
{
    internal sealed class PlayServEosContribProvider : IPlayServEpicAuthProvider
    {
        private readonly PlayServEosContribBridge _bridge;

        public PlayServEosContribProvider()
        {
            PlayServEosContribBridge.TryCreate(out _bridge);
        }

        public bool IsAvailable => _bridge != null;

        public bool IsInitialized => _bridge != null && _bridge.IsInitialized;

        public Task<PlayServEpicCredential> GetCredentialAsync(
            PlayServEpicAuthRequest request,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_bridge == null)
            {
                throw new PlayServEpicAuthException(
                    "credential acquisition",
                    "sdk_missing",
                    "EOS-Contrib/PlayEveryWare EOS Plugin for Unity is not installed or is incompatible.");
            }

            return Task.FromResult(_bridge.GetCredential(request));
        }
    }

    internal sealed class PlayServEosContribBridge
    {
        private readonly PropertyInfo _instanceProperty;
        private readonly MethodInfo _getAuthInterface;
        private readonly MethodInfo _getLocalUserId;
        private readonly MethodInfo _getLauncherArguments;
        private readonly MethodInfo _copyUserAuthToken;

        private PlayServEosContribBridge(
            PropertyInfo instanceProperty,
            MethodInfo getAuthInterface,
            MethodInfo getLocalUserId,
            MethodInfo getLauncherArguments,
            MethodInfo copyUserAuthToken)
        {
            _instanceProperty = instanceProperty;
            _getAuthInterface = getAuthInterface;
            _getLocalUserId = getLocalUserId;
            _getLauncherArguments = getLauncherArguments;
            _copyUserAuthToken = copyUserAuthToken;
        }

        public bool IsInitialized
        {
            get
            {
                try
                {
                    var manager = GetManager();
                    return manager != null && _getAuthInterface.Invoke(manager, null) != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool TryCreate(out PlayServEosContribBridge bridge)
        {
            bridge = null;
            var managerType = FindType("PlayEveryWare.EpicOnlineServices.EOSManager");
            if (managerType == null)
                return false;

            var instance = managerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                           ?? managerType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
            var serviceType = instance?.PropertyType;
            var auth = serviceType?.GetMethod("GetEOSAuthInterface", BindingFlags.Public | BindingFlags.Instance);
            var localUser = serviceType?.GetMethod("GetLocalUserId", BindingFlags.Public | BindingFlags.Instance);
            var launcher = managerType.GetMethod(
                               "GetCommandLineArgsFromEpicLauncher",
                               BindingFlags.Public | BindingFlags.Static)
                           ?? serviceType?.GetMethod(
                               "GetCommandLineArgsFromEpicLauncher",
                               BindingFlags.Public | BindingFlags.Instance);
            var accountType = FindType("Epic.OnlineServices.EpicAccountId");
            var fromString = accountType?.GetMethod(
                "FromString",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);
            var copy = auth?.ReturnType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => method.Name == "CopyUserAuthToken")
                .OrderByDescending(method => method.GetParameters().Length)
                .FirstOrDefault(method => method.GetParameters().Length == 3 || method.GetParameters().Length == 2);
            if (instance == null || auth == null || localUser == null || launcher == null ||
                fromString == null || copy == null ||
                !HasReadableMember(launcher.ReturnType, "authPassword", "AuthPassword") ||
                !HasReadableMember(
                    UnwrapNullable(UnwrapByRef(copy.GetParameters().Last().ParameterType)),
                    "AccessToken"))
                return false;

            bridge = new PlayServEosContribBridge(instance, auth, localUser, launcher, copy);
            return true;
        }

        public PlayServEpicCredential GetCredential(PlayServEpicAuthRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            try
            {
                return request.Source == PlayServEpicCredentialSource.LauncherExchangeCode
                    ? GetLauncherCredential(request)
                    : GetEosCredential(request);
            }
            catch (PlayServEpicAuthException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new PlayServEpicAuthException(
                    "credential acquisition",
                    "provider_call_failed",
                    "EOS could not complete the provider operation.");
            }
        }

        private PlayServEpicCredential GetEosCredential(PlayServEpicAuthRequest request)
        {
            var manager = GetRequiredManager();
            var authInterface = _getAuthInterface.Invoke(manager, null);
            if (authInterface == null)
                throw new PlayServEpicAuthException("EOS token acquisition", "sdk_not_initialized", "EOS Auth interface is unavailable.");

            object accountId;
            if (string.IsNullOrWhiteSpace(request.EpicAccountId))
            {
                accountId = _getLocalUserId.Invoke(manager, null);
            }
            else
            {
                var accountType = FindType("Epic.OnlineServices.EpicAccountId");
                var fromString = accountType?.GetMethod(
                    "FromString",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string) },
                    null);
                if (fromString == null)
                    throw new PlayServEpicAuthException("EOS token acquisition", "unsupported_sdk", "EpicAccountId.FromString is unavailable.");
                accountId = fromString.Invoke(null, new object[] { request.EpicAccountId });
            }

            if (accountId == null || IsInvalidAccountId(accountId))
                throw new PlayServEpicAuthException("EOS token acquisition", "account_not_logged_in", "No valid Epic Account Services local user is logged in.");

            var parameters = _copyUserAuthToken.GetParameters();
            var optionsType = UnwrapByRef(parameters[0].ParameterType);
            var options = Activator.CreateInstance(optionsType);
            object[] arguments;
            if (parameters.Length == 3)
            {
                arguments = new[] { options, accountId, null };
            }
            else
            {
                SetMember(options, "LocalUserId", accountId);
                arguments = new[] { options, null };
            }

            var result = _copyUserAuthToken.Invoke(authInterface, arguments);
            if (!string.Equals(result?.ToString(), "Success", StringComparison.OrdinalIgnoreCase))
            {
                throw new PlayServEpicAuthException(
                    "EOS token acquisition",
                    "provider_rejected",
                    $"EOS returned result '{result}'.");
            }

            var token = arguments[arguments.Length - 1];
            var accessToken = ReadString(token, "AccessToken");
            if (string.IsNullOrWhiteSpace(accessToken))
                throw new PlayServEpicAuthException("EOS token acquisition", "invalid_provider_response", "EOS returned an empty access token.");
            return new PlayServEpicCredential(PlayServEpicCredentialSource.EosAccessToken, accessToken);
        }

        private PlayServEpicCredential GetLauncherCredential(PlayServEpicAuthRequest request)
        {
            var exchangeCode = request.ExchangeCode;
            if (string.IsNullOrWhiteSpace(exchangeCode))
            {
                var target = _getLauncherArguments.IsStatic ? null : GetRequiredManager();
                var arguments = _getLauncherArguments.Invoke(target, null);
                exchangeCode = ReadString(arguments, "authPassword", "AuthPassword");
            }

            if (string.IsNullOrWhiteSpace(exchangeCode))
            {
                throw new PlayServEpicAuthException(
                    "launcher credential acquisition",
                    "exchange_code_missing",
                    "Epic Games Launcher did not supply an exchange code.");
            }

            return new PlayServEpicCredential(
                PlayServEpicCredentialSource.LauncherExchangeCode,
                exchangeCode.Trim());
        }

        private object GetRequiredManager()
        {
            var manager = GetManager();
            if (manager == null)
                throw new PlayServEpicAuthException("credential acquisition", "sdk_not_initialized", "EOSManager is not initialized.");
            return manager;
        }

        private object GetManager() => _instanceProperty.GetValue(null, null);

        private static bool IsInvalidAccountId(object accountId)
        {
            var isValid = accountId.GetType().GetMethod("IsValid", BindingFlags.Public | BindingFlags.Instance);
            return isValid != null && !(bool)isValid.Invoke(accountId, null);
        }

        private static void SetMember(object target, string name, object value)
        {
            var property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanWrite)
            {
                property.SetValue(target, value, null);
                return;
            }
            var field = target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
                field.SetValue(target, value);
        }

        private static string ReadString(object source, params string[] names)
        {
            if (source == null)
                return string.Empty;
            foreach (var name in names)
            {
                var property = source.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (property != null)
                    return property.GetValue(source, null)?.ToString() ?? string.Empty;
                var field = source.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                    return field.GetValue(source)?.ToString() ?? string.Empty;
            }
            return string.Empty;
        }

        private static bool HasReadableMember(Type type, params string[] names)
        {
            if (type == null)
                return false;
            return names.Any(name =>
                type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) != null ||
                type.GetField(name, BindingFlags.Public | BindingFlags.Instance) != null);
        }

        private static Type UnwrapByRef(Type type) => type.IsByRef ? type.GetElementType() : type;

        private static Type UnwrapNullable(Type type) => Nullable.GetUnderlyingType(type) ?? type;

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

    }
}
