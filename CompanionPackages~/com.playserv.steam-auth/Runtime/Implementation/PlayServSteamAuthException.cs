using System;

namespace Playserv.SteamAuth
{
    public sealed class PlayServSteamAuthException : Exception
    {
        public PlayServSteamAuthException(string operation, string sourceCode, string message, Exception innerException = null)
            : base(string.IsNullOrWhiteSpace(message)
                ? $"Steam authentication failed during {operation}."
                : $"Steam authentication failed during {operation}: {message}", innerException)
        {
            Operation = operation ?? string.Empty;
            SourceCode = sourceCode ?? string.Empty;
        }

        public string Operation { get; }

        public string SourceCode { get; }
    }
}
