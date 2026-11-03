using Playserv.CodeGenerator;
using Playserv.Serialization;

namespace Playserv.ModelGenerator.Editor
{
    internal static class SchemaJsonReader
    {
        private static readonly IJsonCodec JsonCodec = new NewtonsoftJsonCodec();
        private static readonly JsonCodecOptions SchemaCodecOptions = new JsonCodecOptions
        {
            ParseDates = true,
            IncludeNullValues = true,
            IgnoreMissingMembers = true,
            IgnoreMetadataProperties = true
        };

        public static JsonSchemaRoot ReadRoot(string content)
        {
            return JsonCodec.Deserialize<JsonSchemaRoot>(content, SchemaCodecOptions);
        }

        public static bool IsSupportedRoot(JsonSchemaRoot root)
        {
            return root != null &&
                   root.Version > 0 &&
                   root.JsonSchema != null;
        }
    }
}
