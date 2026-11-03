using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Playserv.Editor.Migration
{
    internal static class PlayServApiMigrationEngine
    {
        private const string WrapperNamespace = "Playserv.Wrapper";
        private const string LegacyTypeName = "PlayServ";

        private static readonly Dictionary<string, PlayServApiMigrationRule> Rules =
            BuildRules();

        internal static PlayServApiMigrationScanResult ScanProject(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
                throw new ArgumentException("Project root is required.", nameof(projectRoot));

            projectRoot = Path.GetFullPath(projectRoot);
            var assetsRoot = Path.Combine(projectRoot, "Assets");
            var result = new PlayServApiMigrationScanResult(projectRoot);
            if (!Directory.Exists(assetsRoot))
            {
                result.Issues.Add(new PlayServApiMigrationIssue(
                    "Assets",
                    "Project Assets directory was not found."));
                return result;
            }

            string[] sourcePaths;
            try
            {
                sourcePaths = Directory.GetFiles(
                        assetsRoot,
                        "*.cs",
                        SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception exception)
            {
                result.Issues.Add(new PlayServApiMigrationIssue(
                    "Assets",
                    "Could not enumerate project scripts: " + exception.Message));
                return result;
            }

            for (var i = 0; i < sourcePaths.Length; i++)
            {
                var absolutePath = Path.GetFullPath(sourcePaths[i]);
                var assetPath = ToAssetPath(projectRoot, absolutePath);
                if (ShouldSkip(assetPath))
                    continue;

                result.ScannedFileCount++;
                try
                {
                    var document = PlayServMigrationSourceDocument.Load(absolutePath);
                    var tree = CSharpSyntaxTree.ParseText(
                        document.Text,
                        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest));
                    var syntaxError = tree.GetDiagnostics()
                        .FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
                    if (syntaxError != null)
                    {
                        result.Issues.Add(new PlayServApiMigrationIssue(
                            assetPath,
                            "Skipped because the script contains a syntax error: " +
                            syntaxError.GetMessage()));
                        continue;
                    }

                    var changes = AnalyzeTree(assetPath, document.Text, tree);
                    if (changes.Count == 0)
                        continue;

                    result.Files.Add(new PlayServApiMigrationFilePlan(
                        assetPath,
                        absolutePath,
                        document.Hash,
                        changes));
                }
                catch (Exception exception)
                {
                    result.Issues.Add(new PlayServApiMigrationIssue(
                        assetPath,
                        "Could not scan script: " + exception.Message));
                }
            }

            return result;
        }

        internal static IReadOnlyList<PlayServApiMigrationChange> AnalyzeSource(
            string assetPath,
            string source)
        {
            source = source ?? string.Empty;
            var tree = CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest));
            return AnalyzeTree(assetPath ?? string.Empty, source, tree);
        }

        internal static PlayServApiMigrationApplyResult Apply(
            PlayServApiMigrationScanResult scan,
            DateTime timestampUtc)
        {
            if (scan == null)
                throw new ArgumentNullException(nameof(scan));

            var result = new PlayServApiMigrationApplyResult(timestampUtc);
            var selectedFiles = scan.Files
                .Where(file => file.Changes.Any(change => change.IsSelected))
                .ToArray();
            result.SelectedChangeCount = selectedFiles.Sum(
                file => file.Changes.Count(change => change.IsSelected));

            var operationId = timestampUtc.ToString("yyyyMMdd-HHmmss-fff") +
                              "-" +
                              Guid.NewGuid().ToString("N").Substring(0, 8);
            result.BackupRoot = Path.Combine(
                scan.ProjectRoot,
                "Library",
                "PlayServ",
                "ApiMigrationBackups",
                operationId);
            result.ReportPath = Path.Combine(
                scan.ProjectRoot,
                "Library",
                "PlayServ",
                "Reports",
                "PlayServApiMigrationReport.md");

            for (var i = 0; i < selectedFiles.Length; i++)
                ApplyFile(scan.ProjectRoot, selectedFiles[i], result);

            WriteReport(scan, result);
            return result;
        }

        private static IReadOnlyList<PlayServApiMigrationChange> AnalyzeTree(
            string assetPath,
            string source,
            SyntaxTree tree)
        {
            var root = tree.GetCompilationUnitRoot();
            var aliases = FindLegacyAliases(root);
            var canUseSimpleLegacyName =
                HasWrapperNamespaceImport(root) &&
                !HasConflictingPlayServDeclaration(root);
            var changes = new List<PlayServApiMigrationChange>();

            foreach (var memberAccess in root.DescendantNodes()
                         .OfType<MemberAccessExpressionSyntax>())
            {
                var memberName = memberAccess.Name.Identifier.ValueText;
                if (!Rules.TryGetValue(memberName, out var rule) ||
                    !IsLegacyPlayServExpression(
                        memberAccess.Expression,
                        aliases,
                        canUseSimpleLegacyName))
                {
                    continue;
                }

                var replacement = BuildReplacement(memberAccess, rule, aliases);
                var lineSpan = tree.GetLineSpan(memberAccess.Span);
                changes.Add(new PlayServApiMigrationChange(
                    assetPath,
                    lineSpan.StartLinePosition.Line + 1,
                    lineSpan.StartLinePosition.Character + 1,
                    memberAccess.SpanStart,
                    memberAccess.Span.Length,
                    source.Substring(memberAccess.SpanStart, memberAccess.Span.Length),
                    replacement.ToString(),
                    rule.ModuleApi));
            }

            return changes;
        }

        private static HashSet<string> FindLegacyAliases(CompilationUnitSyntax root)
        {
            var aliases = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < root.Usings.Count; i++)
            {
                var directive = root.Usings[i];
                if (directive.Alias == null || directive.Name == null)
                    continue;

                if (!string.Equals(
                        NormalizeQualifiedName(directive.Name.ToString()),
                        WrapperNamespace + "." + LegacyTypeName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                aliases.Add(directive.Alias.Name.Identifier.ValueText);
            }

            return aliases;
        }

        private static bool HasWrapperNamespaceImport(CompilationUnitSyntax root)
        {
            for (var i = 0; i < root.Usings.Count; i++)
            {
                var directive = root.Usings[i];
                if (directive.Alias != null || directive.Name == null)
                    continue;

                if (string.Equals(
                        NormalizeQualifiedName(directive.Name.ToString()),
                        WrapperNamespace,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasConflictingPlayServDeclaration(CompilationUnitSyntax root)
        {
            for (var i = 0; i < root.Usings.Count; i++)
            {
                var directive = root.Usings[i];
                if (directive.Alias == null ||
                    !string.Equals(
                        directive.Alias.Name.Identifier.ValueText,
                        LegacyTypeName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (directive.Name == null ||
                    !string.Equals(
                        NormalizeQualifiedName(directive.Name.ToString()),
                        WrapperNamespace + "." + LegacyTypeName,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            if (root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
                .Any(declaration => IsLegacyName(declaration.Identifier)))
            {
                return true;
            }

            if (root.DescendantNodes().OfType<DelegateDeclarationSyntax>()
                .Any(declaration => IsLegacyName(declaration.Identifier)))
            {
                return true;
            }

            if (root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
                .Any(declaration => IsLegacyName(declaration.Identifier)))
            {
                return true;
            }

            if (root.DescendantNodes().OfType<ParameterSyntax>()
                .Any(declaration => IsLegacyName(declaration.Identifier)))
            {
                return true;
            }

            if (root.DescendantNodes().OfType<PropertyDeclarationSyntax>()
                .Any(declaration => IsLegacyName(declaration.Identifier)))
            {
                return true;
            }

            if (root.DescendantNodes().OfType<EventDeclarationSyntax>()
                .Any(declaration => IsLegacyName(declaration.Identifier)))
            {
                return true;
            }

            if (root.DescendantNodes().OfType<TypeParameterSyntax>()
                .Any(declaration => IsLegacyName(declaration.Identifier)))
            {
                return true;
            }

            return root.DescendantNodes().OfType<ForEachStatementSyntax>()
                .Any(declaration => IsLegacyName(declaration.Identifier));
        }

        private static bool IsLegacyName(SyntaxToken identifier)
        {
            return string.Equals(
                identifier.ValueText,
                LegacyTypeName,
                StringComparison.Ordinal);
        }

        private static bool IsLegacyPlayServExpression(
            ExpressionSyntax expression,
            ISet<string> aliases,
            bool canUseSimpleLegacyName)
        {
            if (expression is IdentifierNameSyntax identifier)
            {
                var name = identifier.Identifier.ValueText;
                if (aliases.Contains(name))
                    return true;

                return canUseSimpleLegacyName &&
                       string.Equals(name, LegacyTypeName, StringComparison.Ordinal);
            }

            return string.Equals(
                NormalizeQualifiedName(expression.ToString()),
                WrapperNamespace + "." + LegacyTypeName,
                StringComparison.Ordinal);
        }

        private static MemberAccessExpressionSyntax BuildReplacement(
            MemberAccessExpressionSyntax memberAccess,
            PlayServApiMigrationRule rule,
            ISet<string> aliases)
        {
            var targetExpression = BuildTargetExpression(
                memberAccess.Expression,
                rule.ModuleApi,
                aliases);
            SimpleNameSyntax targetMember = memberAccess.Name;
            if (!string.Equals(
                    memberAccess.Name.Identifier.ValueText,
                    rule.TargetMember,
                    StringComparison.Ordinal))
            {
                targetMember = SyntaxFactory.IdentifierName(rule.TargetMember)
                    .WithTriviaFrom(memberAccess.Name);
            }

            return memberAccess
                .WithExpression(targetExpression)
                .WithName(targetMember);
        }

        private static ExpressionSyntax BuildTargetExpression(
            ExpressionSyntax legacyExpression,
            string moduleApi,
            ISet<string> aliases)
        {
            if (legacyExpression is IdentifierNameSyntax identifier)
            {
                if (aliases.Contains(identifier.Identifier.ValueText))
                {
                    return SyntaxFactory.ParseExpression(
                            "global::" + WrapperNamespace + "." + moduleApi)
                        .WithTriviaFrom(legacyExpression);
                }

                return SyntaxFactory.IdentifierName(moduleApi)
                    .WithTriviaFrom(legacyExpression);
            }

            var lastToken = legacyExpression.GetLastToken();
            var replacementToken = SyntaxFactory.Identifier(
                lastToken.LeadingTrivia,
                moduleApi,
                lastToken.TrailingTrivia);
            return legacyExpression.ReplaceToken(lastToken, replacementToken);
        }

        private static void ApplyFile(
            string projectRoot,
            PlayServApiMigrationFilePlan file,
            PlayServApiMigrationApplyResult result)
        {
            var selectedChanges = file.Changes
                .Where(change => change.IsSelected)
                .OrderByDescending(change => change.Start)
                .ToArray();
            var fileResult = new PlayServApiMigrationFileResult(
                file.AssetPath,
                selectedChanges.Length);
            result.Files.Add(fileResult);

            PlayServMigrationSourceDocument document;
            try
            {
                document = PlayServMigrationSourceDocument.Load(file.AbsolutePath);
            }
            catch (Exception exception)
            {
                fileResult.Error = "Could not read file: " + exception.Message;
                return;
            }

            if (!string.Equals(document.Hash, file.SourceHash, StringComparison.Ordinal))
            {
                fileResult.Error = "File changed after preview. Scan the project again.";
                return;
            }

            var migrated = document.Text;
            for (var i = 0; i < selectedChanges.Length; i++)
            {
                var change = selectedChanges[i];
                if (change.Start < 0 ||
                    change.Length < 0 ||
                    change.Start + change.Length > migrated.Length ||
                    !string.Equals(
                        migrated.Substring(change.Start, change.Length),
                        change.OriginalExpression,
                        StringComparison.Ordinal))
                {
                    fileResult.Error =
                        "Preview no longer matches the source. Scan the project again.";
                    return;
                }

                migrated = migrated.Remove(change.Start, change.Length)
                    .Insert(change.Start, change.ReplacementExpression);
            }

            try
            {
                var backupPath = Path.Combine(
                    result.BackupRoot,
                    file.AssetPath.Replace('/', Path.DirectorySeparatorChar));
                var backupDirectory = Path.GetDirectoryName(backupPath);
                if (!string.IsNullOrEmpty(backupDirectory))
                    Directory.CreateDirectory(backupDirectory);
                File.Copy(file.AbsolutePath, backupPath, overwrite: true);

                document.Write(migrated);
                fileResult.BackupPath = backupPath;
                fileResult.AppliedChangeCount = selectedChanges.Length;
                fileResult.Changes.AddRange(selectedChanges);
                result.AppliedChangeCount += selectedChanges.Length;
                result.UpdatedFileCount++;
            }
            catch (Exception exception)
            {
                fileResult.Error = "Could not update file: " + exception.Message;
            }
        }

        private static void WriteReport(
            PlayServApiMigrationScanResult scan,
            PlayServApiMigrationApplyResult result)
        {
            try
            {
                var reportDirectory = Path.GetDirectoryName(result.ReportPath);
                if (!string.IsNullOrEmpty(reportDirectory))
                    Directory.CreateDirectory(reportDirectory);
                File.WriteAllText(
                    result.ReportPath,
                    BuildReport(scan, result),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception exception)
            {
                result.ReportError = exception.Message;
            }
        }

        internal static string BuildReport(
            PlayServApiMigrationScanResult scan,
            PlayServApiMigrationApplyResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# PlayServ API Migration Report");
            sb.AppendLine();
            sb.Append("- Generated: ")
                .Append(result.TimestampUtc.ToString("u"))
                .AppendLine();
            sb.Append("- Scripts scanned: ")
                .Append(scan.ScannedFileCount)
                .AppendLine();
            sb.Append("- Changes selected: ")
                .Append(result.SelectedChangeCount)
                .AppendLine();
            sb.Append("- Changes applied: ")
                .Append(result.AppliedChangeCount)
                .AppendLine();
            sb.Append("- Files updated: ")
                .Append(result.UpdatedFileCount)
                .AppendLine();
            sb.Append("- Files skipped: ")
                .Append(result.Files.Count(file => !string.IsNullOrEmpty(file.Error)))
                .AppendLine();
            sb.AppendLine();

            if (!string.IsNullOrEmpty(result.BackupRoot))
            {
                sb.Append("Backups: `")
                    .Append(result.BackupRoot.Replace('\\', '/'))
                    .AppendLine("`");
                sb.AppendLine();
            }

            sb.AppendLine("## Files");
            sb.AppendLine();
            if (result.Files.Count == 0)
            {
                sb.AppendLine("No files were selected.");
            }
            else
            {
                for (var i = 0; i < result.Files.Count; i++)
                {
                    var file = result.Files[i];
                    sb.Append("### `")
                        .Append(file.AssetPath)
                        .AppendLine("`");
                    sb.AppendLine();
                    if (string.IsNullOrEmpty(file.Error))
                    {
                        sb.Append("- Applied changes: ")
                            .Append(file.AppliedChangeCount)
                            .AppendLine();
                        sb.Append("- Backup: `")
                            .Append((file.BackupPath ?? string.Empty).Replace('\\', '/'))
                            .AppendLine("`");
                        for (var changeIndex = 0;
                             changeIndex < file.Changes.Count;
                             changeIndex++)
                        {
                            var change = file.Changes[changeIndex];
                            sb.Append("- Line ")
                                .Append(change.Line)
                                .Append(": `")
                                .Append(change.OriginalExpression)
                                .Append("` -> `")
                                .Append(change.ReplacementExpression)
                                .AppendLine("`");
                        }
                    }
                    else
                    {
                        sb.Append("- Status: skipped")
                            .AppendLine();
                        sb.Append("- Reason: ")
                            .AppendLine(file.Error);
                    }

                    sb.AppendLine();
                }
            }

            if (scan.Issues.Count > 0)
            {
                sb.AppendLine("## Scan Issues");
                sb.AppendLine();
                for (var i = 0; i < scan.Issues.Count; i++)
                {
                    sb.Append("- `")
                        .Append(scan.Issues[i].AssetPath)
                        .Append("`: ")
                        .AppendLine(scan.Issues[i].Message);
                }
            }

            return sb.ToString();
        }

        private static bool ShouldSkip(string assetPath)
        {
            var normalized = (assetPath ?? string.Empty).Replace('\\', '/');
            if (normalized.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
                return true;

            if (normalized.StartsWith(
                    "Assets/PlayServ/Generated/",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(
                    "Assets/Shared/Generated/",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return normalized.StartsWith(
                       "Assets/playserv-unity-sdk/",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.IndexOf(
                       "/playserv-unity-sdk/",
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ToAssetPath(string projectRoot, string absolutePath)
        {
            var normalizedRoot = Path.GetFullPath(projectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedPath = Path.GetFullPath(absolutePath);
            var relative = normalizedPath.Substring(normalizedRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return relative.Replace('\\', '/');
        }

        private static string NormalizeQualifiedName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                if (!char.IsWhiteSpace(value[i]))
                    sb.Append(value[i]);
            }

            return sb.ToString().Replace("global::", string.Empty);
        }

        private static Dictionary<string, PlayServApiMigrationRule> BuildRules()
        {
            var rules = new[]
            {
                Rule("SelectEntity", "PlayServData"),
                Rule("GetDataByKeyAsync", "PlayServData"),
                Rule("StartDataByKeyPolling", "PlayServData"),

                Rule("Subscribe", "PlayServEvents"),
                Rule("SubscribeRaw", "PlayServEvents"),
                Rule("Publish", "PlayServEvents"),
                Rule("PublishForGroup", "PlayServEvents"),
                Rule("PublishForUser", "PlayServEvents"),
                Rule("SubscribeGroupAsync", "PlayServEvents"),
                Rule("UnsubscribeGroupAsync", "PlayServEvents"),

                Rule("OnRpcInvokeResponse", "PlayServRpc"),
                Rule("Send", "PlayServRpc"),
                Rule("Invoke", "PlayServRpc"),
                Rule("InvokeArgs", "PlayServRpc"),
                Rule("InvokeNamed", "PlayServRpc"),

                Rule("SetCommandHandler", "PlayServServerRpc"),
                Rule("SetEventHandler", "PlayServServerRpc"),
                Rule("SetRpcInvoker", "PlayServServerRpc"),

                Rule("Spawn", "PlayServSpawn"),
                Rule("CurrentSpawnScope", "PlayServSpawn", "CurrentScope"),
                Rule("JoinSpawnScopeAsync", "PlayServSpawn"),
                Rule("JoinSpawnScope", "PlayServSpawn"),
                Rule("LeaveSpawnScopeAsync", "PlayServSpawn"),
                Rule("LeaveSpawnScope", "PlayServSpawn"),
                Rule("Despawn", "PlayServSpawn"),
                Rule("SetPrefabRegistry", "PlayServSpawn")
            };

            return rules.ToDictionary(rule => rule.LegacyMember, StringComparer.Ordinal);
        }

        private static PlayServApiMigrationRule Rule(
            string legacyMember,
            string moduleApi,
            string targetMember = null)
        {
            return new PlayServApiMigrationRule(
                legacyMember,
                moduleApi,
                targetMember ?? legacyMember);
        }
    }

    internal sealed class PlayServApiMigrationScanResult
    {
        public PlayServApiMigrationScanResult(string projectRoot)
        {
            ProjectRoot = projectRoot;
        }

        public string ProjectRoot { get; }
        public int ScannedFileCount { get; set; }
        public List<PlayServApiMigrationFilePlan> Files { get; } =
            new List<PlayServApiMigrationFilePlan>();
        public List<PlayServApiMigrationIssue> Issues { get; } =
            new List<PlayServApiMigrationIssue>();

        public int ChangeCount => Files.Sum(file => file.Changes.Count);
        public int SelectedChangeCount => Files.Sum(
            file => file.Changes.Count(change => change.IsSelected));
    }

    internal sealed class PlayServApiMigrationFilePlan
    {
        public PlayServApiMigrationFilePlan(
            string assetPath,
            string absolutePath,
            string sourceHash,
            IReadOnlyList<PlayServApiMigrationChange> changes)
        {
            AssetPath = assetPath;
            AbsolutePath = absolutePath;
            SourceHash = sourceHash;
            Changes = changes;
        }

        public string AssetPath { get; }
        public string AbsolutePath { get; }
        public string SourceHash { get; }
        public IReadOnlyList<PlayServApiMigrationChange> Changes { get; }
    }

    internal sealed class PlayServApiMigrationChange
    {
        public PlayServApiMigrationChange(
            string assetPath,
            int line,
            int column,
            int start,
            int length,
            string originalExpression,
            string replacementExpression,
            string moduleApi)
        {
            AssetPath = assetPath;
            Line = line;
            Column = column;
            Start = start;
            Length = length;
            OriginalExpression = originalExpression;
            ReplacementExpression = replacementExpression;
            ModuleApi = moduleApi;
            IsSelected = true;
        }

        public string AssetPath { get; }
        public int Line { get; }
        public int Column { get; }
        public int Start { get; }
        public int Length { get; }
        public string OriginalExpression { get; }
        public string ReplacementExpression { get; }
        public string ModuleApi { get; }
        public bool IsSelected { get; set; }
    }

    internal sealed class PlayServApiMigrationIssue
    {
        public PlayServApiMigrationIssue(string assetPath, string message)
        {
            AssetPath = assetPath;
            Message = message;
        }

        public string AssetPath { get; }
        public string Message { get; }
    }

    internal sealed class PlayServApiMigrationApplyResult
    {
        public PlayServApiMigrationApplyResult(DateTime timestampUtc)
        {
            TimestampUtc = timestampUtc;
        }

        public DateTime TimestampUtc { get; }
        public int SelectedChangeCount { get; set; }
        public int AppliedChangeCount { get; set; }
        public int UpdatedFileCount { get; set; }
        public string BackupRoot { get; set; }
        public string ReportPath { get; set; }
        public string ReportError { get; set; }
        public List<PlayServApiMigrationFileResult> Files { get; } =
            new List<PlayServApiMigrationFileResult>();

        public bool HasErrors =>
            !string.IsNullOrEmpty(ReportError) ||
            Files.Any(file => !string.IsNullOrEmpty(file.Error));
    }

    internal sealed class PlayServApiMigrationFileResult
    {
        public PlayServApiMigrationFileResult(string assetPath, int selectedChangeCount)
        {
            AssetPath = assetPath;
            SelectedChangeCount = selectedChangeCount;
        }

        public string AssetPath { get; }
        public int SelectedChangeCount { get; }
        public int AppliedChangeCount { get; set; }
        public string BackupPath { get; set; }
        public string Error { get; set; }
        public List<PlayServApiMigrationChange> Changes { get; } =
            new List<PlayServApiMigrationChange>();
    }

    internal sealed class PlayServApiMigrationRule
    {
        public PlayServApiMigrationRule(
            string legacyMember,
            string moduleApi,
            string targetMember)
        {
            LegacyMember = legacyMember;
            ModuleApi = moduleApi;
            TargetMember = targetMember;
        }

        public string LegacyMember { get; }
        public string ModuleApi { get; }
        public string TargetMember { get; }
    }

    internal sealed class PlayServMigrationSourceDocument
    {
        private readonly string _path;
        private readonly Encoding _encoding;
        private readonly bool _emitPreamble;

        private PlayServMigrationSourceDocument(
            string path,
            string text,
            string hash,
            Encoding encoding,
            bool emitPreamble)
        {
            _path = path;
            Text = text;
            Hash = hash;
            _encoding = encoding;
            _emitPreamble = emitPreamble;
        }

        public string Text { get; }
        public string Hash { get; }

        public static PlayServMigrationSourceDocument Load(string path)
        {
            var bytes = File.ReadAllBytes(path);
            DetectEncoding(bytes, out var encoding, out var preambleLength);
            var text = encoding.GetString(
                bytes,
                preambleLength,
                bytes.Length - preambleLength);
            return new PlayServMigrationSourceDocument(
                path,
                text,
                ComputeHash(bytes),
                encoding,
                preambleLength > 0);
        }

        public void Write(string text)
        {
            var content = _encoding.GetBytes(text ?? string.Empty);
            if (!_emitPreamble)
            {
                File.WriteAllBytes(_path, content);
                return;
            }

            var preamble = _encoding.GetPreamble();
            var bytes = new byte[preamble.Length + content.Length];
            Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
            Buffer.BlockCopy(content, 0, bytes, preamble.Length, content.Length);
            File.WriteAllBytes(_path, bytes);
        }

        private static void DetectEncoding(
            byte[] bytes,
            out Encoding encoding,
            out int preambleLength)
        {
            if (bytes.Length >= 3 &&
                bytes[0] == 0xEF &&
                bytes[1] == 0xBB &&
                bytes[2] == 0xBF)
            {
                encoding = new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: true,
                    throwOnInvalidBytes: true);
                preambleLength = 3;
                return;
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                encoding = Encoding.Unicode;
                preambleLength = 2;
                return;
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                encoding = Encoding.BigEndianUnicode;
                preambleLength = 2;
                return;
            }

            encoding = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true);
            preambleLength = 0;
        }

        private static string ComputeHash(byte[] bytes)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                for (var i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
