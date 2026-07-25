namespace PlayServ.Schema.Tool;

internal sealed class CliOptions
{
    public string Command { get; private set; } = "status";
    public string ProjectRoot { get; private set; } = Directory.GetCurrentDirectory();
    public string ChangedPath { get; private set; } = string.Empty;
    public bool Json { get; private set; }
    public bool Check { get; private set; }
    public bool Help { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
        var commandSet = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index] ?? string.Empty;
            switch (argument)
            {
                case "--project":
                    options.ProjectRoot = RequireValue(args, ref index, argument);
                    break;
                case "--changed":
                    options.ChangedPath = RequireValue(args, ref index, argument);
                    break;
                case "--json":
                case "--format=json":
                    options.Json = true;
                    break;
                case "--check":
                case "--locked":
                    options.Check = true;
                    break;
                case "--help":
                case "-h":
                    options.Help = true;
                    break;
                case "--version":
                    options.Command = "version";
                    commandSet = true;
                    break;
                default:
                    if (argument.StartsWith("-", StringComparison.Ordinal))
                        throw new ArgumentException($"Unknown option: {argument}");
                    if (commandSet)
                        throw new ArgumentException($"Unexpected argument: {argument}");

                    options.Command = argument.Trim().ToLowerInvariant();
                    commandSet = true;
                    break;
            }
        }

        options.ProjectRoot = Path.GetFullPath(options.ProjectRoot);
        return options;
    }

    private static string RequireValue(
        IReadOnlyList<string> args,
        ref int index,
        string option)
    {
        index++;
        if (index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
            throw new ArgumentException($"{option} requires a value.");

        return args[index];
    }
}
