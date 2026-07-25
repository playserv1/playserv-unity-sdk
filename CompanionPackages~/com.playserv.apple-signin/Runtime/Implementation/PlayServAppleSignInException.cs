using System;

namespace Playserv.AppleSignIn
{
    public sealed class PlayServAppleSignInException : Exception
    {
        public PlayServAppleSignInException(string operation, int code, string message)
            : base(string.IsNullOrWhiteSpace(message)
                ? $"Apple Sign In failed during {operation}."
                : $"Apple Sign In failed during {operation}: {message}")
        {
            Operation = operation ?? string.Empty;
            Code = code;
            AppleMessage = message ?? string.Empty;
        }

        public string Operation { get; }

        public int Code { get; }

        public string AppleMessage { get; }
    }
}
