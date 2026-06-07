using Microsoft.Extensions.AI;
using Phelix.Core.Agent;

namespace Phelix.Core.Tools;

/// <summary>
/// A tool the agent can invoke. The model identifies the tool by <see cref="Name"/> and
/// supplies arguments as a loosely-typed dictionary matching the tool's expected parameters.
/// </summary>
public interface ITool
{
    /// <summary>The name the model uses to call this tool. Must be unique within a <c>ToolRegistry</c>.</summary>
    string Name { get; }

    /// <summary>Natural-language description passed to the model so it knows when to call this tool.</summary>
    string Description { get; }

    /// <summary>
    /// The level of user approval required before this tool may execute.
    /// Consulted by <see cref="IApprovalGate"/> at dispatch time.
    /// </summary>
    ApprovalTier ApprovalTier { get; }

    /// <summary>
    /// Executes the tool with the arguments the model supplied.
    /// </summary>
    /// <param name="parameters">
    /// Key/value pairs extracted from the model's tool-call response.
    /// Keys are parameter names; values are deserialized from JSON and may be <c>null</c>.
    /// </param>
    /// <param name="cancellationToken">Propagates cancellation from the agent loop.</param>
    /// <returns>A string result the agent loop feeds back to the model as a tool result message.</returns>
    Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken);

    /// <summary>
    /// Returns an <see cref="AITool"/> that describes this tool to the model.
    /// The returned instance carries the correct parameter schema and delegates invocation
    /// back through <see cref="ExecuteAsync"/> so the agent loop can dispatch uniformly.
    /// </summary>
    AITool ToAITool();
}
