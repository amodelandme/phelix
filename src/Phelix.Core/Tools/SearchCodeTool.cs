using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Phelix.Core.Tools;

/// <summary>
/// Searches file contents for a literal string or .NET regular expression.
/// </summary>
/// <remarks>
/// Walks files in <see cref="RootDirectory"/> matching an optional file glob,
/// reads each file line by line, and returns matching lines with their file
/// path and line number. Binary files are skipped silently.
/// </remarks>
public class SearchCodeTool : ITool
{
    const int DefaultMaxResults = 50;

    /// <summary>The directory all searches are rooted at.</summary>
    public string RootDirectory { get; }

    /// <inheritdoc/>
    public string Name => "search_code";

    /// <inheritdoc/>
    public string Description => "Searches file contents for a literal string or .NET regex. Returns matching lines in the format 'path:line_number: content'. Use file_glob to restrict which files are searched (e.g. **/*.cs).";

    /// <param name="rootDirectory">
    /// Absolute path to root all searches against.
    /// Defaults to <see cref="Directory.GetCurrentDirectory"/> when <c>null</c>.
    /// </param>
    public SearchCodeTool(string? rootDirectory = null) =>
        RootDirectory = Path.GetFullPath(rootDirectory ?? Directory.GetCurrentDirectory());

    /// <inheritdoc/>
    /// <remarks>
    /// Expects a required parameter <c>pattern</c> (string) and optional parameters
    /// <c>file_glob</c> (string), <c>is_regex</c> (bool), and <c>max_results</c> (int).
    /// Returns one match per line. Appends a truncation notice when capped.
    /// Returns a descriptive error string on regex compilation failure or invalid input.
    /// </remarks>
    public async Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        if (!parameters.TryGetValue("pattern", out object? rawPattern) || rawPattern is null)
            return "Error: required parameter 'pattern' is missing.";

        string pattern = rawPattern.ToString()!;

        string fileGlob = "**/*";
        if (parameters.TryGetValue("file_glob", out object? rawGlob) && rawGlob is not null)
            fileGlob = rawGlob.ToString()!;

        bool isRegex = false;
        if (parameters.TryGetValue("is_regex", out object? rawIsRegex) && rawIsRegex is not null)
            isRegex = rawIsRegex.ToString()!.Equals("true", StringComparison.OrdinalIgnoreCase);

        int maxResults = DefaultMaxResults;
        if (parameters.TryGetValue("max_results", out object? rawMax) && rawMax is not null)
        {
            if (!int.TryParse(rawMax.ToString(), out int parsed) || parsed < 1)
                return $"Error: max_results must be a positive integer, got '{rawMax}'.";
            maxResults = parsed;
        }

        Regex? regex = null;
        if (isRegex)
        {
            try
            {
                regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
            }
            catch (ArgumentException ex)
            {
                return $"Error: invalid regex '{pattern}': {ex.Message}";
            }
        }

        string[] files;
        try
        {
            files = ResolveGlob(RootDirectory, fileGlob);
        }
        catch (ArgumentException ex)
        {
            return $"Error: invalid file_glob '{fileGlob}': {ex.Message}";
        }

        Array.Sort(files, StringComparer.Ordinal);

        System.Text.StringBuilder sb = new();
        int matchCount = 0;
        bool truncated = false;

        foreach (string filePath in files)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            string[] lines;
            try
            {
                lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
            }
            catch
            {
                continue;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                bool matched = isRegex
                    ? regex!.IsMatch(line)
                    : line.Contains(pattern, StringComparison.Ordinal);

                if (!matched)
                    continue;

                if (matchCount >= maxResults)
                {
                    truncated = true;
                    break;
                }

                sb.AppendLine($"{filePath}:{i + 1}: {line}");
                matchCount++;
            }

            if (truncated)
                break;
        }

        if (matchCount == 0)
            return "(no matches found)";

        if (truncated)
            sb.Append("... (truncated — narrow the pattern or file_glob)");

        return sb.ToString().TrimEnd();
    }

    /// <inheritdoc/>
    public AITool ToAITool() =>
        AIFunctionFactory.Create(
            (string pattern, string? file_glob, bool? is_regex, int? max_results, CancellationToken ct) =>
            {
                Dictionary<string, object?> parameters = new()
                {
                    ["pattern"] = pattern,
                    ["file_glob"] = file_glob,
                    ["is_regex"] = is_regex?.ToString(),
                    ["max_results"] = max_results?.ToString()
                };
                return ExecuteAsync(parameters, ct);
            },
            Name,
            Description);

    // Directory.GetFiles does not support ** globs. Extract the file name pattern from
    // the last segment and search AllDirectories, then filter by path prefix for
    // patterns like "src/**/*.cs".
    static string[] ResolveGlob(string root, string glob)
    {
        string normalizedGlob = glob.Replace('\\', '/');
        int lastSlash = normalizedGlob.LastIndexOf('/');

        string filePattern = lastSlash < 0 ? normalizedGlob : normalizedGlob[(lastSlash + 1)..];
        string dirPrefix = lastSlash < 0 ? string.Empty : normalizedGlob[..lastSlash].Replace("**", string.Empty).Trim('/');

        string searchRoot = string.IsNullOrEmpty(dirPrefix)
            ? root
            : Path.Combine(root, dirPrefix);

        if (!Directory.Exists(searchRoot))
            return [];

        return Directory.GetFiles(searchRoot, filePattern, SearchOption.AllDirectories);
    }
}
