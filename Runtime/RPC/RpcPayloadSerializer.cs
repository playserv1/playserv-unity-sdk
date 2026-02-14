using System;
using System.Text;
using Newtonsoft.Json;

#nullable enable

namespace Playserv.RPC
{
    /// <summary>
    /// Helper for converting RPC payload objects to/from base64 JSON format.
    /// </summary>
    public static class RpcPayloadSerializer
    {
        /// <summary>
        /// Serializes arbitrary object to JSON and encodes it as base64.
        /// </summary>
        /// <param name="payload">Payload object. Can be null.</param>
        /// <returns>Base64-encoded UTF8 JSON string.</returns>
        public static string SerializeToBase64(object? payload)
        {
            var json = JsonConvert.SerializeObject(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            return Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// Decodes base64 payload to UTF8 JSON string.
        /// </summary>
        /// <param name="payloadBase64">Base64 payload string.</param>
        /// <returns>Decoded JSON string.</returns>
        public static string DecodeToJson(string payloadBase64)
        {
            if (string.IsNullOrWhiteSpace(payloadBase64))
                throw new ArgumentException("Payload base64 cannot be null or empty.", nameof(payloadBase64));

            var bytes = Convert.FromBase64String(payloadBase64);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}

#nullable restore
