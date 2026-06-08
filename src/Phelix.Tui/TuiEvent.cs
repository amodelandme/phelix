using Phelix.Core.Agent;
using Phelix.Core.Session;

namespace Phelix.Tui;

/// <summary>
/// A discriminated union of every event that can flow through the TUI's event channel.
/// </summary>
/// <remarks>
/// Two producers write events into the <c>Channel&lt;TuiEvent&gt;</c>:
/// <list type="bullet">
///   <item>The agent — via <see cref="Phelix.Core.Agent.TurnCallbacks"/> delegates wired up in <see cref="TuiSession"/>.</item>
///   <item>The keyboard loop — which wraps each <see cref="ConsoleKeyInfo"/> in a <see cref="KeyPressed"/> record.</item>
/// </list>
/// A single consumer loop in <see cref="TuiSession"/> reads from the channel and applies
/// each event to <see cref="TuiState"/> via <see cref="TuiState.Apply"/>. All state mutation
/// is serialized through this channel — no locks required.
/// </remarks>
public abstract record TuiEvent;

/// <summary>A streamed text fragment arrived from the model response.</summary>
/// <param name="Text">The text fragment. Never null or empty — <see cref="TuiSession"/> filters those out.</param>
public sealed record ChunkReceived(string Text) : TuiEvent;

/// <summary>The agent is about to execute a tool call.</summary>
/// <param name="Name">The tool's registered name (e.g. <c>read_file</c>).</param>
/// <param name="Args">The resolved arguments the model passed to the tool.</param>
public sealed record ToolStarted(
    string Name,
    IReadOnlyDictionary<string, object?> Args) : TuiEvent;

/// <summary>A tool call has finished executing.</summary>
/// <param name="Name">The tool's registered name.</param>
/// <param name="Status">Whether the call succeeded, was denied, or failed.</param>
/// <param name="Duration">Wall-clock time the call took. <see cref="TimeSpan.Zero"/> for denied and failed calls.</param>
public sealed record ToolCompleted(
    string Name,
    ToolCallStatus Status,
    TimeSpan Duration) : TuiEvent;

/// <summary>
/// The approval gate is waiting for the user to approve or deny a tool call.
/// </summary>
/// <remarks>
/// The agent turn is blocked on <see cref="Gate"/>.<c>Task</c> until the consumer loop
/// resolves it. The consumer sets <see cref="TaskCompletionSource{TResult}.TrySetResult"/>
/// to <c>true</c> on approval or <c>false</c> on denial. The <c>CancellationToken</c>
/// registered in <see cref="TuiApprovalGate"/> handles <c>Ctrl+C</c> cancellation
/// independently.
/// </remarks>
/// <param name="ToolName">The tool requesting approval.</param>
/// <param name="CallSummary">Short human-readable description of what the call will do.</param>
/// <param name="Args">Structured arguments for display in the approval panel.</param>
/// <param name="Gate">
/// The <see cref="TaskCompletionSource{TResult}"/> the consumer loop resolves when the
/// user presses <c>y</c> or <c>n</c>.
/// </param>
public sealed record ApprovalRequested(
    string ToolName,
    string CallSummary,
    IReadOnlyDictionary<string, object?> Args,
    TaskCompletionSource<bool> Gate) : TuiEvent;

/// <summary>The agent turn completed — either normally or at the turn limit.</summary>
/// <param name="Result">The outcome returned by <see cref="Phelix.Core.Session.PhelixSession.RunTurnAsync"/>.</param>
public sealed record TurnCompleted(TurnResult Result) : TuiEvent;

/// <summary>The agent turn ended due to an unhandled exception.</summary>
/// <param name="ErrorMessage">The exception message surfaced for display.</param>
public sealed record TurnFailed(string ErrorMessage) : TuiEvent;

/// <summary>The user pressed a key on the keyboard.</summary>
/// <param name="Key">The full key info including modifiers.</param>
public sealed record KeyPressed(ConsoleKeyInfo Key) : TuiEvent;
