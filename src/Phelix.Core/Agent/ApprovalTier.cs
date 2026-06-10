namespace Phelix.Core.Agent;

/// <summary>
/// Declares how much user approval a tool requires before it may execute.
/// </summary>
/// <remarks>
/// Each <see cref="Tools.ITool"/> implementation declares its own tier. The
/// <see cref="IApprovalGate"/> consults this value at dispatch time and decides
/// whether to proceed, prompt, or require explicit confirmation.
///
/// Tiers are ordered by risk: <see cref="Auto"/> is safest, <see cref="Confirm"/>
/// is most dangerous. A gate operating in a more permissive <see cref="SessionMode"/>
/// may collapse higher tiers down to <see cref="Auto"/> automatically.
/// </remarks>
public enum ApprovalTier
{
    /// <summary>
    /// Executes without interruption. Safe, read-only operations:
    /// <c>read_file</c>, <c>list_files</c>, <c>search_code</c>, <c>search_session</c>.
    /// </summary>
    Auto,

    /// <summary>
    /// Pauses and asks the user before proceeding. Default-deny — pressing Enter
    /// without typing declines the call. Used for file writes (<c>write_file</c>).
    /// </summary>
    Prompt,

    /// <summary>
    /// Requires the user to type <c>yes</c> explicitly before proceeding. Used for
    /// shell execution (<c>bash</c>) where the consequences of an accidental approval
    /// are potentially irreversible.
    /// </summary>
    Confirm,
}
