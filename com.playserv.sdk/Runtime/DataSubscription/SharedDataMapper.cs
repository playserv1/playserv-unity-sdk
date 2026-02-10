using System;
using System.Linq.Expressions;
using Newtonsoft.Json;

namespace Playserv.DataSubscription
{
    internal static class SharedDataMapper
    {
        private static readonly JsonSerializerSettings Settings = new()
        {
            // Adjust if you need custom converters / enum handling
            DateParseHandling = DateParseHandling.DateTime,
            NullValueHandling = NullValueHandling.Include,
            MissingMemberHandling = MissingMemberHandling.Ignore,
        };

        public static TResult Map<TResult>(LambdaExpression selector, object raw)
        {
            if (raw == null)
                return default;

            // If raw is already a string, treat it as JSON; otherwise serialize it
            var jsonString = raw as string ?? JsonConvert.SerializeObject(raw, Settings);

            // No selector => deserialize directly to TResult
            if (selector == null)
                return JsonConvert.DeserializeObject<TResult>(jsonString, Settings);

            var sourceType = selector.Parameters[0].Type;
            var source = JsonConvert.DeserializeObject(jsonString, sourceType, Settings);

            if (source == null)
                return default;

            var compiled = selector.Compile();
            var result = compiled.DynamicInvoke(source);

            if (result == null)
                return default;

            // If result is already TResult (or compatible), return directly
            if (result is TResult typed)
                return typed;

            // Otherwise, try convert (covers value types like int/float) 
            return (TResult)Convert.ChangeType(result, typeof(TResult));
        }
    }
}