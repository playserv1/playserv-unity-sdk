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

        public static Func<object, TResult> CreateCompiledMapper<TResult>(LambdaExpression selector, IJsonCodec jsonCodec)
        {
            if (jsonCodec == null)
                throw new ArgumentNullException(nameof(jsonCodec));

            if (selector == null)
                return raw => Deserialize<TResult>(raw, jsonCodec);

            var sourceType = selector.Parameters[0].Type;
            var projector = CompileProjector<TResult>(selector, sourceType);

            return raw =>
            {
                if (raw == null)
                    return default;

                var jsonString = raw as string ?? jsonCodec.Serialize(raw, CodecOptions);
                var source = jsonCodec.Deserialize(jsonString, sourceType, CodecOptions);

                if (source == null)
                    return default;

                return projector(source);
            };
        }

        private static TResult Deserialize<TResult>(object raw, IJsonCodec jsonCodec)
        {
            if (raw == null)
                return default;

            var jsonString = raw as string ?? jsonCodec.Serialize(raw, CodecOptions);
            return jsonCodec.Deserialize<TResult>(jsonString, CodecOptions);
        }

        private static Func<object, TResult> CompileProjector<TResult>(LambdaExpression selector, Type sourceType)
        {
            var source = Expression.Parameter(typeof(object), "source");
            var typedSource = Expression.Convert(source, sourceType);
            var projected = Expression.Invoke(selector, typedSource);
            var converted = Expression.Convert(projected, typeof(TResult));

            return Expression.Lambda<Func<object, TResult>>(converted, source).Compile();
        }
    }
}

#endif
