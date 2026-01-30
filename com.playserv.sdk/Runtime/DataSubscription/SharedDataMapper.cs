using System;
using System.Linq.Expressions;
using UnityEngine;

namespace Playserv.DataSubscription
{
    internal static class SharedDataMapper
    {
        public static TResult Map<TResult>(LambdaExpression selector, object raw)
        {
            if (raw == null)
                return default(TResult);

            var jsonString = raw as string ?? JsonUtility.ToJson(raw);

            if (selector == null)
            {
                return JsonUtility.FromJson<TResult>(jsonString);
            }

            var sourceType = selector.Parameters[0].Type;
            var source = JsonUtility.FromJson(jsonString, sourceType);

            if (source == null)
                return default(TResult);

            var compiled = selector.Compile();
            var result = compiled.DynamicInvoke(source);

            return (TResult)result;
        }
    }
}
