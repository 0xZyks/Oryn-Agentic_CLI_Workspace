namespace Oryn.App;

public sealed record StartupOptions(string ProjectRoot, bool ShowHelp, bool ShowVersion,
    bool Check, bool UseColor, bool UseUnicode, string? ConfigPath)
{
    public static StartupOptions Parse(IReadOnlyList<string> args, string workingDirectory)
    {
        string? path = null;
        string? config = null;
        bool help = false, version = false, check = false, color = true, unicode = true;
        bool positionalOnly = false;
        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            if (!positionalOnly && arg == "--") { positionalOnly = true; continue; }
            if (!positionalOnly)
            {
                switch (arg)
                {
                    case "-h": case "--help": help = true; continue;
                    case "--version": version = true; continue;
                    case "--check": check = true; continue;
                    case "--no-color": color = false; continue;
                    case "--ascii": unicode = false; continue;
                    case "--config":
                        if (++i >= args.Count) throw new ArgumentException("--config requires a JSON file path.");
                        config = Path.GetFullPath(args[i], workingDirectory);
                        continue;
                }
                if (arg.StartsWith('-')) throw new ArgumentException($"Unknown option: {arg}");
            }
            if (path is not null) throw new ArgumentException("Specify at most one project directory.");
            path = arg;
        }
        // Resolve against the caller's working directory, never the executable location.
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path ?? ".", workingDirectory));
        return new(root, help, version, check, color, unicode, config);
    }
}
