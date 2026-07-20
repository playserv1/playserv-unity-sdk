using System.Collections.Generic;

namespace Playserv.Serialization
{
    public static class UnityJsonCodecOptionsFactory
    {
        public static JsonCodecOptions CreateDefaultEventOptions()
        {
            var converters = new List<object>();
#if UNITY_5_3_OR_NEWER && PLAYSERV_HAS_NEWTONSOFT_JSON
            converters.Add(new Vector3NewtonsoftJsonConverter());
            converters.Add(new QuaternionNewtonsoftJsonConverter());
#endif
            return new JsonCodecOptions
            {
                IncludeNullValues = true,
                IgnoreMissingMembers = true,
                ParseDates = true,
                CustomConverters = converters
            };
        }
    }
}
