using System;

namespace Playserv.AppleSignIn
{
    [Flags]
    public enum PlayServAppleSignInScope
    {
        None = 0,
        Email = 1,
        FullName = 2
    }
}
