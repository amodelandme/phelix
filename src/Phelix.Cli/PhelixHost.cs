using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Phelix.Core.Agent;
using Phelix.Core.Config;
using Phelix.Core.Session;
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
    /// Constructs and returns all application components needed to run the REPL loop.
    /// </summary>
    /// <remarks>
    /// The caller is responsible for disposing <c>SessionStore</c> and
    /// <paramref name="tracerProvider"/> (via the returned tuple) when the session ends.
    /// When <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is not set, <c>TracerProvider</c> is
    /// <c>null</c> and tracing is a no-op with zero overhead.
    /// </remarks>
    /// <returns>
    /// A named tuple containing the configured <see cref="AgentLoop"/>,
    /// <see cref="ISessionStore"/>, <see cref="ICompactionPolicy"/>,
    /// <see cref="ISessionSummarizer"/>, and an optional <see cref="TracerProvider"/>.
    /// </returns>
    internal static (
        AgentLoop AgentLoop,
        ISessionStore SessionStore,
        ICompactionPolicy CompactionPolicy,
        ISessionSummarizer Summarizer,
        TracerProvider? TracerProvider) Build()
    {
        PhelixConfig config = ConfigLoader.Load();
        ModelConfig activeModel = config.Models[config.ActiveModel];
        ProviderConfig provider = config.Providers[activeModel.Provider];

        string? otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

        TracerProvider? tracerProvider = otlpEndpoint is not null
            ? Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("phelix"))
                .AddSource(PhelixTelemetry.SourceName)
                .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint))
                .Build()
            : null;

        string apiKey = Environment.GetEnvironmentVariable(provider.ApiKeyEnv)
            ?? throw new ConfigException($"Environment variable '{provider.ApiKeyEnv}' is not set.");

        OpenAIClient openAiClient = new(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(provider.BaseUrl) }
        );

        RetryPolicy retryPolicy = ConfigLoader.ResolveRetryPolicy(config, activeModel);

        // Potential prompt injection vector if ModelId is user-controlled — in production,
        // validate against an allowlist or use separate credentials per model.
        IChatClient chatClient = new ChatClientBuilder(
                openAiClient.GetChatClient(activeModel.ModelId).AsIChatClient())
            .Use(inner => new RetryingChatClient(inner, retryPolicy))
            .UseOpenTelemetry(loggerFactory: null, sourceName: PhelixTelemetry.SourceName)
            .Build();

        AgentOptions agentOptions = new()
        {
            ModelId = activeModel.ModelId,
            SystemPrompt = config.SystemPrompt,
            MaxTurns = activeModel.MaxTurns
        };

        SqliteSessionStore sessionStore = new(SessionLogger.SessionId);

        ICompactionPolicy compactionPolicy =
            new TokenThresholdPolicy(agentOptions.CompactionThresholdTokens);

        ISessionSummarizer summarizer =
            new ModelSessionSummarizer(chatClient, sessionStore);

        ToolRegistry toolRegistry = new();
        toolRegistry.Register(new ReadFileTool());
        toolRegistry.Register(new WriteFileTool());
        toolRegistry.Register(new BashTool());
        toolRegistry.Register(new ListFilesTool());
        toolRegistry.Register(new SearchCodeTool());
        toolRegistry.Register(new SearchSessionTool(sessionStore));

        AgentLoop agentLoop = new(chatClient, agentOptions, toolRegistry);

        return (agentLoop, sessionStore, compactionPolicy, summarizer, tracerProvider);
    }
}
