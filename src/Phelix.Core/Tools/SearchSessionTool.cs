using Microsoft.Extensions.AI;
using Phelix.Core.Session;

namespace Phelix.Core.Tools;

/// <summary>
/// Searches the current session's tool call history for relevant output.
/// </summary>
/// <remarks>
/// Backed by the FTS5-indexed <c>tool_outputs</c> table in <see cref="ISessionStore"/>.
/// The tool is always registered in <see cref="ToolRegistry"/> — not only after compaction —
/// but the description guides the model to use it primarily for recall after a context
/// compaction event, when detailed tool outputs are no longer in the message list.
/// </remarks>
/// <param name="store">The session store to query.</param>
public sealed class SearchSessionTool(ISessionStore store) : ITool
{
    const int MaxSearchResults = 5;

    /// <inheritdoc/>
    public string Name => "search_session";

    /// <inheritdoc/>
    public string Description =>
        "Search the current session's tool call history for relevant output. " +
        "Use this after a context compaction to recall specific file contents, " +
        "command output, or search results from earlier in the session. " +
        "Returns up to 5 matching tool call records.";

    /// <inheritdoc/>
    /// <remarks>
    /// Expects a single parameter named <c>query</c> (string). Returns a formatted
    /// string of matching records, or a human-readable message when nothing matches.
    /// Never throws — errors are returned as strings the model can act on.
    /// </remarks>
    public async Task<string> ExecuteAsync(
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        if (!parameters.TryGetValue("query", out object? rawQuery) || rawQuery is null)
            return "Error: required parameter 'query' is missing.";

        string query = rawQuery.ToString()!;

        IReadOnlyList<ToolCallRecord> results =
            await store.SearchToolOutputsAsync(query, MaxSearchResults, cancellationToken);

        if (results.Count == 0)
            return $"No session history found matching '{query}'.";

        return FormatResults(results);
    }

    /// <inheritdoc/>
    public AITool ToAITool() =>
        AIFunctionFactory.Create(
            (string query, CancellationToken ct) =>
                ExecuteAsync(new Dictionary<string, object?> { ["query"] = query }, ct),
            Name,
            Description);

    static string FormatResults(IReadOnlyList<ToolCallRecord> results)
    {
        System.Text.StringBuilder output = new();
        output.AppendLine($"Found {results.Count} result(s):");
        output.AppendLine();

        for (int i = 0; i < results.Count; i++)
        {
            ToolCallRecord record = results[i];
            output.AppendLine($"[{i + 1}] {record.Name}({record.ArgumentsJson})");
            output.AppendLine($"    {record.Result}");
            output.AppendLine();
        }

        return output.ToString().TrimEnd();
    }
}
