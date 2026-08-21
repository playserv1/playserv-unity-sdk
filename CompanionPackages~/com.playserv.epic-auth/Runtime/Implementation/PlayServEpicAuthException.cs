using System;

namespace Playserv.EpicAuth
{
    public sealed class PlayServEpicAuthException : Exception
    {
        public PlayServEpicAuthException(string operation, string sourceCode, string message, Exception innerException = null)
            : base(string.IsNullOrWhiteSpace(message)
                ? $"Epic authentication failed during {operation}."
                : $"Epic authentication failed during {operation}: {message}", innerException)
        {
            Operation = operation ?? string.Empty;
            SourceCode = sourceCode ?? string.Empty;
        }

        public string Operation { get; }

        public string SourceCode { get; }
    }
}
