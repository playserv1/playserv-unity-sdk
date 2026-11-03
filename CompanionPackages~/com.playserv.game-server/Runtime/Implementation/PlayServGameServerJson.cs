using System;
using Playserv.Serialization;

namespace Playserv.GameServer
{
    internal static class PlayServGameServerJson
    {
        private static readonly IJsonCodec Codec = new NewtonsoftJsonCodec();

        internal static string Serialize(object value)
        {
            return Codec.Serialize(value);
        }

        internal static T Deserialize<T>(string json)
        {
            return Codec.Deserialize<T>(json);
        }

        internal static string SerializeOptionalObject(object value, string parameterName)
        {
            if (value == null)
                return null;

            string json;
            try
            {
                json = Codec.Serialize(value);
            }
            catch (Exception ex)
            {
                throw new ArgumentException("The value must be serializable as a JSON object.", parameterName, ex);
            }

            var trimmed = json == null ? string.Empty : json.Trim();
            if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[trimmed.Length - 1] != '}')
                throw new ArgumentException("The value must serialize as a JSON object.", parameterName);

            return trimmed;
        }

        internal static object ParseOptionalObject(string json)
        {
            return string.IsNullOrWhiteSpace(json) ? null : Codec.ParseToPlainValue(json);
        }
    }
}
