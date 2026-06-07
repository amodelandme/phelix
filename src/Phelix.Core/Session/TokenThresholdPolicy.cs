using Microsoft.Extensions.AI;

namespace Phelix.Core.Session;

/// <summary>
/// Fires compaction when estimated token count of the history meets or exceeds a threshold.
/// </summary>
/// <remarks>
/// Token estimation divides total character count by 4 — the standard heuristic for
/// English prose and mixed code. The estimate is deliberately imprecise; the threshold
/// is set well below the model's hard context limit so the heuristic's rounding error
/// cannot cause a context overflow before compaction fires.
/// <para>
/// The divisor <c>4</c> is an implementation detail of this class. A future policy
/// using a real tokenizer implements <see cref="ICompactionPolicy"/> directly and
/// replaces this class without any change to callers.
/// </para>
/// </remarks>
/// <param name="thresholdTokens">
/// Estimated token count at or above which <see cref="ShouldCompact"/> returns
/// <see langword="true"/>. Sourced from
/// <see cref="AgentOptions.CompactionThresholdTokens"/>; default is <c>40,000</c>.
/// </param>
public sealed class TokenThresholdPolicy(int thresholdTokens) : ICompactionPolicy
{
    const int CharactersPerTokenEstimate = 4;

    /// <inheritdoc/>
    public bool ShouldCompact(IReadOnlyList<ChatMessage> history)
    {
        int estimatedTokens = history.Sum(EstimateTokens);
        return estimatedTokens >= thresholdTokens;
    }

    static int EstimateTokens(ChatMessage message) =>
        message.Contents
            .OfType<TextContent>()
            .Sum(textContent => (textContent.Text?.Length ?? 0) / CharactersPerTokenEstimate);
}
