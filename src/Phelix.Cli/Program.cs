using OpenTelemetry.Trace;
using Phelix.Cli;
using Phelix.Core.Agent;
using Phelix.Core.Session;
using Phelix.Tui;

SessionMode sessionMode = args.Contains("--allow-all")    ? SessionMode.AllowAll
                        : args.Contains("--accepts-edits") ? SessionMode.AcceptsEdits
                        : SessionMode.Default;

(PhelixSession session,
 ISessionStore sessionStore,
 TracerProvider? tracerProvider) = PhelixHost.Build(sessionMode);

using TracerProvider? _ = tracerProvider;
using IDisposable sessionStoreDisposable = (IDisposable)sessionStore;

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

    TurnCallbacks callbacks = new(OnChunk: TerminalRenderer.WriteChunk);

    TurnResult result = await session.RunTurnAsync(userPrompt, callbacks);

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

return 0;
