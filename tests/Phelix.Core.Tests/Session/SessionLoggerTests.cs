using System.Text.Json;
using Microsoft.Extensions.AI;
using Phelix.Core.Agent;
using Phelix.Core.Session;

namespace Phelix.Core.Tests.Session;

public class SessionLoggerTests : IDisposable
{
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

    private static Turn BuildFakeTurn(string userText, string assistantText, string modelId)
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
            Timestamp: DateTimeOffset.UtcNow
        );
    }

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

        await SessionLogger.AppendAsync(turn1, turn1.Messages[0].Text!, _logFile);
        await SessionLogger.AppendAsync(turn2, turn2.Messages[0].Text!, _logFile);

        string[] lines = await File.ReadAllLinesAsync(_logFile);

        Assert.Equal(2, lines.Length);

        SessionEntry? entry1 = JsonSerializer.Deserialize<SessionEntry>(lines[0], new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        SessionEntry? entry2 = JsonSerializer.Deserialize<SessionEntry>(lines[1], new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(entry1);
        Assert.Equal("What is a record in C#?", entry1.UserMessage);
        Assert.Equal("A record is an immutable reference type with value-based equality.", entry1.AssistantMessage);
        Assert.Equal("fake-model-v1", entry1.ModelId);

        Assert.NotNull(entry2);
        Assert.Equal("How does it differ from a class?", entry2.UserMessage);
        Assert.Equal("Records generate Equals, GetHashCode, and ToString based on their properties. Classes do not.", entry2.AssistantMessage);
    }

    [Fact]
    public async Task SessionLogger_EachLine_IsIndependentlyParseable()
    {
        Turn turn = BuildFakeTurn("Hello", "Hi there!", "fake-model-v1");
        await SessionLogger.AppendAsync(turn, "Hello", _logFile);

        string line = (await File.ReadAllLinesAsync(_logFile))[0];

        // Each line must parse standalone — no array wrapper
        SessionEntry? entry = JsonSerializer.Deserialize<SessionEntry>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(entry);
        Assert.Equal("Hello", entry.UserMessage);
    }
}
