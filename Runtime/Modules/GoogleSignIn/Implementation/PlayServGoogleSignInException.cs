using System;

namespace Playserv.GoogleSignIn
{
    public sealed class PlayServGoogleSignInException : Exception
    {
        public PlayServGoogleSignInException(string operation, string message, Exception innerException = null)
            : base(string.IsNullOrWhiteSpace(message)
                ? $"Google Sign In failed during {operation}."
                : $"Google Sign In failed during {operation}: {message}", innerException)
        {
            Operation = operation;
        }

        public string Operation { get; }
    }
}
