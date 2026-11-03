using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Playserv.Editor
{
    internal static class DockerExecutable
    {
        internal static string Resolve()
        {
            var windows = Path.DirectorySeparatorChar == '\\';
            return Resolve(Environment.GetEnvironmentVariable("PATH"),
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                windows, File.Exists, path => windows || access(path, 1) == 0);
        }

        internal static string Resolve(string searchPath, bool mac, string home, bool windows,
            Func<string, bool> exists, Func<string, bool> executable)
        {
            var candidates = new List<string>();
            foreach (var folder in (searchPath ?? "").Split(windows ? ';' : ':'))
            {
                if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathRooted(folder)) continue;
                candidates.Add(Path.Combine(folder.Trim('"'), windows ? "docker.exe" : "docker"));
            }
            if (mac)
            {
                candidates.Add("/Applications/Docker.app/Contents/Resources/bin/docker");
                if (!string.IsNullOrEmpty(home)) candidates.Add(Path.Combine(home, "Applications/Docker.app/Contents/Resources/bin/docker"));
                candidates.Add("/usr/local/bin/docker");
                candidates.Add("/opt/homebrew/bin/docker");
                if (!string.IsNullOrEmpty(home)) candidates.Add(Path.Combine(home, ".docker/bin/docker"));
            }
            var found = false;
            foreach (var candidate in candidates)
            {
                if (!exists(candidate)) continue;
                found = true;
                if (executable(candidate)) return Path.GetFullPath(candidate);
            }
            if (found) throw new UnauthorizedAccessException("Docker CLI was found but is not executable. Check its file permissions.");
            throw new FileNotFoundException("Docker CLI was not found on the Editor's PATH or in standard Docker Desktop locations. Install Docker Desktop or add Docker to PATH.");
        }

        [DllImport("libc", SetLastError = true)] private static extern int access(string path, int mode);
    }
}
