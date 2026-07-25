using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Playserv.GoogleSignIn
{
    internal sealed class PlayServGoogleSignInPluginBridge
    {
        public const string ProviderMissingMessage =
            "Google Sign-In Unity plugin is not installed. Add Google Sign-In for Unity to the project before calling this API.";

        private static readonly string[] GoogleSignInTypeNames =
        {
            "Google.GoogleSignIn",
            "GoogleSignIn"
        };

        private static readonly string[] ConfigurationTypeNames =
        {
            "Google.GoogleSignInConfiguration",
            "GoogleSignInConfiguration"
        };

        private readonly Type _googleSignInType;
        private readonly Type _configurationType;

        private PlayServGoogleSignInPluginBridge(Type googleSignInType, Type configurationType)
        {
            _googleSignInType = googleSignInType;
            _configurationType = configurationType;
        }

        public static bool IsAvailable =>
            FindType(GoogleSignInTypeNames) != null &&
            FindType(ConfigurationTypeNames) != null;

        public static bool TryCreate(out PlayServGoogleSignInPluginBridge bridge)
        {
            var googleSignInType = FindType(GoogleSignInTypeNames);
            var configurationType = FindType(ConfigurationTypeNames);
            if (googleSignInType == null || configurationType == null)
            {
                bridge = null;
                return false;
            }

            bridge = new PlayServGoogleSignInPluginBridge(googleSignInType, configurationType);
            return true;
        }

        public Task<PlayServGoogleSignInCredential> SignInAsync(
            PlayServGoogleSignInSettings settings,
            PlayServGoogleSignInRequest request,
            CancellationToken ct)
        {
            return InvokeSignInAsync("sign in", "SignIn", settings, request, ct);
        }

        public Task<PlayServGoogleSignInCredential> SignInSilentlyAsync(
            PlayServGoogleSignInSettings settings,
            PlayServGoogleSignInRequest request,
            CancellationToken ct)
        {
            return InvokeSignInAsync("silent sign in", "SignInSilently", settings, request, ct);
        }

        public void SignOut()
        {
            InvokeVoid("SignOut");
        }

        public void Disconnect()
        {
            InvokeVoid("Disconnect");
        }

        private async Task<PlayServGoogleSignInCredential> InvokeSignInAsync(
            string operation,
            string methodName,
            PlayServGoogleSignInSettings settings,
            PlayServGoogleSignInRequest request,
            CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                Configure(settings, request);

                var instance = GetDefaultInstance();
                var task = InvokeTask(instance, methodName);
                await task.ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();

                var resultProperty = task.GetType().GetProperty("Result", BindingFlags.Public | BindingFlags.Instance);
                var user = resultProperty?.GetValue(task, null);
                if (user == null)
                    throw new InvalidOperationException("Google Sign-In returned an empty user.");

                return MapCredential(user);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException(operation, ex);
            }
        }

        private void Configure(PlayServGoogleSignInSettings settings, PlayServGoogleSignInRequest request)
        {
            settings = settings ?? PlayServGoogleSignInSettingsProvider.LoadOrDefault();
            request = request ?? settings.CreateDefaultRequest();

            var configuration = Activator.CreateInstance(_configurationType);
            TrySetMember(configuration, "WebClientId", ResolveString(request.WebClientId, settings.WebClientId));
            TrySetMember(configuration, "RequestIdToken", request.RequestIdToken ?? settings.RequestIdToken);
            TrySetMember(configuration, "RequestAuthCode", request.RequestAuthCode ?? settings.RequestAuthCode);
            TrySetMember(configuration, "RequestEmail", request.RequestEmail ?? settings.RequestEmail);
            TrySetMember(configuration, "ForceTokenRefresh", request.ForceTokenRefresh ?? settings.ForceTokenRefresh);
            TrySetMember(configuration, "UseGameSignIn", request.UseGameSignIn ?? settings.UseGameSignIn);
            TrySetMember(configuration, "HostedDomain", ResolveString(request.HostedDomain, settings.HostedDomain));
            TrySetMember(configuration, "AccountName", ResolveString(request.AccountName, settings.AccountName));
            TrySetMember(configuration, "AdditionalScopes", MergeScopes(settings.AdditionalScopes, request.AdditionalScopes));

            var property = _googleSignInType.GetProperty("Configuration", BindingFlags.Public | BindingFlags.Static);
            if (property == null || !property.CanWrite)
                throw new MissingMemberException(_googleSignInType.FullName, "Configuration");

            property.SetValue(null, configuration, null);
        }

        private object GetDefaultInstance()
        {
            var property = _googleSignInType.GetProperty("DefaultInstance", BindingFlags.Public | BindingFlags.Static);
            var instance = property?.GetValue(null, null);
            if (instance == null)
                throw new MissingMemberException(_googleSignInType.FullName, "DefaultInstance");

            return instance;
        }

        private static Task InvokeTask(object instance, string methodName)
        {
            var method = instance.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            var result = method?.Invoke(instance, null);
            if (result is Task task)
                return task;

            throw new MissingMethodException(instance.GetType().FullName, methodName);
        }

        private void InvokeVoid(string methodName)
        {
            try
            {
                var instance = GetDefaultInstance();
                var method = instance.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
                if (method == null)
                    throw new MissingMethodException(instance.GetType().FullName, methodName);

                method.Invoke(instance, null);
            }
            catch (Exception ex)
            {
                throw CreateException(methodName, ex);
            }
        }

        private static PlayServGoogleSignInCredential MapCredential(object user)
        {
            return new PlayServGoogleSignInCredential(
                GetStringProperty(user, "UserId"),
                GetStringProperty(user, "Email"),
                GetStringProperty(user, "DisplayName"),
                GetStringProperty(user, "GivenName"),
                GetStringProperty(user, "FamilyName"),
                GetStringProperty(user, "ImageUrl", "PhotoUrl"),
                GetStringProperty(user, "IdToken"),
                GetStringProperty(user, "AuthCode", "ServerAuthCode"));
        }

        private static bool TrySetMember(object target, string memberName, object value)
        {
            if (target == null)
                return false;

            var type = target.GetType();
            var property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanWrite)
                return TrySetValue(target, property.PropertyType, value, nextValue => property.SetValue(target, nextValue, null));

            var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
            if (field != null && !field.IsInitOnly)
                return TrySetValue(target, field.FieldType, value, nextValue => field.SetValue(target, nextValue));

            return false;
        }

        private static bool TrySetValue(object target, Type targetType, object value, Action<object> assign)
        {
            if (target == null || targetType == null || assign == null || value == null)
                return false;

            if (targetType == typeof(string) && string.IsNullOrWhiteSpace(value as string))
                return false;

            if (targetType == typeof(string[]))
            {
                assign(value as string[] ?? Array.Empty<string>());
                return true;
            }

            if (targetType == typeof(List<string>))
            {
                assign(new List<string>((string[])value));
                return true;
            }

            if (targetType.IsInstanceOfType(value))
            {
                assign(value);
                return true;
            }

            return false;
        }

        private static string GetStringProperty(object source, params string[] propertyNames)
        {
            if (source == null)
                return string.Empty;

            var type = source.GetType();
            for (var i = 0; i < propertyNames.Length; i++)
            {
                var property = type.GetProperty(propertyNames[i], BindingFlags.Public | BindingFlags.Instance);
                if (property != null)
                {
                    var value = property.GetValue(source, null);
                    return value?.ToString() ?? string.Empty;
                }

                var field = type.GetField(propertyNames[i], BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    var value = field.GetValue(source);
                    return value?.ToString() ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private static string ResolveString(string requestValue, string settingsValue)
        {
            if (!string.IsNullOrWhiteSpace(requestValue))
                return requestValue.Trim();

            return string.IsNullOrWhiteSpace(settingsValue)
                ? string.Empty
                : settingsValue.Trim();
        }

        private static string[] MergeScopes(string[] settingsScopes, string[] requestScopes)
        {
            return (settingsScopes ?? Array.Empty<string>())
                .Concat(requestScopes ?? Array.Empty<string>())
                .Where(scope => !string.IsNullOrWhiteSpace(scope))
                .Select(scope => scope.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static Type FindType(IEnumerable<string> typeNames)
        {
            foreach (var typeName in typeNames)
            {
                var type = Type.GetType(typeName);
                if (type != null)
                    return type;
            }

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                foreach (var typeName in typeNames)
                {
                    var type = assembly.GetType(typeName);
                    if (type != null)
                        return type;
                }
            }

            return null;
        }

        private static PlayServGoogleSignInException CreateException(string operation, Exception exception)
        {
            exception = Unwrap(exception);
            return new PlayServGoogleSignInException(operation, exception.Message, exception);
        }

        private static Exception Unwrap(Exception exception)
        {
            if (exception is TargetInvocationException targetInvocationException &&
                targetInvocationException.InnerException != null)
            {
                return Unwrap(targetInvocationException.InnerException);
            }

            if (exception is AggregateException aggregateException &&
                aggregateException.InnerExceptions.Count == 1)
            {
                return Unwrap(aggregateException.InnerExceptions[0]);
            }

            return exception;
        }
    }
}
