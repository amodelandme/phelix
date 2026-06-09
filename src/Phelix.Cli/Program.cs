using System.CommandLine;
using OpenTelemetry.Trace;
using Phelix.Cli;
using Phelix.Core.Agent;
using Phelix.Core.Session;

// ─── Options & arguments ──────────────────────────────────────────────────────

Option<bool>    acceptsEditsFlag    = new("--accepts-edits")    { Description = "Auto-approve Prompt-tier tool calls." };
Option<bool>    allowAllFlag        = new("--allow-all")        { Description = "Auto-approve all tool calls." };
Option<string?> acceptsCommandsFlag = new("--accepts-commands") { Description = "Comma-separated bash executable names to auto-approve (e.g. dotnet,git)." };
Argument<string?> promptArg         = new("prompt")             { Arity = ArgumentArity.ZeroOrOne };

// ─── Root command ─────────────────────────────────────────────────────────────

RootCommand root = new("Phelix — AI coding agent");
root.Options.Add(acceptsEditsFlag);
root.Options.Add(allowAllFlag);
root.Options.Add(acceptsCommandsFlag);
root.Arguments.Add(promptArg);

root.SetAction(async (ParseResult result, CancellationToken ct) =>
{
    bool acceptsEdits      = result.GetValue(acceptsEditsFlag);
    bool allowAll          = result.GetValue(allowAllFlag);
    string? acceptsCommands = result.GetValue(acceptsCommandsFlag);
    string? prompt         = result.GetValue(promptArg);

    SessionMode sessionMode = allowAll     ? SessionMode.AllowAll
                            : acceptsEdits ? SessionMode.AcceptsEdits
                            : SessionMode.Default;

    HashSet<string> allowedCommandPrefixes = acceptsCommands is not null
        ? [.. acceptsCommands.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
        : [];

    (PhelixSession session,
     ISessionStore sessionStore,
     TracerProvider? tracerProvider) = PhelixHost.Build(sessionMode, allowedCommandPrefixes);

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
});

return await root.Parse(args).InvokeAsync();

// ─── Helpers ──────────────────────────────────────────────────────────────────

static async Task RunSingleTurnAsync(PhelixSession session, string userPrompt, CancellationToken ct)
{
    TurnCallbacks callbacks = new(
        OnChunk:         CliRenderer.WriteChunk,
        OnToolStarted:   CliRenderer.WriteToolStarted,
        OnToolCompleted: CliRenderer.WriteToolCompleted
    );

    TurnResult result = await session.RunTurnAsync(userPrompt, callbacks, ct);

    Console.WriteLine();

    switch (result)
    {
        case TurnResult.Success success when success.Turn.ExitReason == TurnExitReason.TurnLimitReached:
            CliRenderer.WriteWarning("[turn limit reached]");
            break;

        case TurnResult.Failure failure:
            CliRenderer.WriteError(failure.ErrorMessage);
            break;
    }

    CliRenderer.WriteTurnSeparator();
}
