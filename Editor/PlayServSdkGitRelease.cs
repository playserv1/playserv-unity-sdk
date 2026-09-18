using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Playserv.Serialization;

namespace Playserv.Editor
{
    internal static class PlayServSdkGitRelease
    {
        public static PlayServPackageRelease Parse(string json, string editorVersion)
        {
            var value = new NewtonsoftJsonCodec().ParseToPlainValue(json) as IDictionary<string, object>;
            if (value == null) throw new InvalidOperationException("Invalid package manifest.");
            string Text(string name) => value.TryGetValue(name, out var field) && field is string text ? text : null;
            var nameValue = Text("name");
            var version = Text("version");
            if (string.IsNullOrEmpty(nameValue) || string.IsNullOrEmpty(version))
                throw new InvalidOperationException("Incomplete package manifest.");
            var unity = Text("unity");
            var release = Text("unityRelease");
            if ((value.ContainsKey("unity") && unity == null) || (value.ContainsKey("unityRelease") && release == null))
                throw new InvalidOperationException("Invalid Unity compatibility metadata.");
            var compatible = string.IsNullOrEmpty(unity) && string.IsNullOrEmpty(release) ||
                IsCompatible(editorVersion, unity + (string.IsNullOrEmpty(release) ? "" : "." + release));
            IDictionary<string, object> dependencies = null;
            if (value.TryGetValue("dependencies", out var dependencyValue))
            {
                dependencies = dependencyValue as IDictionary<string, object>;
                if (dependencies == null || dependencies.Values.Any(v => !(v is string)))
                    throw new InvalidOperationException("Invalid package dependencies.");
            }
            return new PlayServPackageRelease
            {
                Name = nameValue, Version = version,
                CompatibleVersions = compatible ? new[] { version } : Array.Empty<string>(),
                DependencyNames = dependencies?.Keys.ToArray() ?? Array.Empty<string>()
            };
        }

        private static bool IsCompatible(string editor, string minimum)
        {
            int[] ParseVersion(string text)
            {
                var match = Regex.Match(text ?? "", @"^(\d+)\.(\d+)(?:\.(\d+)([abfp])(\d+))?$");
                if (!match.Success) return null;
                var numbers = new int[5];
                var groups = new[] { 1, 2, 3, 5 };
                for (var i = 0; i < groups.Length; i++)
                {
                    var part = match.Groups[groups[i]].Value;
                    if (part.Length > 0 && !int.TryParse(part, out numbers[i == 3 ? 4 : i])) return null;
                }
                numbers[3] = match.Groups[4].Success ? "abfp".IndexOf(match.Groups[4].Value, StringComparison.Ordinal) : 0;
                return numbers;
            }
            var current = ParseVersion(editor);
            var floor = ParseVersion(minimum);
            if (current == null || floor == null) return false;
            for (var i = 0; i < current.Length; i++)
                if (current[i] != floor[i]) return current[i] > floor[i];
            return true;
        }
    }
}
