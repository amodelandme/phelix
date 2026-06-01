using Microsoft.Extensions.AI;

namespace Phelix.Core.Tools;

/// <summary>
/// Reads the contents of a file and returns them as a string.
/// </summary>
/// <remarks>
/// Path traversal is prevented by resolving the requested path to an absolute path and
/// verifying it falls within <see cref="RootDirectory"/> before any I/O is performed.
/// </remarks>
public class ReadFileTool : ITool
{
    /// <summary>The directory outside of which file reads are refused.</summary>
    public string RootDirectory { get; }

    /// <inheritdoc/>
    public string Name => "read_file";

    /// <inheritdoc/>
    public string Description => "Reads the contents of a file at the given path. The path must be within the allowed root directory.";

    /// <param name="rootDirectory">
    /// Absolute path of the directory that bounds all reads.
    /// Defaults to <see cref="Directory.GetCurrentDirectory"/> when <c>null</c>.
    /// </param>
    public ReadFileTool(string? rootDirectory = null)
    {
        RootDirectory = Path.GetFullPath(rootDirectory ?? Directory.GetCurrentDirectory());
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Expects a single parameter named <c>path</c> (string). Returns the file contents on
    /// success, or a descriptive error string the model can act on if the path is invalid,
    /// outside the root, or the file cannot be read.
    /// </remarks>
    public async Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        if (!parameters.TryGetValue("path", out object? rawPath) || rawPath is null)
            return "Error: required parameter 'path' is missing.";

        string requestedPath = rawPath.ToString()!;

        string absolutePath;
        try
        {
            absolutePath = Path.GetFullPath(requestedPath);
        }
        catch (Exception ex)
        {
            return $"Error: could not resolve path '{requestedPath}': {ex.Message}";
        }

        if (!absolutePath.StartsWith(RootDirectory, StringComparison.Ordinal))
            return $"Error: path '{absolutePath}' is outside the allowed root '{RootDirectory}'.";

        if (!File.Exists(absolutePath))
            return $"Error: file not found at '{absolutePath}'.";

        try
        {
            return await File.ReadAllTextAsync(absolutePath, cancellationToken);
        }
        catch (Exception ex)
        {
            return $"Error: could not read '{absolutePath}': {ex.Message}";
        }
    }

    /// <inheritdoc/>
    public AITool ToAITool() =>
        AIFunctionFactory.Create(
            (string path, CancellationToken ct) =>
                ExecuteAsync(new Dictionary<string, object?> { ["path"] = path }, ct),
            Name,
            Description);
}
