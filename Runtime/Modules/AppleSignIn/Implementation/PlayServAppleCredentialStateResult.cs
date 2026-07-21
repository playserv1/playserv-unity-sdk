namespace Playserv.AppleSignIn
{
    public sealed class PlayServAppleCredentialStateResult
    {
        internal PlayServAppleCredentialStateResult(
            PlayServAppleCredentialState state,
            int errorCode,
            string errorMessage)
        {
            State = state;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public PlayServAppleCredentialState State { get; }

        public int ErrorCode { get; }

        public string ErrorMessage { get; }

        public bool HasError => ErrorCode != 0 || !string.IsNullOrWhiteSpace(ErrorMessage);
    }
}
