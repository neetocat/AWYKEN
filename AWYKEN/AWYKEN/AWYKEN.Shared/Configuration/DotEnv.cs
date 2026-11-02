namespace AWYKEN.Shared.Configuration;

/// <summary>
/// Minimal <c>.env</c> reader. It keeps secrets such as the Azure SQL password on the
/// machine instead of in source control, and feeds both the MAUI app and the Web host.
/// </summary>
public static class DotEnv
{
    public const string FileName = ".env";

    // Build output folders sit several levels below the repository root, so walk up
    // generously. Stops as soon as a .env file is found.
    private const int MaxParentHops = 8;

    /// <summary>
    /// Finds the first <c>.env</c> file reachable from the extra roots, the app base
    /// directory, or the current working directory (and their parent folders).
    /// Returns <c>null</c> when no file exists.
    /// </summary>
    public static string? FindFile(params string[]? additionalRoots)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in EnumerateRoots(additionalRoots))
        {
            var directory = root;
            for (var hop = 0; directory is not null && hop <= MaxParentHops; hop++)
            {
                if (!visited.Add(directory))
                    break;

                var candidate = Path.Combine(directory, FileName);
                if (File.Exists(candidate))
                    return candidate;

                directory = Path.GetDirectoryName(directory);
            }
        }

        return null;
    }

    /// <summary>Parses <c>.env</c> text into key/value pairs. Later keys win.</summary>
    public static IReadOnlyDictionary<string, string> Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Parse(content.Split('\n'));
    }

    /// <summary>Parses <c>.env</c> lines into key/value pairs. Later keys win.</summary>
    public static IReadOnlyDictionary<string, string> Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in lines)
        {
            // Tolerate a UTF-8 BOM, Windows line endings, comments, and "export KEY=value".
            var line = rawLine.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line[0] == '#')
                continue;

            if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
                line = line[7..].TrimStart();

            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = line[..separator].Trim();
            if (key.Length == 0)
                continue;

            values[key] = Unquote(line[(separator + 1)..].Trim());
        }

        return values;
    }

    /// <summary>
    /// Reads the first .env file found. Returns an empty map when no file exists or it
    /// cannot be read, so a missing file never masks the caller's own error message.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Load(out string? path, params string[]? additionalRoots)
    {
        path = FindFile(additionalRoots);
        if (path is null)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            return Parse(File.ReadAllLines(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            path = null;
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }

    private static IEnumerable<string> EnumerateRoots(string[]? additionalRoots)
    {
        if (additionalRoots is not null)
        {
            foreach (var root in additionalRoots)
            {
                if (TryNormalize(root) is { } directory)
                    yield return directory;
            }
        }

        if (TryNormalize(AppContext.BaseDirectory) is { } baseDirectory)
            yield return baseDirectory;

        if (TryNormalize(Directory.GetCurrentDirectory()) is { } workingDirectory)
            yield return workingDirectory;
    }

    private static string? TryNormalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return null;

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
