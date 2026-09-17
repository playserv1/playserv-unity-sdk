namespace Playserv.Runtime.Abstractions
{
    /// <summary>Shared cosmetic-name policy; never rejects a sign-in for length.</summary>
    internal static class PlayServDisplayNamePolicy
    {
        internal static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var name = value.Trim();
            if (name.Length > 64)
            {
                var length = char.IsHighSurrogate(name[63]) ? 63 : 64;
                name = name.Substring(0, length).TrimEnd();
            }
            return name.Length == 0 ? null : name;
        }
    }
}
