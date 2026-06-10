using Microsoft.Extensions.AI;
using Phelix.Core.Session;

namespace Phelix.Core.Tests.Session;

public class TokenThresholdPolicyTests
{
    // Each character in a TextContent contributes 1/4 of a token (chars ÷ 4).
    // A string of length N has N/4 estimated tokens (integer division).

    static ChatMessage MessageWithChars(int characterCount) =>
        new(ChatRole.User, new string('x', characterCount));

    [Fact]
    public void ShouldCompact_ReturnsFalse_WhenBelowThreshold()
    {
        TokenThresholdPolicy policy = new(thresholdTokens: 100);

        // 380 characters → 95 estimated tokens, below threshold of 100
        IReadOnlyList<ChatMessage> history = [MessageWithChars(380)];

        Assert.False(policy.ShouldCompact(history));
    }

    [Fact]
    public void ShouldCompact_ReturnsTrue_WhenAtThreshold()
    {
        TokenThresholdPolicy policy = new(thresholdTokens: 100);

        // 400 characters → exactly 100 estimated tokens, meets threshold
        IReadOnlyList<ChatMessage> history = [MessageWithChars(400)];

        Assert.True(policy.ShouldCompact(history));
    }

    [Fact]
    public void ShouldCompact_ReturnsTrue_WhenAboveThreshold()
    {
        TokenThresholdPolicy policy = new(thresholdTokens: 100);

        // 420 characters → 105 estimated tokens, above threshold
        IReadOnlyList<ChatMessage> history = [MessageWithChars(420)];

        Assert.True(policy.ShouldCompact(history));
    }

    [Fact]
    public void ShouldCompact_ReturnsFalse_OnEmptyHistory()
    {
        TokenThresholdPolicy policy = new(thresholdTokens: 100);

        Assert.False(policy.ShouldCompact([]));
    }

    [Fact]
    public void ShouldCompact_SumsAcrossAllMessages()
    {
        TokenThresholdPolicy policy = new(thresholdTokens: 100);

        // Three messages, each a multiple of 4: 160 + 160 + 80 = 400 chars → 100 estimated tokens
        // Using multiples of 4 avoids per-message integer-division truncation skewing the sum.
        IReadOnlyList<ChatMessage> history =
        [
            MessageWithChars(160),
            MessageWithChars(160),
            MessageWithChars(80),
        ];

        Assert.True(policy.ShouldCompact(history));
    }
}
