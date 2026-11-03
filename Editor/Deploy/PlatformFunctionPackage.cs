using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Playserv.Editor
{
    internal sealed class PlatformFunctionPackage
    {
        private const long MaximumBytes = 240L * 1024 * 1024;
        private readonly string _folder;
        private readonly SortedDictionary<string, byte[]> _hashes;
        public string[] Files => _hashes.Keys.ToArray();
        public string SuggestedKind { get; }

        private PlatformFunctionPackage(string folder, SortedDictionary<string, byte[]> hashes, string kind)
        { _folder = folder; _hashes = hashes; SuggestedKind = kind; }

        public static PlatformFunctionPackage Preview(string folder)
        {
            var root = Path.GetFullPath(folder);
            var files = ReadFiles(root);
            if (!files.Keys.Any(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Select a function folder containing C# source files.");
            var kinds = new HashSet<string>();
            foreach (var pair in files.Where(p => p.Key.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
            {
                var syntax = CSharpSyntaxTree.ParseText(Encoding.UTF8.GetString(pair.Value)).GetRoot();
                foreach (var type in syntax.DescendantNodes().OfType<BaseTypeSyntax>())
                {
                    var name = type.Type.GetLastToken().ValueText;
                    if (name == "IPlatformFunction") kinds.Add("cloud_function");
                    if (name == "PlatformGameServer") kinds.Add("game_server");
                }
            }
            var hashes = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            using (var sha = SHA256.Create())
                foreach (var file in files) hashes.Add(file.Key, sha.ComputeHash(file.Value));
            return new PlatformFunctionPackage(root, hashes, kinds.Count == 1 ? kinds.Single() : null);
        }

        public byte[] BuildArchive()
        {
            var files = ReadFiles(_folder);
            if (!files.Keys.SequenceEqual(_hashes.Keys)) throw Changed();
            using (var sha = SHA256.Create())
                foreach (var file in files)
                    if (!sha.ComputeHash(file.Value).SequenceEqual(_hashes[file.Key])) throw Changed();
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true))
                {
                    var index = 0;
                    foreach (var file in files)
                    {
                        var name = file.Key;
                        if (Encoding.UTF8.GetByteCount(name) > 100 || name.Any(c => c > 127))
                        {
                            var value = "path=" + name + "\n";
                            var length = Encoding.UTF8.GetByteCount(value) + 2;
                            while (length != Encoding.UTF8.GetByteCount(value) + length.ToString().Length + 1)
                                length = Encoding.UTF8.GetByteCount(value) + length.ToString().Length + 1;
                            WriteEntry(gzip, "PaxHeaders/" + index, Encoding.UTF8.GetBytes(length + " " + value), (byte)'x');
                            name = "file-" + index;
                        }
                        WriteEntry(gzip, name, file.Value, (byte)'0');
                        index++;
                    }
                    gzip.Write(new byte[1024], 0, 1024);
                }
                return output.ToArray();
            }
        }

        private static InvalidOperationException Changed() => new InvalidOperationException("Source files changed since preview. Preview the package again before deploying.");

        private static SortedDictionary<string, byte[]> ReadFiles(string root)
        {
            if (!Directory.Exists(root)) throw new InvalidOperationException("The function folder does not exist.");
            for (var ancestor = new DirectoryInfo(root); ancestor != null; ancestor = ancestor.Parent)
                RejectLink(ancestor.FullName);
            var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            long total = 0;
            Collect(root, "", files, ref total);
            return files;
        }

        private static void Collect(string root, string relative, SortedDictionary<string, byte[]> files, ref long total)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(Path.Combine(root, relative)))
            {
                RejectLink(path);
                var name = Path.GetFileName(path);
                if (name.Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;
                if (Directory.Exists(path))
                {
                    if (name.Equals("bin", StringComparison.OrdinalIgnoreCase) || name.Equals("obj", StringComparison.OrdinalIgnoreCase) || name.Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;
                    Collect(root, Path.Combine(relative, name), files, ref total);
                }
                else
                {
                    if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                    var key = Path.Combine(relative, name).Replace('\\', '/');
                    if (key.Any(char.IsControl)) throw new InvalidOperationException("Source filenames cannot contain control characters.");
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        total += stream.Length;
                        if (total > MaximumBytes || files.Count >= 10000) throw new InvalidOperationException("Function source exceeds the package limit (240 MiB or 10,000 files).");
                        using (var content = new MemoryStream()) { stream.CopyTo(content); files.Add(key, content.ToArray()); }
                    }
                }
            }
        }

        private static void RejectLink(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Symbolic links and junctions are not supported in function source: " + path);
        }

        private static void WriteEntry(Stream stream, string name, byte[] content, byte type)
        {
            var header = new byte[512];
            WriteText(header, 0, name);
            WriteOctal(header, 100, 8, 420);
            WriteOctal(header, 108, 8, 0); WriteOctal(header, 116, 8, 0);
            WriteOctal(header, 124, 12, content.LongLength); WriteOctal(header, 136, 12, 0);
            for (var i = 148; i < 156; i++) header[i] = 32;
            header[156] = type;
            WriteText(header, 257, "ustar"); WriteText(header, 263, "00");
            WriteOctal(header, 148, 7, header.Sum(b => (int)b)); header[155] = 32;
            stream.Write(header, 0, header.Length);
            stream.Write(content, 0, content.Length);
            var padding = (512 - content.Length % 512) % 512;
            stream.Write(new byte[padding], 0, padding);
        }

        private static void WriteText(byte[] bytes, int offset, string value)
        { var text = Encoding.ASCII.GetBytes(value); Buffer.BlockCopy(text, 0, bytes, offset, text.Length); }
        private static void WriteOctal(byte[] bytes, int offset, int width, long value)
        { WriteText(bytes, offset, Convert.ToString(value, 8).PadLeft(width - 1, '0')); }
    }
}
