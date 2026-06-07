using System.Text.Json;
using Microsoft.Extensions.AI;
using Phelix.Core.Agent;
using Phelix.Core.Session;

namespace Phelix.Core.Tests.Session;

public class SessionLoggerTests : IDisposable
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly string _logFile;

    public SessionLoggerTests()
    {
        _logFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jsonl");
    }

    public void Dispose()
    {
        if (File.Exists(_logFile))
            File.Delete(_logFile);
    }

    private static Turn BuildFakeTurn(
        string userText,
        string assistantText,
        string modelId,
        IReadOnlyList<ToolCallRecord>? toolCalls = null)
    {
        ChatMessage userMessage = new(ChatRole.User, userText);
        ChatMessage assistantMessage = new(ChatRole.Assistant, assistantText);

        ChatResponse response = new([assistantMessage])
        {
            ModelId = modelId,
        };

        return new Turn(
            Messages: [userMessage, assistantMessage],
            Response: response,
            Timestamp: DateTimeOffset.UtcNow,
            Usage: new UsageSummary(100, 50),
            ToolCalls: toolCalls ?? [],
            ExitReason: TurnExitReason.Completed
        );
    }

    private static TurnRecord BuildRecord(Turn turn, string userMessage) =>
        TurnRecord.FromTurn(
            turn,
            sessionId: "test-session",
            userMessage: userMessage,
            turnId: Guid.NewGuid().ToString("N"),
            startedAt: DateTimeOffset.UtcNow.AddSeconds(-2)
        );

    [Fact]
    public async Task SessionLogger_WritesTwoTurns_ProducesValidJsonlFile()
    {
        Turn turn1 = BuildFakeTurn(
            userText: "What is a record in C#?",
            assistantText: "A record is an immutable reference type with value-based equality.",
            modelId: "fake-model-v1"
        );

        Turn turn2 = BuildFakeTurn(
            userText: "How does it differ from a class?",
            assistantText: "Records generate Equals, GetHashCode, and ToString based on their properties. Classes do not.",
            modelId: "fake-model-v1"
        );

        await SessionLogger.AppendAsync(BuildRecord(turn1, turn1.Messages[0].Text ?? string.Empty), _logFile);
        await SessionLogger.AppendAsync(BuildRecord(turn2, turn2.Messages[0].Text ?? string.Empty), _logFile);

        string[] lines = await File.ReadAllLinesAsync(_logFile);

        Assert.Equal(2, lines.Length);

        TurnRecord? record1 = JsonSerializer.Deserialize<TurnRecord>(lines[0], ReadOptions);
        TurnRecord? record2 = JsonSerializer.Deserialize<TurnRecord>(lines[1], ReadOptions);

        Assert.NotNull(record1);
        Assert.Equal("What is a record in C#?", record1.UserMessage);
        Assert.Equal("A record is an immutable reference type with value-based equality.", record1.FinalAssistantMessage);
        Assert.Equal("fake-model-v1", record1.ModelId);
        Assert.Equal(TurnExitReason.Completed, record1.ExitReason);
        Assert.Equal(100, record1.Usage.InputTokens);
        Assert.Equal(50, record1.Usage.OutputTokens);

        Assert.NotNull(record2);
        Assert.Equal("How does it differ from a class?", record2.UserMessage);
        Assert.Equal("Records generate Equals, GetHashCode, and ToString based on their properties. Classes do not.", record2.FinalAssistantMessage);
    }

    [Fact]
    public async Task SessionLogger_EachLine_IsIndependentlyParseable()
    {
        Turn turn = BuildFakeTurn("Hello", "Hi there!", "fake-model-v1");
        await SessionLogger.AppendAsync(BuildRecord(turn, "Hello"), _logFile);

        string line = (await File.ReadAllLinesAsync(_logFile))[0];

        TurnRecord? record = JsonSerializer.Deserialize<TurnRecord>(line, ReadOptions);
        Assert.NotNull(record);
        Assert.Equal("Hello", record.UserMessage);
    }

    [Fact]
    public async Task SessionLogger_ToolCalls_ArePersisted()
    {
        IReadOnlyList<ToolCallRecord> toolCalls =
        [
            new ToolCallRecord(
                CallId: "call_01",
                Name: "ReadFileTool",
                ArgumentsJson: "{\"path\":\"src/Program.cs\"}",
                Result: "using System;",
                Status: ToolCallStatus.Succeeded
            )
        ];

        Turn turn = BuildFakeTurn("Read the file", "Done.", "fake-model-v1", toolCalls);
        await SessionLogger.AppendAsync(BuildRecord(turn, "Read the file"), _logFile);

        string line = (await File.ReadAllLinesAsync(_logFile))[0];
        TurnRecord? record = JsonSerializer.Deserialize<TurnRecord>(line, ReadOptions);

        Assert.NotNull(record);
        Assert.Single(record.ToolCalls);
        Assert.Equal("ReadFileTool", record.ToolCalls[0].Name);
        Assert.Equal("call_01", record.ToolCalls[0].CallId);
        Assert.Equal(ToolCallStatus.Succeeded, record.ToolCalls[0].Status);
    }
}
