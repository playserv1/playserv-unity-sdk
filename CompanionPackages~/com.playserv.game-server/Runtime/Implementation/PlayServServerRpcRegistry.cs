using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playserv.Serialization;

namespace Playserv.GameServer
{
    /// <summary>Explicit wire parameter descriptor. Use Parameter&lt;T&gt; to retain AOT generic instantiations.</summary>
    public abstract class PlayServServerRpcParameter
    {
        public string Name { get; }
        public bool Required { get; }
        private protected PlayServServerRpcParameter(string name, bool required)
        {
            PlayServServerRpcRegistry.ValidateName(name);
            Name = name; Required = required;
        }
        internal abstract object Read(string json);
        internal abstract object DefaultValue { get; }
    }

    /// <summary>A strict JSON parameter. Optional arguments use the explicit immutable JSON snapshot of the default.</summary>
    public sealed class PlayServServerRpcParameter<T> : PlayServServerRpcParameter
    {
        private readonly IPlayServStrictJsonCodec _strict = PlayServServerRpcRegistry.RequireStrict(new NewtonsoftJsonCodec());
        private readonly string _defaultJson;
        public PlayServServerRpcParameter(string name, bool required = true, T defaultValue = default) : base(name, required)
        {
            _strict.ValidateResponseType(typeof(T));
            if (!required) _defaultJson = new NewtonsoftJsonCodec().Serialize(defaultValue);
        }
        internal override object Read(string json) => _strict.DeserializeStrict<T>(json);
        internal override object DefaultValue => Read(_defaultJson);
    }

    /// <summary>Bound arguments can only be accessed through the descriptors registered for this method.</summary>
    public sealed class PlayServServerRpcArguments
    {
        private readonly Dictionary<PlayServServerRpcParameter, object> _values;
        internal PlayServServerRpcArguments(Dictionary<PlayServServerRpcParameter, object> values) => _values = values;
        public T Get<T>(PlayServServerRpcParameter<T> parameter) => (T)_values[parameter];
    }

    /// <summary>Immutable platform-authenticated caller metadata. Does not grant REST/Records authority.</summary>
    public sealed class PlayServServerRpcContext
    {
        public string CallId { get; }
        public string MethodName { get; }
        public string PlayerId { get; }
        public string DisplayName { get; }
        public string CallerKind { get; }
        public string CallerKeyId { get; }
        public string ProjectId { get; }
        public string Environment { get; }
        public string PlayerProviders { get; }
        public long? ReceivedAtUnixMs { get; }
        public bool OneWay { get; }
        internal PlayServServerRpcContext(IDictionary<string, object> frame)
        {
            CallId = Text(frame, "id"); MethodName = Text(frame, "method_name");
            PlayerId = Text(frame, "player_id"); DisplayName = Text(frame, "display_name");
            CallerKind = Text(frame, "caller_kind"); CallerKeyId = Text(frame, "caller_key_id");
            ProjectId = Text(frame, "project_id"); Environment = Text(frame, "env");
            PlayerProviders = Text(frame, "player_providers");
            if (frame.TryGetValue("received_at_unix_ms", out var timestamp) && timestamp != null)
                ReceivedAtUnixMs = timestamp is long n ? n : timestamp is int i ? i : throw new ArgumentException();
            if (frame.TryGetValue("one_way", out var oneWay)) OneWay = oneWay is bool b ? b : throw new ArgumentException();
            // player_jwt is deliberately neither retained nor installed in an acting-player context.
        }
        internal static string Text(IDictionary<string, object> frame, string name) =>
            !frame.TryGetValue(name, out var value) || value == null ? null : value is string text ? text : throw new ArgumentException();
    }

    /// <summary>Explicit typed RPC method registry. Configure snapshots it; later registrations do not affect a running host.</summary>
    public sealed class PlayServServerRpcRegistry
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, Method> _methods = new Dictionary<string, Method>(StringComparer.Ordinal);

