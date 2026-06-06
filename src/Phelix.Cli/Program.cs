using Microsoft.Extensions.AI;
using OpenTelemetry.Trace;
using Phelix.Cli;
using Phelix.Core.Agent;
using Phelix.Tui;

(AgentLoop agentLoop, TracerProvider? tracerProvider) = PhelixHost.Build();
using TracerProvider? _ = tracerProvider;

IReadOnlyList<ChatMessage> conversationHistory = [];

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

    try
    {
        Turn completedTurn = await agentLoop.RunTurnAsync(conversationHistory, userPrompt, TerminalRenderer.WriteChunk);

        Console.WriteLine();

        if (completedTurn.ExitReason == TurnExitReason.TurnLimitReached)
            Console.WriteLine("[turn limit reached]");

        conversationHistory = completedTurn.Messages;

        await Phelix.Core.Session.SessionLogger.AppendAsync(completedTurn, userPrompt);
    }
    catch (Exception ex)
    {
        Console.WriteLine();
        Console.WriteLine($"Error: {ex.Message}");
    }
}

return 0;
