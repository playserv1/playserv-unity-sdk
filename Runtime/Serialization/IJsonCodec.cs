using System;

namespace Playserv.Serialization
{
    public interface IJsonCodec
    {
        string Serialize(object value, JsonCodecOptions options = null);

        T Deserialize<T>(string json, JsonCodecOptions options = null);

        object Deserialize(string json, Type type, JsonCodecOptions options = null);

        T Convert<T>(object value, JsonCodecOptions options = null);

        object Convert(object value, Type type, JsonCodecOptions options = null);

        string ToCanonicalJson(object value, JsonCodecOptions options = null);

        object Clone(object value, JsonCodecOptions options = null);

        bool TryGetProperty(object value, string propertyName, bool ignoreCase, out object propertyValue);

        bool TryGetFirstPropertyValue(object value, out object propertyValue);
    }
}
