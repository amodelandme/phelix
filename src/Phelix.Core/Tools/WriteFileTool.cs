using Microsoft.Extensions.AI;

namespace Phelix.Core.Tools;

/// <summary>
/// Writes text content to a file at the given path.
/// </summary>
/// <remarks>
/// Overwrites the file if it already exists. Creates intermediate directories
/// as needed. Path traversal is prevented by the same root-confinement check
/// used by <see cref="ReadFileTool"/>.
/// </remarks>
public class WriteFileTool : ITool
{
    /// <summary>The directory outside of which file writes are refused.</summary>
    public string RootDirectory { get; }

    /// <inheritdoc/>
    public string Name => "write_file";

    /// <inheritdoc/>
    public string Description => "Writes text content to a file at the given path, creating the file and any missing directories. Overwrites the file if it already exists. The path must be within the allowed root directory.";

    /// <param name="rootDirectory">
    /// Absolute path of the directory that bounds all writes.
    /// Defaults to <see cref="Directory.GetCurrentDirectory"/> when <c>null</c>.
    /// </param>
    public WriteFileTool(string? rootDirectory = null) =>
        RootDirectory = Path.GetFullPath(rootDirectory ?? Directory.GetCurrentDirectory());

    /// <inheritdoc/>
    /// <remarks>
    /// Expects parameters named <c>path</c> (string) and <c>content</c> (string).
    /// Returns <c>"OK"</c> on success, or a descriptive error string the model can act on.
    /// </remarks>
    public async Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        if (!parameters.TryGetValue("path", out object? rawPath) || rawPath is null)
            return "Error: required parameter 'path' is missing.";

        if (!parameters.TryGetValue("content", out object? rawContent) || rawContent is null)
            return "Error: required parameter 'content' is missing.";

        string requestedPath = rawPath.ToString()!;
        string content = rawContent.ToString()!;

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

        try
        {
            string? directory = Path.GetDirectoryName(absolutePath);
            if (directory is not null)
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(absolutePath, content, cancellationToken);
            return "OK";
        }
        catch (Exception ex)
        {
            return $"Error: could not write '{absolutePath}': {ex.Message}";
        }
    }

    /// <inheritdoc/>
    public AITool ToAITool() =>
        AIFunctionFactory.Create(
            (string path, string content, CancellationToken ct) =>
                ExecuteAsync(new Dictionary<string, object?> { ["path"] = path, ["content"] = content }, ct),
            Name,
            Description);
}
