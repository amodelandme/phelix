using Microsoft.Extensions.AI;

namespace Phelix.Core.Tools;

/// <summary>
/// Holds the set of tools available to the agent loop for a given session.
/// </summary>
public class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers <paramref name="tool"/> by its <see cref="ITool.Name"/>.
    /// Throws <see cref="ArgumentException"/> if a tool with the same name is already registered.
    /// </summary>
    public void Register(ITool tool)
    {
        if (!_tools.TryAdd(tool.Name, tool))
            throw new ArgumentException($"A tool named '{tool.Name}' is already registered.", nameof(tool));
    }

    /// <summary>
    /// Looks up a tool by name. Returns <c>true</c> and sets <paramref name="tool"/> if found.
    /// </summary>
    public bool TryGet(string name, out ITool? tool) => _tools.TryGetValue(name, out tool);

    /// <summary>All registered tools, in registration order.</summary>
    public IReadOnlyCollection<ITool> All => _tools.Values;

    /// <summary>
    /// Returns an <see cref="AITool"/> list suitable for passing to <see cref="ChatOptions.Tools"/>.
    /// Each entry is produced by the corresponding <see cref="ITool.ToAITool"/> implementation,
    /// which owns the correct parameter schema for that tool.
    /// </summary>
    public IList<AITool> ToAITools()
    {
        List<AITool> aiTools = new List<AITool>(_tools.Count);
        foreach (ITool tool in _tools.Values)
            aiTools.Add(tool.ToAITool());
        return aiTools;
    }
}
