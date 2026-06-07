namespace Phelix.Core.Agent;

/// <summary>
/// An <see cref="IApprovalGate"/> that enforces approval tiers by prompting the user
/// on <c>stdout</c> and reading their response from <c>stdin</c>.
/// </summary>
/// <remarks>
/// <para>
/// The gate's behaviour depends on the active <see cref="SessionMode"/>:
/// </para>
/// <list type="table">
///   <listheader><term>Mode</term><description>Behaviour</description></listheader>
///   <item><term><see cref="SessionMode.Default"/></term><description><see cref="ApprovalTier.Auto"/> — silent; <see cref="ApprovalTier.Prompt"/> — y/N; <see cref="ApprovalTier.Confirm"/> — must type "yes".</description></item>
///   <item><term><see cref="SessionMode.AcceptsEdits"/></term><description><see cref="ApprovalTier.Auto"/> and <see cref="ApprovalTier.Prompt"/> — silent; <see cref="ApprovalTier.Confirm"/> — must type "yes".</description></item>
/// </list>
/// <para>
/// <paramref name="input"/> and <paramref name="output"/> are injected so the gate
/// can be tested without a real terminal — pass <see cref="Console.In"/> and
/// <see cref="Console.Out"/> in production.
/// </para>
/// </remarks>
/// <param name="mode">The active session mode. Controls which tiers are auto-approved.</param>
/// <param name="input">The reader used to receive user responses.</param>
/// <param name="output">The writer used to display approval prompts.</param>
public sealed class InteractiveApprovalGate(SessionMode mode, TextReader input, TextWriter output) : IApprovalGate
{
    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="ApprovalTier.Auto"/> calls are always approved silently.
    /// <see cref="ApprovalTier.Prompt"/> calls prompt for <c>y/N</c>; anything other than
    /// <c>y</c> or <c>yes</c> (case-insensitive) is treated as a denial. In
    /// <see cref="SessionMode.AcceptsEdits"/>, <see cref="ApprovalTier.Prompt"/> is also
    /// silent.
    /// <see cref="ApprovalTier.Confirm"/> calls require the user to type <c>yes</c>
    /// in full; any other input is denied.
    /// </remarks>
    public async Task<bool> RequestApprovalAsync(
        string toolName,
        ApprovalTier tier,
        string callSummary,
        CancellationToken cancellationToken)
    {
        if (tier == ApprovalTier.Auto)
            return true;

        if (tier == ApprovalTier.Prompt && mode == SessionMode.AcceptsEdits)
            return true;

        return tier switch
        {
            ApprovalTier.Prompt   => await PromptAsync(toolName, callSummary, requireFullYes: false, cancellationToken),
            ApprovalTier.Confirm  => await PromptAsync(toolName, callSummary, requireFullYes: true,  cancellationToken),
            _                     => true,
        };
    }

    /// <summary>
    /// Writes the approval prompt and reads the user's response.
    /// </summary>
    /// <param name="toolName">The tool requesting approval.</param>
    /// <param name="callSummary">Short description of what the call will do.</param>
    /// <param name="requireFullYes">
    /// When <c>true</c>, only the exact string <c>yes</c> is accepted (Confirm tier).
    /// When <c>false</c>, <c>y</c> or <c>yes</c> is accepted (Prompt tier).
    /// </param>
    /// <param name="cancellationToken">Propagates cancellation from the agent loop.</param>
    async Task<bool> PromptAsync(
        string toolName,
        string callSummary,
        bool requireFullYes,
        CancellationToken cancellationToken)
    {
        string hint = requireFullYes ? "[type 'yes' to allow]" : "[y/N]";
        await output.WriteLineAsync($"\n  Tool: {ControlCharSanitizer.Sanitize(toolName)}");
        await output.WriteLineAsync($"  {ControlCharSanitizer.Sanitize(callSummary)}");
        await output.WriteAsync($"  Allow? {hint} ");

        string? response = await input.ReadLineAsync(cancellationToken);

        if (response is null)
            return false;

        string trimmed = response.Trim();

        return requireFullYes
            ? trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase)
            : trimmed.Equals("y",   StringComparison.OrdinalIgnoreCase)
           || trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}
