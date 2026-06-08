using System.Threading.Channels;
using Phelix.Core.Agent;

namespace Phelix.Tui;

/// <summary>
/// An <see cref="IApprovalGate"/> that pauses the agent turn and waits for the user
/// to approve or deny via the TUI's keyboard loop.
/// </summary>
/// <remarks>
/// <para>
/// When <see cref="RequestApprovalAsync"/> is called, it writes an
/// <see cref="ApprovalRequested"/> event to the TUI's event channel and then awaits a
/// <see cref="TaskCompletionSource{TResult}"/> that it carries inside the event.
/// The consumer loop in <see cref="TuiSession"/> resolves that TCS when the user presses
/// <c>y</c> (approve) or <c>n</c> / <c>Escape</c> (deny).
/// </para>
/// <para>
/// The TCS is created with <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/>.
/// This ensures that when the consumer loop calls <c>TrySetResult</c>, the agent turn's
/// continuation is queued back onto the ThreadPool rather than running inline on the
/// consumer loop thread. Without this flag, pressing <c>y</c> would execute the entire
/// next model call synchronously on the consumer loop, freezing the display.
/// </para>
/// <para>
/// A <see cref="CancellationToken.Register"/> callback calls <c>TrySetCanceled</c> on
/// the TCS so a <c>Ctrl+C</c> during a pending approval unblocks the gate and propagates
/// cancellation up through <see cref="Phelix.Core.Agent.AgentLoop"/> cleanly, without
/// leaving the approval gate hanging.
/// </para>
/// </remarks>
/// <param name="eventWriter">
/// The write end of the TUI's <c>Channel&lt;TuiEvent&gt;</c>. Events are written here
/// to be consumed by <see cref="TuiSession"/>'s consumer loop.
/// </param>
public sealed class TuiApprovalGate(ChannelWriter<TuiEvent> eventWriter) : IApprovalGate
{
    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="ApprovalTier.Auto"/> calls return <c>true</c> immediately without
    /// posting to the channel. All other tiers post an <see cref="ApprovalRequested"/>
    /// event and await user input via the <see cref="TaskCompletionSource{TResult}"/>.
    /// </remarks>
    public async Task<bool> RequestApprovalAsync(
        string toolName,
        ApprovalTier tier,
        string callSummary,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken cancellationToken)
    {
        if (tier == ApprovalTier.Auto)
            return true;

        TaskCompletionSource<bool> gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        cancellationToken.Register(() => gate.TrySetCanceled(cancellationToken));

        await eventWriter.WriteAsync(
            new ApprovalRequested(toolName, callSummary, args, gate),
            cancellationToken);

        return await gate.Task;
    }
}