        /// <summary>Registers ordered positional or named parameters; unknown/missing arguments fail before the handler.</summary>
        public void Register<TResult>(string methodName, IEnumerable<PlayServServerRpcParameter> parameters,
            Func<PlayServServerRpcContext, PlayServServerRpcArguments, CancellationToken, Task<TResult>> handler)
        {
            ValidateName(methodName);
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var snapshot = parameters.ToArray();
            if (snapshot.Length > 64 || snapshot.Any(p => p == null) || snapshot.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
                throw new ArgumentException("RPC parameter descriptors must be unique and bounded.", nameof(parameters));
            var optional = false;
            foreach (var parameter in snapshot)
            {
                if (parameter.Required && optional) throw new ArgumentException("Required positional parameters must precede optional parameters.");
                optional |= !parameter.Required;
            }
            RequireStrict(new NewtonsoftJsonCodec()).ValidateResponseType(typeof(TResult));
            var method = new Method(snapshot, async (context, args, ct) => (object)await handler(context, args, ct));
            lock (_gate) _methods.Add(methodName, method);
        }

        internal Dictionary<string, Method> Snapshot() { lock (_gate) return new Dictionary<string, Method>(_methods, StringComparer.Ordinal); }
        internal static IPlayServStrictJsonCodec RequireStrict(IJsonCodec codec) => codec as IPlayServStrictJsonCodec ??
            throw new NotSupportedException("Inbound RPC requires a strict JSON codec.");
        internal static void ValidateName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 128 ||
                name.Any(c => !(char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == ':' || c == '-')))
                throw new ArgumentException("RPC names must contain 1–128 identifier characters.");
        }
        internal sealed class Method
        {
            private readonly PlayServServerRpcParameter[] _parameters;
            internal readonly Func<PlayServServerRpcContext, PlayServServerRpcArguments, CancellationToken, Task<object>> Invoke;
            internal Method(PlayServServerRpcParameter[] parameters,
                Func<PlayServServerRpcContext, PlayServServerRpcArguments, CancellationToken, Task<object>> invoke)
            { _parameters = parameters; Invoke = invoke; }
            internal PlayServServerRpcArguments Bind(string base64)
            {
                var codec = new NewtonsoftJsonCodec();
                var json = base64 == null ? "[]" : new UTF8Encoding(false, true).GetString(Convert.FromBase64String(base64));
                // The strict parser rejects malformed JSON, duplicate object keys and trailing input first.
                RequireStrict(codec).DeserializeStrict<object>(json);
                json = json.Trim();
                if (json[0] != '[' && json[0] != '{') throw new ArgumentException();
                var parts = SplitTopLevel(json.Substring(1, json.Length - 2), ',');
                Dictionary<string, string> named = null;
                List<string> positional = null;
                if (json[0] == '[') positional = parts;
                else
                {
                    named = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var part in parts)
                    {
                        var pair = SplitTopLevel(part, ':');
                        if (pair.Count != 2) throw new ArgumentException();
                        named.Add(RequireStrict(codec).DeserializeStrict<string>(pair[0]), pair[1]);
                    }
                }
                if (positional != null && positional.Count > _parameters.Length) throw new ArgumentException();
                if (named != null && named.Keys.Any(key => !_parameters.Any(p => p.Name == key))) throw new ArgumentException();
                var result = new Dictionary<PlayServServerRpcParameter, object>();
                for (var i = 0; i < _parameters.Length; i++)
                {
                    var parameter = _parameters[i];
                    string argument = null;
                    var present = named != null ? named.TryGetValue(parameter.Name, out argument) : i < positional.Count;
                    if (present && positional != null) argument = positional[i];
                    if (!present && parameter.Required) throw new ArgumentException();
                    result.Add(parameter, present ? parameter.Read(argument) : parameter.DefaultValue);
                }
                return new PlayServServerRpcArguments(result);
            }

            // Syntax was validated above. Slice, rather than reserialize, to preserve decimal precision,
            // exponent spelling and JSON strings that happen to look like dates.
            private static List<string> SplitTopLevel(string text, char separator)
            {
                var result = new List<string>();
                if (string.IsNullOrWhiteSpace(text)) return result;
                var start = 0; var depth = 0; var quoted = false; var escaped = false;
                for (var i = 0; i < text.Length; i++)
                {
                    var c = text[i];
                    if (quoted)
                    {
                        if (escaped) escaped = false;
                        else if (c == '\\') escaped = true;
                        else if (c == '"') quoted = false;
                    }
                    else if (c == '"') quoted = true;
                    else if (c == '{' || c == '[') depth++;
                    else if (c == '}' || c == ']') depth--;
                    else if (c == separator && depth == 0)
                    { result.Add(text.Substring(start, i - start)); start = i + 1; }
                }
                result.Add(text.Substring(start));
                return result;
            }
        }
    }
}
