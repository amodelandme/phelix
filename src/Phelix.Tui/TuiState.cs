using System.Collections.Immutable;
using Phelix.Core.Agent;
using Phelix.Core.Session;

namespace Phelix.Tui;

/// <summary>
/// The phase the TUI is in at any given moment. Drives which components the renderer shows.
/// </summary>
public enum TuiPhase
{
    /// <summary>Waiting for user input. Prompt input is visible.</summary>
    Idle,

    /// <summary>The model is responding — streaming text or thinking after tool results.</summary>
    Running,

    /// <summary>A tool call is actively executing.</summary>
    ToolRunning,

    /// <summary>Waiting for the user to approve or deny a tool call.</summary>
    AwaitingApproval,

    /// <summary>An error occurred or the turn limit was reached.</summary>
    Error,
}

/// <summary>The display status of a tool card in the conversation.</summary>
public enum ToolCardStatus
{
    /// <summary>The tool is currently executing.</summary>
    Running,

    /// <summary>The tool completed successfully.</summary>
    Done,

    /// <summary>The tool call was denied by the user.</summary>
    Denied,

    /// <summary>The tool call failed (unregistered tool or execution error).</summary>
    Failed,
}

/// <summary>
/// A rendered tool call card shown beneath a Phelix message.
/// </summary>
/// <param name="Name">The tool's registered name.</param>
/// <param name="Args">The resolved arguments, shown in the card's argument grid.</param>
/// <param name="Status">The current state of the call.</param>
/// <param name="Duration">Wall-clock time taken. Zero while still running.</param>
public sealed record ToolCard(
    string Name,
    IReadOnlyDictionary<string, object?> Args,
    ToolCardStatus Status,
    TimeSpan Duration);

/// <summary>
/// A single message in the conversation history, owned by either the user or Phelix.
/// </summary>
/// <param name="Speaker">Either <c>"You"</c> or <c>"Phelix"</c>.</param>
/// <param name="Text">The accumulated text content of the message.</param>
/// <param name="Timestamp">When this message was first created.</param>
/// <param name="ToolCards">Tool call cards attached to this message. Empty for user messages.</param>
public sealed record DisplayMessage(
    string Speaker,
    string Text,
    DateTimeOffset Timestamp,
    ImmutableArray<ToolCard> ToolCards);

/// <summary>
/// The data the <see cref="TuiApprovalGate"/> posts when it needs user input to proceed.
/// </summary>
/// <param name="ToolName">The tool requesting approval.</param>
/// <param name="CallSummary">Short description of what the call will do.</param>
/// <param name="Args">Structured arguments shown in the approval panel's argument grid.</param>
/// <param name="Gate">
/// Resolved by the consumer loop when the user presses <c>y</c> (<c>true</c>) or
/// <c>n</c> / <c>Escape</c> (<c>false</c>).
/// </param>
public sealed record ApprovalRequest(
    string ToolName,
    string CallSummary,
    IReadOnlyDictionary<string, object?> Args,
    TaskCompletionSource<bool> Gate);

