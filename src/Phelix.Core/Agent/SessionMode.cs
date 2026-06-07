namespace Phelix.Core.Agent;

/// <summary>
/// Controls how aggressively the session gates tool calls for approval.
/// Set once at startup via <see cref="AgentOptions.Mode"/> and passed to
/// <c>PhelixHost</c> when building the <see cref="IApprovalGate"/>.
/// </summary>
/// <remarks>
/// The mode does not change what tools are available — it changes how much
/// friction stands between the model and execution. Choose based on how much
/// you trust the task and the model for the current session.
/// </remarks>
public enum SessionMode
{
    /// <summary>
    /// Default interactive mode. <see cref="ApprovalTier.Auto"/> tools run freely;
    /// <see cref="ApprovalTier.Prompt"/> tools pause for <c>y/N</c> confirmation;
    /// <see cref="ApprovalTier.Confirm"/> tools require an explicit <c>yes</c>.
    /// </summary>
    Default,

    /// <summary>
    /// Edits-accepted mode. <see cref="ApprovalTier.Auto"/> and
    /// <see cref="ApprovalTier.Prompt"/> tools both run freely — the developer has
    /// accepted that the model may write files without interruption.
    /// <see cref="ApprovalTier.Confirm"/> tools (shell execution) still require
    /// explicit confirmation.
    /// </summary>
    AcceptsEdits,

    /// <summary>
    /// Full-trust mode. All tool calls execute without interruption. A warning is
    /// printed at session start. Use when you need the agent to work autonomously
    /// and accept the responsibility for the consequences.
    /// </summary>
    AllowAll,
}
