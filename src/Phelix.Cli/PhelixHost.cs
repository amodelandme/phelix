using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Phelix.Core.Agent;
using Phelix.Core.Telemetry;
using Phelix.Core.Tools;

namespace Phelix.Cli;

/// <summary>
/// Wires together all application dependencies and returns a ready-to-run <see cref="AgentLoop"/>.
/// </summary>
/// <remarks>
/// Owns OTel tracer setup, the <see cref="IChatClient"/> construction, <see cref="AgentOptions"/>,
/// and <see cref="ToolRegistry"/> population. <c>Program.cs</c> calls <see cref="Build"/> and
/// receives exactly what it needs to run the REPL — nothing else lives here.
/// </remarks>
internal static class PhelixHost
{
    /// <summary>
    /// Constructs and returns the <see cref="AgentLoop"/> and an optional <see cref="TracerProvider"/>.
    /// </summary>
    /// <remarks>
    /// The caller is responsible for disposing <paramref name="tracerProvider"/> when the session ends.
    /// When <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is not set, <paramref name="tracerProvider"/> is <c>null</c>
    /// and tracing is a no-op with zero overhead.
    /// </remarks>
    /// <returns>
    /// A tuple of the configured <see cref="AgentLoop"/> and an optional <see cref="TracerProvider"/>.
    /// </returns>
    internal static (AgentLoop AgentLoop, TracerProvider? TracerProvider) Build()
    {
        string? otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

        TracerProvider? tracerProvider = otlpEndpoint is not null
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

        // Potential prompt injection vector if ModelId is user-controlled — in production,
        // validate against an allowlist or use separate credentials per model.
        IChatClient chatClient = new ChatClientBuilder(
                openAiClient.GetChatClient("qwen/qwen3.5-flash-02-23").AsIChatClient())
            .UseOpenTelemetry(loggerFactory: null, sourceName: PhelixTelemetry.SourceName)
            .Build();

        AgentOptions agentOptions = new()
        {
            ModelId = "qwen/qwen3.5-flash-02-23",
            SystemPrompt = "You are a helpful coding assistant."
        };

        ToolRegistry toolRegistry = new();
        toolRegistry.Register(new ReadFileTool());
        toolRegistry.Register(new WriteFileTool());
        toolRegistry.Register(new BashTool());
        toolRegistry.Register(new ListFilesTool());
        toolRegistry.Register(new SearchCodeTool());

        AgentLoop agentLoop = new(chatClient, agentOptions, toolRegistry);

        return (agentLoop, tracerProvider);
    }
}
