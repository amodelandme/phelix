using System.ClientModel;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Phelix.Core.Agent;
using Phelix.Core.Config;
using Phelix.Core.Context;
using Phelix.Core.Session;
using Phelix.Core.Telemetry;
using Phelix.Core.Tools;
using Phelix.Tui;

namespace Phelix.Cli;

/// <summary>
/// Wires together all application dependencies and returns a ready-to-run <see cref="PhelixSession"/>.
/// </summary>
/// <remarks>
/// Owns OTel tracer setup, the <see cref="IChatClient"/> construction, <see cref="AgentOptions"/>,
/// and <see cref="ToolRegistry"/> population. <c>Program.cs</c> calls <see cref="Build"/> and
/// receives exactly what it needs — nothing else lives here.
/// </remarks>
internal static class PhelixHost
{
    /// <summary>
    /// Constructs and returns all application components needed to run the session.
    /// </summary>
    /// <remarks>
    /// The caller is responsible for disposing <c>SessionStore</c> and
    /// <paramref name="tracerProvider"/> (via the returned tuple) when the session ends.
    /// When <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is not set, <c>TracerProvider</c> is
    /// <c>null</c> and tracing is a no-op with zero overhead.
    ///
    /// When <paramref name="mode"/> is <see cref="HostMode.Tui"/>, <c>InitialState</c> is
    /// non-null and populated with metadata from config. When <paramref name="mode"/> is
    /// <see cref="HostMode.Cli"/> with <see cref="SessionMode.AllowAll"/>, a warning is
    /// printed before the REPL starts.
    /// </remarks>
    /// <param name="mode">
    /// Controls the runtime path and approval gate. Defaults to <see cref="HostMode.Tui"/>.
    /// </param>
    /// <returns>
    /// A named tuple containing the configured <see cref="PhelixSession"/>,
    /// <see cref="ISessionStore"/>, an optional <see cref="TracerProvider"/>, and an
    /// optional <see cref="TuiState"/> (non-null only for <see cref="HostMode.Tui"/>).
    /// The caller is responsible for disposing <see cref="ISessionStore"/> and
    /// <see cref="TracerProvider"/> when the session ends.
    /// </returns>
    internal static (
        PhelixSession Session,
        ISessionStore SessionStore,
        TracerProvider? TracerProvider,
        TuiState? InitialState) Build(HostMode? mode = null)
    {
        mode ??= new HostMode.Tui(Channel.CreateUnbounded<TuiEvent>().Writer);
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

        string systemPrompt = AgentsMdLoader.Load(
            config.SystemPrompt,
            Directory.GetCurrentDirectory());

        IApprovalGate approvalGate = BuildApprovalGate(mode);

        AgentOptions agentOptions = new()
        {
            ModelId = activeModel.ModelId,
            SystemPrompt = systemPrompt,
            MaxTurns = activeModel.MaxTurns,
            ApprovalGate = approvalGate
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

        PhelixSession session = new(agentLoop, sessionStore, compactionPolicy, summarizer);

        TuiState? initialState = mode is HostMode.Tui
            ? new TuiState(
                Phase: TuiPhase.Idle,
                Messages: [],
                CurrentInput: string.Empty,
                ActiveTool: null,
                TotalTokens: 0,
                PendingApproval: null,
                ErrorMessage: null,
                TurnNumber: 0,
                MaxTurns: agentOptions.MaxTurns,
                SessionId: SessionLogger.SessionId,
                ModelId: activeModel.ModelId,
                Provider: activeModel.Provider)
            : null;

        return (session, sessionStore, tracerProvider, initialState);
    }

    /// <summary>
    /// Builds the <see cref="IApprovalGate"/> for <paramref name="mode"/>.
    /// </summary>
    static IApprovalGate BuildApprovalGate(HostMode mode)
    {
        if (mode is HostMode.Tui tui)
            return new TuiApprovalGate(tui.EventWriter);

        if (mode is HostMode.Cli { SessionMode: SessionMode.AllowAll })
        {
            TerminalRenderer.WriteWarning(
                "Running in allow-all mode. All tool calls will execute without approval prompts.");
            return new AutoApproveGate();
        }

        SessionMode sessionMode = mode is HostMode.Cli cli ? cli.SessionMode : SessionMode.Default;
        return new InteractiveApprovalGate(sessionMode, Console.In, Console.Out);
    }
}
