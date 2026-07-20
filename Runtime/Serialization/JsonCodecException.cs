using System;

namespace Playserv.Serialization
{
    public sealed class JsonCodecException : Exception
    {
        public JsonCodecException(string message)
            : base(message)
        {
        }

        public JsonCodecException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
