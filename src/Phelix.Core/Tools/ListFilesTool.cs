using Microsoft.Extensions.AI;

namespace Phelix.Core.Tools;

/// <summary>
/// Lists files matching a glob pattern within the root directory.
/// </summary>
/// <remarks>
/// Supports <c>**</c> for recursive matching via <see cref="SearchOption.AllDirectories"/>.
/// Results are sorted lexicographically and capped at <c>max_results</c>.
/// Directories named in <see cref="ExcludedDirectories"/> are never included in results.
/// </remarks>
public class ListFilesTool : ITool
{
    const int DefaultMaxResults = 200;

    static readonly IReadOnlySet<string> DefaultExcludedDirectories =
        new HashSet<string>(StringComparer.Ordinal) { ".git", "bin", "obj" };

    /// <summary>The directory all glob searches are rooted at.</summary>
    public string RootDirectory { get; }

    /// <summary>Directory segment names excluded from all results.</summary>
    public IReadOnlySet<string> ExcludedDirectories { get; }

    /// <inheritdoc/>
    public string Name => "list_files";

    /// <inheritdoc/>
    public string Description => "Lists files matching a glob pattern relative to the root directory. Use ** for recursive matching (e.g. src/**/*.cs). Prefer scoped patterns over bare * to avoid broad results. .git, bin, and obj directories are always excluded. Returns one path per line, sorted, and capped at max_results.";

    /// <param name="rootDirectory">
    /// Absolute path to root all searches against.
    /// Defaults to <see cref="Directory.GetCurrentDirectory"/> when <c>null</c>.
    /// </param>
    /// <param name="excludedDirectories">
    /// Directory segment names to exclude from results.
    /// Defaults to <c>{ ".git", "bin", "obj" }</c> when <c>null</c>.
    /// Pass an empty set to disable all exclusions.
    /// </param>
    public ListFilesTool(string? rootDirectory = null, IReadOnlySet<string>? excludedDirectories = null)
    {
        RootDirectory = Path.GetFullPath(rootDirectory ?? Directory.GetCurrentDirectory());
        ExcludedDirectories = excludedDirectories ?? DefaultExcludedDirectories;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Expects a required parameter <c>pattern</c> (string) and optional <c>max_results</c> (int).
    /// Returns one absolute path per line on success. Appends a truncation notice if more matches
    /// exist than <c>max_results</c>. Returns a descriptive error string on invalid input.
    /// </remarks>
    public Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        if (!parameters.TryGetValue("pattern", out object? rawPattern) || rawPattern is null)
            return Task.FromResult("Error: required parameter 'pattern' is missing.");

        string pattern = rawPattern.ToString()!;

        int maxResults = DefaultMaxResults;
        if (parameters.TryGetValue("max_results", out object? rawMax) && rawMax is not null)
        {
            if (!int.TryParse(rawMax.ToString(), out int parsed) || parsed < 1)
                return Task.FromResult($"Error: max_results must be a positive integer, got '{rawMax}'.");
            maxResults = parsed;
        }

        string[] matches;
        try
        {
            matches = ResolveGlob(RootDirectory, pattern, ExcludedDirectories);
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult($"Error: invalid pattern '{pattern}': {ex.Message}");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error: could not list files: {ex.Message}");
        }

        Array.Sort(matches, StringComparer.Ordinal);

        int total = matches.Length;

        if (total == 0)
            return Task.FromResult("(no files matched)");

        System.Text.StringBuilder sb = new();
        int limit = Math.Min(total, maxResults);
        for (int i = 0; i < limit; i++)
        {
            sb.AppendLine(matches[i]);
        }

        if (total > maxResults)
            sb.Append($"... (truncated, {total} total matches — narrow the pattern)");

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    /// <inheritdoc/>
    public AITool ToAITool() =>
        AIFunctionFactory.Create(
            (string pattern, int? max_results, CancellationToken ct) =>
            {
                Dictionary<string, object?> parameters = new()
                {
                    ["pattern"] = pattern,
                    ["max_results"] = max_results?.ToString()
                };
                return ExecuteAsync(parameters, ct);
            },
            Name,
            Description);

    // Directory.GetFiles does not support ** globs. Extract the file name pattern from
    // the last segment and search AllDirectories, then filter by path prefix for
    // patterns like "src/**/*.cs".
    static string[] ResolveGlob(string root, string glob, IReadOnlySet<string> excludedDirectories)
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

        string[] all = Directory.GetFiles(searchRoot, filePattern, SearchOption.AllDirectories);

        if (excludedDirectories.Count == 0)
            return all;

        return Array.FindAll(all, path => !HasExcludedSegment(path, excludedDirectories));
    }

    static bool HasExcludedSegment(string path, IReadOnlySet<string> excludedDirectories)
    {
        ReadOnlySpan<char> span = path.AsSpan();
        int start = 0;
        for (int i = 0; i <= span.Length; i++)
        {
            if (i == span.Length || span[i] == Path.DirectorySeparatorChar || span[i] == Path.AltDirectorySeparatorChar)
            {
                ReadOnlySpan<char> segment = span[start..i];
                if (segment.Length > 0 && excludedDirectories.Contains(segment.ToString()))
                    return true;
                start = i + 1;
            }
        }
        return false;
    }
}
