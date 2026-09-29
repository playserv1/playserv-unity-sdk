using System;
using Playserv.ModelGenerator.Editor;
using System.Threading.Tasks;

public static class SchemaLoader
{
    [Obsolete("V1 server-schema download has been retired. Generate models from a local JSON schema or use Schema Tool.")]
    public static Task<bool> LoadSchema(string token) => Task.FromException<bool>(
        new NotSupportedException("V1 server-schema download has been retired. Generate models from a local JSON schema or use Schema Tool."));

    public static void CheckNewSchema() => SchemaCodeGenerator.CheckNewVersionJsonSchema();
}
