using System;

namespace Playserv.Serialization
{
    /// <summary>Optional codec capability for typed responses without JSON type coercion.</summary>
    public interface IPlayServStrictJsonCodec
    {
        /// <summary>Checks the complete response contract before any network I/O.</summary>
        void ValidateResponseType(Type responseType);

        /// <summary>Validates the JSON shape, then deserializes using the codec's normal contracts.</summary>
        T DeserializeStrict<T>(string json);
    }

    /// <summary>Payload-free strict response diagnostic. Paths identify declared fields, not values.</summary>
    public sealed class PlayServStrictResponseException : Exception
    {
        public string Path { get; }
        public string ExpectedType { get; }
        public string ActualType { get; }

        public PlayServStrictResponseException(string path, string expectedType, string actualType)
            : base($"Strict response at {path}: expected {expectedType}; actual {actualType}.")
        {
            Path = path;
            ExpectedType = expectedType;
            ActualType = actualType;
        }

        /// <summary>Never forwards an unknown codec's exception message or inner exception.</summary>
        public static PlayServStrictResponseException Sanitize(Exception error) =>
            error as PlayServStrictResponseException ??
            new PlayServStrictResponseException("$", "supported typed JSON response", "codec validation failure");
    }
}
