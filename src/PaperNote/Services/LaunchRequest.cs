namespace PaperNote.Services;

public sealed record LaunchRequest(string Action, IReadOnlyList<string> Files)
{
    public const int MaximumArguments = 64;
    public const int MaximumMessageLength = 65536;
    public static readonly string[] Extensions = [".md", ".markdown", ".mdown", ".mkd", ".txt"];
    public static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static bool TryParse(string[] args, out LaunchRequest request)
    {
        request = new("--show", []);
        if (args.Length > MaximumArguments || args.Any(a => a is null || a.Length > 8192 || a.Contains('\0'))) return false;
        var action = "--show";
        var files = new List<string>();
        var pathsOnly = false;
        foreach (var value in args)
        {
            if (!pathsOnly && value == "--") { pathsOnly = true; continue; }
            if (!pathsOnly && value is "--new-note" or "--library" or "--settings" or "--login" or "--show" or "--markdown" or "--open")
            { action = value; continue; }
            if (!pathsOnly && value.StartsWith('-')) return false;
            if (!TryGetPath(value, out var path)) return false;
            files.Add(path);
        }
        if (files.Count >= MaximumArguments || action == "--open" && files.Count == 0) return false;
        request = new(action, files.Distinct(PathComparer).ToArray());
        return true;
    }

    public static bool TryGetPath(string value, out string path)
    {
        path = "";
        try
        {
            if (string.IsNullOrWhiteSpace(value) || value.Contains('\0')) return false;
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !Path.IsPathRooted(value))
            {
                if (!uri.IsFile || !uri.IsLoopback) return false;
                value = uri.LocalPath;
            }
            path = Path.GetFullPath(value);
            return Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    // Normalize relative paths in the sending process, before forwarding to a
    // running instance whose working directory can be completely different.
    public string[] ToArguments() => Files.Count > 0 ? ["--open", .. Files] : [Action];
}
