using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using Phelix.Core.Agent;
using Phelix.Core.Session;
using Phelix.Core.Tools;
using Phelix.Tui;

OpenAIClient openAiClient = new(
    new ApiKeyCredential(Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")!),
    new OpenAIClientOptions { Endpoint = new Uri("https://openrouter.ai/api/v1") }
);

IChatClient chatClient = openAiClient.GetChatClient("google/gemma-4-31b-it:free").AsIChatClient();

AgentOptions agentOptions = new()
{
    ModelId = "google/gemma-4-31b-it:free",
    SystemPrompt = "You are a helpful coding assistant."
};

ToolRegistry toolRegistry = new();
toolRegistry.Register(new ReadFileTool());

AgentLoop agentLoop = new(chatClient, agentOptions, toolRegistry);

// The full conversation history, grown turn by turn and passed into every model call.
// The model has no memory of its own — this list IS the memory.
List<ChatMessage> conversationHistory = new();

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

    Turn completedTurn = await agentLoop.RunTurnAsync(conversationHistory, userPrompt, TerminalRenderer.WriteChunk);

    Console.WriteLine();

    // Turn.Messages already contains the full history plus the new user message.
    // Append only the assistant reply — that's the one piece RunTurnAsync doesn't add.
    List<ChatMessage> updatedHistory = new(completedTurn.Messages)
    {
        new(ChatRole.Assistant, completedTurn.Response.Text ?? string.Empty)
    };
    conversationHistory = updatedHistory;

    await SessionLogger.AppendAsync(completedTurn, userPrompt);
}

return 0;
