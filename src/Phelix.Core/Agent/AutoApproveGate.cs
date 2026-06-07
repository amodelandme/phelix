namespace Phelix.Core.Agent;

/// <summary>
/// An <see cref="IApprovalGate"/> that approves every tool call without prompting.
/// </summary>
/// <remarks>
/// Used when <see cref="SessionMode.AllowAll"/> is active. The caller (<c>PhelixHost</c>)
/// is responsible for printing the allow-all warning at session start before this gate
/// is put into service — this class has no knowledge of the session mode that selected it.
/// </remarks>
public sealed class AutoApproveGate : IApprovalGate
{
    /// <inheritdoc/>
    /// <remarks>Always returns <c>true</c>. The parameters are accepted but not evaluated.</remarks>
    public Task<bool> RequestApprovalAsync(
        string toolName,
        ApprovalTier tier,
        string callSummary,
        CancellationToken cancellationToken) => Task.FromResult(true);
}
