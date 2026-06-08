using System.CommandLine;
using System.Threading.Channels;
using OpenTelemetry.Trace;
using Phelix.Cli;
using Phelix.Core.Agent;
using Phelix.Core.Session;
using Phelix.Tui;

// ─── Options & arguments ──────────────────────────────────────────────────────

Option<bool> cliFlag          = new("--cli",           "Run in terminal REPL mode instead of the TUI.");
Option<bool> acceptsEditsFlag = new("--accepts-edits", "Auto-approve Prompt-tier tool calls (CLI mode only).");
Option<bool> allowAllFlag     = new("--allow-all",     "Auto-approve all tool calls (CLI mode only).");
Argument<string?> promptArg   = new("prompt") { Arity = ArgumentArity.ZeroOrOne };

// ─── Root command — TUI by default ────────────────────────────────────────────

RootCommand root = new("Phelix — AI coding agent");
root.Options.Add(cliFlag);
root.Options.Add(acceptsEditsFlag);
root.Options.Add(allowAllFlag);
root.Arguments.Add(promptArg);

root.SetAction(async (ParseResult result, CancellationToken ct) =>
{
    bool isCli        = result.GetValue(cliFlag);
    bool acceptsEdits = result.GetValue(acceptsEditsFlag);
    bool allowAll     = result.GetValue(allowAllFlag);
    string? prompt    = result.GetValue(promptArg);

    if (isCli)
        await RunCliAsync(prompt, acceptsEdits, allowAll, ct);
    else
        await RunTuiAsync(ct);
});

return await root.Parse(args).InvokeAsync();

// ─── TUI path ─────────────────────────────────────────────────────────────────

static async Task RunTuiAsync(CancellationToken ct)
{
    Channel<TuiEvent> channel = Channel.CreateUnbounded<TuiEvent>(
        new UnboundedChannelOptions { SingleReader = true });

    (PhelixSession session,
     ISessionStore sessionStore,
     TracerProvider? tracerProvider,
     TuiState? initialState) = PhelixHost.Build(new HostMode.Tui(channel.Writer));

    using TracerProvider? _ = tracerProvider;
    using IDisposable sessionStoreDisposable = (IDisposable)sessionStore;

    TuiSession tui = new(session, initialState!, channel);
    await tui.RunAsync(ct);
}

// ─── CLI path ─────────────────────────────────────────────────────────────────

static async Task RunCliAsync(string? prompt, bool acceptsEdits, bool allowAll, CancellationToken ct)
{
    SessionMode sessionMode = allowAll     ? SessionMode.AllowAll
                            : acceptsEdits ? SessionMode.AcceptsEdits
                            : SessionMode.Default;

    (PhelixSession session,
     ISessionStore sessionStore,
     TracerProvider? tracerProvider,
     TuiState? _) = PhelixHost.Build(new HostMode.Cli(sessionMode));

    using TracerProvider? otel = tracerProvider;
    using IDisposable sessionStoreDisposable = (IDisposable)sessionStore;

    if (prompt is not null)
    {
        await RunSingleTurnAsync(session, prompt.Trim(), ct);
        return;
    }

    Console.WriteLine("Phelix — type 'exit' to quit.");
    Console.WriteLine();

    while (true)
    {
        Console.Write("> ");
        string? rawInput = Console.ReadLine();

        if (rawInput is null || rawInput.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
            break;

        string userPrompt = rawInput.Trim();

        if (string.IsNullOrEmpty(userPrompt))
            continue;

        await RunSingleTurnAsync(session, userPrompt, ct);
    }
}

static async Task RunSingleTurnAsync(PhelixSession session, string userPrompt, CancellationToken ct)
{
    TurnCallbacks callbacks = new(OnChunk: TerminalRenderer.WriteChunk);
    TurnResult result = await session.RunTurnAsync(userPrompt, callbacks, ct);

    Console.WriteLine();

    switch (result)
    {
        case TurnResult.Success success when success.Turn.ExitReason == TurnExitReason.TurnLimitReached:
            Console.WriteLine("[turn limit reached]");
            break;

        case TurnResult.Failure failure:
            Console.WriteLine($"Error: {failure.ErrorMessage}");
            break;
    }
}
