using Playserv.Proxy.Common;
using Playserv.Wrapper;

namespace Playserv.ModelGenerator.Editor
{
    public class SchemaLoader
    {
        public static void LoadSchema()
        {
            
            SchemaRequest request = new SchemaRequest
            {
                RequestId = 1,
                Query = new[] { "tanks-dz32" }
            };
            PlayServ.Send(request, "module_schema");
        }
    }
}