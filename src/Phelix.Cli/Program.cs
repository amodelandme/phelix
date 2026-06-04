using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Phelix.Core.Agent;
using Phelix.Core.Session;
using Phelix.Core.Telemetry;
using Phelix.Core.Tools;
using Phelix.Tui;

//////////////////////////////////////////////////////////////////
const string SYSTEM_PROMPT = @"You are a helpful assistant with access to tools.
Use tools whenever they help you give a more accurate answer.
When you have enough information, respond directly and concisely.";
///////////////////////////////////////////////////////////////////

string? otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

using TracerProvider? tracerProvider = otlpEndpoint is not null
    ? Sdk.CreateTracerProviderBuilder()
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("phelix"))
        .AddSource(PhelixTelemetry.SourceName)
        .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint))
        .Build()
    : null;

OpenAIClient openAiClient = new(
    new ApiKeyCredential(Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")!),
    new OpenAIClientOptions { Endpoint = new Uri("https://openrouter.ai/api/v1") }
);

// Potential prompt injection vector if ModelId is user-controlled — in production, validate against allowlist or use separate credentials per model.
IChatClient chatClient = new ChatClientBuilder(
        openAiClient.GetChatClient("moonshotai/kimi-k2.6:free").AsIChatClient())
    .UseOpenTelemetry(loggerFactory: null, sourceName: PhelixTelemetry.SourceName)
    .Build();

AgentOptions agentOptions = new()
{
    ModelId = "moonshotai/kimi-k2.6:free",
    SystemPrompt = SYSTEM_PROMPT,
};

ToolRegistry toolRegistry = new();
toolRegistry.Register(new ReadFileTool());
toolRegistry.Register(new WriteFileTool());
toolRegistry.Register(new RunCommandTool());
toolRegistry.Register(new ListFilesTool());
toolRegistry.Register(new SearchCodeTool());

AgentLoop agentLoop = new(chatClient, agentOptions, toolRegistry);

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

    List<ChatMessage> updatedHistory = new(completedTurn.Messages)
      {
          new(ChatRole.Assistant, completedTurn.Response.Text ?? string.Empty)
      };
    conversationHistory = updatedHistory;

    await SessionLogger.AppendAsync(completedTurn, userPrompt);
}

return 0;