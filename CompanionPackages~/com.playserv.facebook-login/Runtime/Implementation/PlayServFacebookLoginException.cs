using System;

namespace Playserv.FacebookLogin
{
    public sealed class PlayServFacebookLoginException : Exception
    {
        public PlayServFacebookLoginException(string operation, string sourceCode, string message, Exception innerException = null)
            : base(string.IsNullOrWhiteSpace(message)
                ? $"Facebook Limited Login failed during {operation}."
                : $"Facebook Limited Login failed during {operation}: {message}", innerException)
        {
            Operation = operation ?? string.Empty;
            SourceCode = sourceCode ?? string.Empty;
        }

        public string Operation { get; }

        public string SourceCode { get; }
    }
}
