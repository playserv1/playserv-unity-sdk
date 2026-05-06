using System;
using System.Collections.Generic;

namespace Playserv.RPC
{
    public enum RpcMappedPayloadKind
    {
        Named = 0,
        Positional = 1
    }

    /// <summary>
    /// Internal normalized RPC payload representation used between mapper and serializer.
    /// </summary>
    public sealed class RpcMappedPayload
    {
        private static readonly IReadOnlyDictionary<string, object> EmptyNamedPayload =
            new Dictionary<string, object>(0, StringComparer.Ordinal);

        private static readonly IReadOnlyList<object> EmptyPositionalPayload = Array.Empty<object>();

        private RpcMappedPayload(
            RpcMappedPayloadKind kind,
            IReadOnlyDictionary<string, object> namedValues,
            IReadOnlyList<object> positionalValues)
        {
            Kind = kind;
            NamedValues = namedValues ?? EmptyNamedPayload;
            PositionalValues = positionalValues ?? EmptyPositionalPayload;
        }

        public RpcMappedPayloadKind Kind { get; }
        public IReadOnlyDictionary<string, object> NamedValues { get; }
        public IReadOnlyList<object> PositionalValues { get; }

        public static RpcMappedPayload Named(IDictionary<string, object> values)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));

            if (values.Count == 0)
                return new RpcMappedPayload(RpcMappedPayloadKind.Named, EmptyNamedPayload, EmptyPositionalPayload);

            var copy = new Dictionary<string, object>(values.Count, StringComparer.Ordinal);
            foreach (var pair in values)
                copy[pair.Key] = pair.Value;

            return new RpcMappedPayload(RpcMappedPayloadKind.Named, copy, EmptyPositionalPayload);
        }

        public static RpcMappedPayload Positional(IList<object> values)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));

            if (values.Count == 0)
                return new RpcMappedPayload(RpcMappedPayloadKind.Positional, EmptyNamedPayload, EmptyPositionalPayload);

            var copy = new object[values.Count];
            for (var i = 0; i < values.Count; i++)
                copy[i] = values[i];

            return new RpcMappedPayload(RpcMappedPayloadKind.Positional, EmptyNamedPayload, copy);
        }

        public object ToSerializableObject()
        {
            if (Kind == RpcMappedPayloadKind.Named)
            {
                if (NamedValues.Count == 0)
                    return new Dictionary<string, object>(0, StringComparer.Ordinal);

                var copy = new Dictionary<string, object>(NamedValues.Count, StringComparer.Ordinal);
                foreach (var pair in NamedValues)
                    copy[pair.Key] = pair.Value;

                return copy;
            }

            if (PositionalValues.Count == 0)
                return Array.Empty<object>();

            var copyArray = new object[PositionalValues.Count];
            for (var i = 0; i < PositionalValues.Count; i++)
                copyArray[i] = PositionalValues[i];

            return copyArray;
        }
    }
}
