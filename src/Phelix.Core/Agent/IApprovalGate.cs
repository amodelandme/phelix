namespace Phelix.Core.Agent;

/// <summary>
/// Decides whether a pending tool call may proceed, given its <see cref="ApprovalTier"/>.
/// </summary>
/// <remarks>
/// The gate is the single point where <see cref="SessionMode"/> policy is enforced.
/// <see cref="AgentLoop"/> calls <see cref="RequestApprovalAsync"/> before every tool
/// dispatch and aborts the call when the result is <c>false</c>.
///
/// Two concrete implementations ship with the harness:
/// <list type="bullet">
///   <item><see cref="AutoApproveGate"/> — always returns <c>true</c>; used for <see cref="SessionMode.AllowAll"/>.</item>
///   <item><see cref="InteractiveApprovalGate"/> — prompts the terminal; used for <see cref="SessionMode.Default"/> and <see cref="SessionMode.AcceptsEdits"/>.</item>
/// </list>
///
/// Tests inject a custom <see cref="IApprovalGate"/> to exercise approval logic without
/// a real terminal.
/// </remarks>
public interface IApprovalGate
{
    /// <summary>
    /// Requests approval to execute a tool call.
    /// </summary>
    /// <param name="toolName">The name of the tool about to be called.</param>
    /// <param name="tier">The tier declared by the tool.</param>
    /// <param name="callSummary">
    /// A short human-readable description of the call (e.g. the file path or command).
    /// Displayed in the approval prompt so the user knows what they are approving.
    /// </param>
    /// <param name="args">
    /// The resolved arguments for the call. Text-only gates may ignore this;
    /// the TUI gate uses it to render a structured argument grid in the approval panel.
    /// </param>
    /// <param name="cancellationToken">Propagates cancellation from the agent loop.</param>
    /// <returns><c>true</c> if the call may proceed; <c>false</c> to deny it.</returns>
    Task<bool> RequestApprovalAsync(
        string toolName,
        ApprovalTier tier,
        string callSummary,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken cancellationToken);
}
