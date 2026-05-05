#if !PLAYSERV_DISABLE_DATA && !PLAYSERV_DISABLE_EVENTS
using System;
using System.Linq.Expressions;
using Playserv.Serialization;

namespace Playserv.DataSubscription
{
    internal static class SharedDataMapper
    {
        private static readonly JsonCodecOptions CodecOptions = new JsonCodecOptions
        {
            ParseDates = true,
            IncludeNullValues = true,
            IgnoreMissingMembers = true
        };

        public static TResult Map<TResult>(LambdaExpression selector, object raw, IJsonCodec jsonCodec)
        {
            if (jsonCodec == null)
                throw new ArgumentNullException(nameof(jsonCodec));

            if (raw == null)
                return default;

            var jsonString = raw as string ?? jsonCodec.Serialize(raw, CodecOptions);

            if (selector == null)
                return jsonCodec.Deserialize<TResult>(jsonString, CodecOptions);

            var sourceType = selector.Parameters[0].Type;
            var source = jsonCodec.Deserialize(jsonString, sourceType, CodecOptions);

            if (source == null)
                return default;

            var compiled = selector.Compile();
            var result = compiled.DynamicInvoke(source);

            if (result == null)
                return default;

            if (result is TResult typed)
                return typed;

            return (TResult)Convert.ChangeType(result, typeof(TResult));
        }
    }
}

#endif