/// <summary>
/// The complete, immutable snapshot of everything the renderer needs to draw one frame.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Apply"/> is the only way to produce a new state. It is a pure function:
/// no side effects, no I/O. The consumer loop in <see cref="TuiSession"/> calls it
/// after every event and passes the result to <see cref="TuiRenderer.Render"/>.
/// </para>
/// <para>
/// Immutability means the renderer can read this record safely on the same thread that
/// the consumer loop writes it — the channel serializes all writes, so there are no
/// concurrent readers or writers.
/// </para>
/// </remarks>
/// <param name="Phase">The current activity phase. Controls which components are rendered.</param>
/// <param name="Messages">All messages in the conversation so far, in order.</param>
/// <param name="CurrentInput">The user's in-progress input string. Empty between turns.</param>
/// <param name="ActiveTool">The currently executing tool card. Non-null only in <see cref="TuiPhase.ToolRunning"/>.</param>
/// <param name="TotalTokens">Cumulative input + output token count across all turns this session.</param>
/// <param name="PendingApproval">The approval request awaiting user input. Non-null only in <see cref="TuiPhase.AwaitingApproval"/>.</param>
/// <param name="ErrorMessage">The error or turn-limit message to display. Non-null only in <see cref="TuiPhase.Error"/>.</param>
/// <param name="TurnNumber">The number of turns completed so far.</param>
/// <param name="MaxTurns">The configured turn limit for this session.</param>
/// <param name="SessionId">The session GUID, truncated to 5 chars in the top bar.</param>
/// <param name="ModelId">The active model identifier shown in the top bar.</param>
/// <param name="Provider">The active provider name shown in the top bar.</param>
public sealed record TuiState(
    TuiPhase Phase,
    ImmutableArray<DisplayMessage> Messages,
    string CurrentInput,
    ToolCard? ActiveTool,
    int TotalTokens,
    ApprovalRequest? PendingApproval,
    string? ErrorMessage,
    int TurnNumber,
    int MaxTurns,
    string SessionId,
    string ModelId,
    string Provider)
{
    /// <summary>
    /// Applies <paramref name="e"/> to <paramref name="current"/> and returns the next state.
    /// </summary>
    /// <remarks>
    /// Pure function — no side effects, no I/O. Every event type produces a new record via
    /// <c>with</c> expressions; unrecognised events return <paramref name="current"/> unchanged.
    /// The <see cref="KeyPressed"/> case for <c>Enter</c> is intentionally not handled here —
    /// submitting a prompt has side effects (calling <see cref="Phelix.Core.Session.PhelixSession"/>),
    /// so it is handled directly in the consumer loop of <see cref="TuiSession"/>.
    /// </remarks>
    /// <param name="current">The state before the event.</param>
    /// <param name="e">The event to apply.</param>
    /// <returns>The next state.</returns>
    public static TuiState Apply(TuiState current, TuiEvent e) => e switch
    {
        ChunkReceived chunk         => ApplyChunkReceived(current, chunk),
        ToolStarted started         => ApplyToolStarted(current, started),
        ToolCompleted completed     => ApplyToolCompleted(current, completed),
        ApprovalRequested requested => ApplyApprovalRequested(current, requested),
        TurnCompleted turnCompleted => ApplyTurnCompleted(current, turnCompleted),
        TurnFailed failed           => current with { Phase = TuiPhase.Error, ErrorMessage = failed.ErrorMessage },
        KeyPressed key              => ApplyKeyPressed(current, key),
        _                           => current,
    };

    // ─── Private apply helpers ──────────────────────────────────────────────────

    static TuiState ApplyChunkReceived(TuiState current, ChunkReceived chunk)
    {
        ImmutableArray<DisplayMessage> messages = current.Messages;

        if (messages.Length > 0 && messages[^1].Speaker == "Phelix")
        {
            DisplayMessage last = messages[^1];
            DisplayMessage updated = last with { Text = last.Text + chunk.Text };
            messages = messages.SetItem(messages.Length - 1, updated);
        }
        else
        {
            DisplayMessage newMessage = new(
                Speaker: "Phelix",
                Text: chunk.Text,
                Timestamp: DateTimeOffset.UtcNow,
                ToolCards: []);
            messages = messages.Add(newMessage);
        }

        return current with { Phase = TuiPhase.Running, Messages = messages };
    }

    static TuiState ApplyToolStarted(TuiState current, ToolStarted started)
    {
        ToolCard card = new(
            Name: started.Name,
            Args: started.Args,
            Status: ToolCardStatus.Running,
            Duration: TimeSpan.Zero);

        ImmutableArray<DisplayMessage> messages = current.Messages;

        if (messages.Length > 0 && messages[^1].Speaker == "Phelix")
        {
            DisplayMessage last = messages[^1];
            DisplayMessage updated = last with { ToolCards = last.ToolCards.Add(card) };
            messages = messages.SetItem(messages.Length - 1, updated);
        }
        else
        {
            DisplayMessage newMessage = new(
                Speaker: "Phelix",
                Text: string.Empty,
                Timestamp: DateTimeOffset.UtcNow,
                ToolCards: [card]);
            messages = messages.Add(newMessage);
        }

        return current with
        {
            Phase = TuiPhase.ToolRunning,
            Messages = messages,
            ActiveTool = card,
        };
    }

    static TuiState ApplyToolCompleted(TuiState current, ToolCompleted completed)
    {
        ToolCardStatus cardStatus = completed.Status switch
        {
            ToolCallStatus.Succeeded => ToolCardStatus.Done,
            ToolCallStatus.Denied    => ToolCardStatus.Denied,
            _                        => ToolCardStatus.Failed,
        };

        ImmutableArray<DisplayMessage> messages = current.Messages;

        if (messages.Length > 0 && messages[^1].Speaker == "Phelix")
        {
            DisplayMessage last = messages[^1];
            ImmutableArray<ToolCard> cards = last.ToolCards;

            int cardIndex = cards.Length - 1;
            if (cardIndex >= 0 && cards[cardIndex].Name == completed.Name)
            {
                ToolCard finishedCard = cards[cardIndex] with
                {
                    Status = cardStatus,
                    Duration = completed.Duration,
                };
                cards = cards.SetItem(cardIndex, finishedCard);
            }

            messages = messages.SetItem(messages.Length - 1, last with { ToolCards = cards });
        }

        return current with
        {
            Phase = TuiPhase.Running,
            Messages = messages,
            ActiveTool = null,
        };
    }

    static TuiState ApplyApprovalRequested(TuiState current, ApprovalRequested requested)
    {
        ApprovalRequest approvalRequest = new(
            ToolName: requested.ToolName,
            CallSummary: requested.CallSummary,
            Args: requested.Args,
            Gate: requested.Gate);

        return current with
        {
            Phase = TuiPhase.AwaitingApproval,
            PendingApproval = approvalRequest,
        };
    }

    static TuiState ApplyTurnCompleted(TuiState current, TurnCompleted turnCompleted)
    {
        if (turnCompleted.Result is TurnResult.Success success &&
            success.Turn.ExitReason == TurnExitReason.TurnLimitReached)
        {
            return current with
            {
                Phase = TuiPhase.Error,
                ErrorMessage = $"Turn limit reached ({current.TurnNumber + 1}/{current.MaxTurns}). " +
                               "Increase max_turns in ~/.phelix/config.yaml or start a new session.",
                ActiveTool = null,
                PendingApproval = null,
            };
        }

        int additionalTokens = turnCompleted.Result is TurnResult.Success s
            ? s.Turn.Usage.InputTokens + s.Turn.Usage.OutputTokens
            : 0;

        return current with
        {
            Phase = TuiPhase.Idle,
            ActiveTool = null,
            PendingApproval = null,
            ErrorMessage = null,
            TurnNumber = current.TurnNumber + 1,
            TotalTokens = current.TotalTokens + additionalTokens,
        };
    }

    static TuiState ApplyKeyPressed(TuiState current, KeyPressed keyPressed)
    {
        ConsoleKeyInfo key = keyPressed.Key;

        if (current.Phase == TuiPhase.AwaitingApproval && current.PendingApproval is not null)
        {
            if (key.Key == ConsoleKey.Y)
            {
                current.PendingApproval.Gate.TrySetResult(true);
                return current with { Phase = TuiPhase.Running, PendingApproval = null };
            }

            if (key.Key == ConsoleKey.N || key.Key == ConsoleKey.Escape)
            {
                current.PendingApproval.Gate.TrySetResult(false);
                return current with { Phase = TuiPhase.Running, PendingApproval = null };
            }

            return current;
        }

        if (current.Phase == TuiPhase.Idle)
        {
            if (key.Key == ConsoleKey.Backspace)
            {
                string trimmed = current.CurrentInput.Length > 0
                    ? current.CurrentInput[..^1]
                    : string.Empty;
                return current with { CurrentInput = trimmed };
            }

            // Enter is handled in TuiSession directly — not here — because
            // submitting the prompt has side effects (calling PhelixSession).
            if (key.Key == ConsoleKey.Enter)
                return current;

            if (!char.IsControl(key.KeyChar))
                return current with { CurrentInput = current.CurrentInput + key.KeyChar };
        }

        return current;
    }
}
