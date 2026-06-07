using Microsoft.Extensions.AI;
using OpenTelemetry.Trace;
using Phelix.Cli;
using Phelix.Core.Agent;
using Phelix.Core.Session;
using Phelix.Tui;

(AgentLoop agentLoop,
 ISessionStore sessionStore,
 ICompactionPolicy compactionPolicy,
 ISessionSummarizer summarizer,
 TracerProvider? tracerProvider) = PhelixHost.Build();

using TracerProvider? _ = tracerProvider;
using IDisposable sessionStoreDisposable = (IDisposable)sessionStore;

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
        string turnId = Guid.NewGuid().ToString("N");
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;

        Turn completedTurn = await agentLoop.RunTurnAsync(conversationHistory, userPrompt, TerminalRenderer.WriteChunk);

        Console.WriteLine();

        if (completedTurn.ExitReason == TurnExitReason.TurnLimitReached)
            Console.WriteLine("[turn limit reached]");

        conversationHistory = completedTurn.ContextMessages;

        TurnRecord record = TurnRecord.FromTurn(
            completedTurn,
            sessionId: SessionLogger.SessionId,
            userMessage: userPrompt,
            turnId: turnId,
            startedAt: startedAt
        );

        await SessionLogger.AppendAsync(record);
        await sessionStore.AppendAsync(record);

        if (compactionPolicy.ShouldCompact(conversationHistory))
        {
            string summary = await summarizer.SummarizeAsync(SessionLogger.SessionId);

            if (!string.IsNullOrEmpty(summary))
            {
                conversationHistory =
                [
                    new ChatMessage(ChatRole.System, $"[Session compacted]\n\n{summary}")
                ];
                Console.WriteLine("[context compacted — summary injected]");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine();
        Console.WriteLine($"Error: {ex.Message}");
    }
}

return 0;
