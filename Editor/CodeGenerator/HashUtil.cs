using System.Security.Cryptography;
using System.Text;

namespace Playserv.CodeGenerator.Editor
{
    internal static class HashUtil
    {
        public static string Sha256Hex(string input)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input ?? "");
            var hash = sha.ComputeHash(bytes);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var t in hash)
                sb.Append(t.ToString("x2"));

            return sb.ToString();
        }
    }
}