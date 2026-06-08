using System.Threading.Channels;
using Phelix.Core.Agent;
using Phelix.Core.Session;
using Spectre.Console;

namespace Phelix.Tui;

/// <summary>
/// The TUI entry point. Drives <see cref="PhelixSession"/> via an event-driven loop.
/// </summary>
/// <remarks>
/// <para>
/// Two producers write <see cref="TuiEvent"/> records into an unbounded
/// <see cref="Channel{T}"/>:
/// <list type="bullet">
///   <item>The <b>keyboard task</b> — wraps each <see cref="ConsoleKeyInfo"/> in a
///   <see cref="KeyPressed"/> event.</item>
///   <item>The <b>agent callbacks</b> — <see cref="TurnCallbacks"/> delegates that
///   write <see cref="ChunkReceived"/>, <see cref="ToolStarted"/>, and
///   <see cref="ToolCompleted"/> events as the model responds.</item>
/// </list>
/// A single consumer loop reads from the channel, applies each event to
/// <see cref="TuiState"/> via <see cref="TuiState.Apply"/>, then calls
/// <c>ctx.Refresh()</c>. All state mutation is serialized through the channel —
/// no locks are needed.
/// </para>
/// <para>
/// When the user presses <c>Enter</c>, the prompt is submitted to
/// <see cref="PhelixSession.RunTurnAsync"/> via <see cref="Task.Run"/> so the agent
/// turn executes on the ThreadPool without blocking the consumer loop. The agent writes
/// a <see cref="TurnCompleted"/> event when it finishes, which brings the display back
/// to <see cref="TuiPhase.Idle"/>.
/// </para>
/// </remarks>
/// <param name="session">The session to drive. Constructed once per session by <c>PhelixHost</c>.</param>
/// <param name="initialState">The starting state, populated from session metadata by <c>PhelixHost</c>.</param>
/// <param name="channel">
/// The event channel shared with <see cref="TuiApprovalGate"/>. Created by <c>Program.cs</c>
/// before calling <c>PhelixHost.Build</c> so that the gate and session share the same writer.
/// </param>
public sealed class TuiSession(PhelixSession session, TuiState initialState, Channel<TuiEvent> channel)
{
    /// <summary>
    /// Starts the keyboard loop, consumer loop, and Spectre.Console live display.
    /// Returns when the user quits (<c>q</c>) or <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <param name="cancellationToken">Propagates cancellation to the agent and keyboard loop.</param>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        Task keyboardTask = RunKeyboardLoopAsync(channel.Writer, cancellationToken);

        await RunConsumerLoopAsync(channel, cancellationToken);

        channel.Writer.TryComplete();
        await keyboardTask.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
    }

    // ─── Keyboard loop ─────────────────────────────────────────────────────────

    /// <summary>
    /// Reads keypresses and writes <see cref="KeyPressed"/> events to the channel.
    /// </summary>
    /// <remarks>
    /// <c>Console.ReadKey(intercept: true)</c> suppresses the default terminal echo.
    /// The renderer draws the input line from <see cref="TuiState.CurrentInput"/> instead,
    /// giving full control over how typed text appears.
    /// </remarks>
    static async Task RunKeyboardLoopAsync(
        ChannelWriter<TuiEvent> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // ReadKey blocks synchronously — offload to a thread pool thread so
                // the keyboard loop does not occupy the default synchronization context.
                ConsoleKeyInfo key = await Task.Run(
                    () => Console.ReadKey(intercept: true),
                    cancellationToken);

                await writer.WriteAsync(new KeyPressed(key), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    // ─── Consumer loop ─────────────────────────────────────────────────────────

    async Task RunConsumerLoopAsync(
        Channel<TuiEvent> channel,
        CancellationToken cancellationToken)
    {
        TuiState state = initialState;

        try
        {
            await AnsiConsole.Live(TuiRenderer.Render(state))
                .AutoClear(false)
                .StartAsync(async ctx =>
                {
                    await foreach (TuiEvent e in channel.Reader.ReadAllAsync(cancellationToken))
                    {
                        if (e is KeyPressed { Key.Key: ConsoleKey.Enter } &&
                            state.Phase == TuiPhase.Idle)
                        {
                            state = HandleEnter(state, channel.Writer, cancellationToken);
                            ctx.UpdateTarget(TuiRenderer.Render(state));
                            ctx.Refresh();
                            continue;
                        }

                        if (e is KeyPressed { Key.Key: ConsoleKey.Q } &&
                            state.Phase == TuiPhase.Idle)
                        {
                            channel.Writer.TryComplete();
                            break;
                        }

                        state = TuiState.Apply(state, e);
                        ctx.UpdateTarget(TuiRenderer.Render(state));
                        ctx.Refresh();
                    }
                });
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown via Ctrl+C.
        }
    }

    /// <summary>
    /// Handles the Enter key: submits the buffered prompt to <see cref="PhelixSession"/>
    /// on the ThreadPool and returns the updated state with an empty input buffer.
    /// </summary>
    /// <remarks>
    /// <see cref="Task.Run"/> is deliberate. <see cref="PhelixSession.RunTurnAsync"/>
    /// blocks for the full duration of the model call. Running it inline would stall the
    /// consumer loop — no keyboard events would process and no display updates would occur
    /// until the model responded. Moving it to the ThreadPool keeps the consumer loop free.
    /// The agent writes a <see cref="TurnCompleted"/> event when it finishes, which brings
    /// the display back to <see cref="TuiPhase.Idle"/>.
    /// </remarks>
    TuiState HandleEnter(
        TuiState state,
        ChannelWriter<TuiEvent> writer,
        CancellationToken cancellationToken)
    {
        string prompt = state.CurrentInput.Trim();

        if (string.IsNullOrEmpty(prompt))
            return state;

        DisplayMessage userMessage = new(
            Speaker: "You",
            Text: prompt,
            Timestamp: DateTimeOffset.UtcNow,
            ToolCards: []);

        TuiState submittedState = state with
        {
            CurrentInput = string.Empty,
            Phase = TuiPhase.Running,
            Messages = state.Messages.Add(userMessage),
        };

        _ = Task.Run(async () =>
        {
            TurnCallbacks callbacks = BuildCallbacks(writer);
            TurnResult result = await session.RunTurnAsync(prompt, callbacks, cancellationToken);
            await writer.WriteAsync(new TurnCompleted(result), cancellationToken);
        }, cancellationToken);

        return submittedState;
    }

    // ─── Callbacks ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the <see cref="TurnCallbacks"/> that wire the agent loop's events into
    /// the TUI's event channel.
    /// </summary>
    static TurnCallbacks BuildCallbacks(ChannelWriter<TuiEvent> writer) => new(
        OnChunk: text =>
            writer.WriteAsync(new ChunkReceived(text)).AsTask(),
        OnToolStarted: (name, args) =>
            writer.WriteAsync(new ToolStarted(name, args)).AsTask(),
        OnToolCompleted: (name, status, duration) =>
            writer.WriteAsync(new ToolCompleted(name, status, duration)).AsTask()
    );
}
