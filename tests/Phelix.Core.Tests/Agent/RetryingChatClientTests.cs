using System.Net;
using Microsoft.Extensions.AI;
using Phelix.Core.Agent;
using Phelix.Core.Config;

namespace Phelix.Core.Tests.Agent;

public class RetryingChatClientTests
{
    static RetryPolicy FastPolicy => new()
    {
        MaxRetries = 3,
        BaseDelay = TimeSpan.FromMilliseconds(1),
        MaxDelay = TimeSpan.FromMilliseconds(10)
    };

    // --- GetResponseAsync ---

    [Fact]
    public async Task GetResponseAsync_SucceedsFirstAttempt_ReturnsResponse()
    {
        ChatResponse expected = MakeResponse("hello");
        FailThenSucceedClient inner = new(failures: 0, expected);
        RetryingChatClient client = new(inner, FastPolicy);

        ChatResponse result = await client.GetResponseAsync([], cancellationToken: CancellationToken.None);

        Assert.Equal("hello", result.Text);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task GetResponseAsync_TransientFailure_RetriesAndSucceeds()
    {
        ChatResponse expected = MakeResponse("ok");
        FailThenSucceedClient inner = new(failures: 2, expected);
        RetryingChatClient client = new(inner, FastPolicy);

        ChatResponse result = await client.GetResponseAsync([], cancellationToken: CancellationToken.None);

        Assert.Equal("ok", result.Text);
        Assert.Equal(3, inner.CallCount);
    }

    [Fact]
    public async Task GetResponseAsync_ExhaustsRetries_Throws()
    {
        FailThenSucceedClient inner = new(failures: 10, MakeResponse("never"));
        RetryingChatClient client = new(inner, FastPolicy);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetResponseAsync([], cancellationToken: CancellationToken.None));

        Assert.Equal(FastPolicy.MaxRetries + 1, inner.CallCount);
    }

    [Fact]
    public async Task GetResponseAsync_NonTransientError_ThrowsImmediately()
    {
        NonTransientFailClient inner = new();
        RetryingChatClient client = new(inner, FastPolicy);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetResponseAsync([], cancellationToken: CancellationToken.None));

        Assert.Equal(1, inner.CallCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task GetResponseAsync_HttpTransientStatuses_AreRetried(HttpStatusCode status)
    {
        ChatResponse expected = MakeResponse("ok");
        HttpStatusFailClient inner = new(status, successAfter: 1, expected);
        RetryingChatClient client = new(inner, FastPolicy);

        ChatResponse result = await client.GetResponseAsync([], cancellationToken: CancellationToken.None);

        Assert.Equal("ok", result.Text);
    }

    [Fact]
    public async Task GetResponseAsync_Http400_NotRetried()
    {
        HttpStatusFailClient inner = new(HttpStatusCode.BadRequest, successAfter: 1, MakeResponse("never"));
        RetryingChatClient client = new(inner, FastPolicy);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetResponseAsync([], cancellationToken: CancellationToken.None));

        Assert.Equal(1, inner.CallCount);
    }

    // --- GetStreamingResponseAsync ---

    [Fact]
    public async Task GetStreamingResponseAsync_SucceedsFirstAttempt_YieldsAllUpdates()
    {
        StreamingFailThenSucceedClient inner = new(failures: 0, ["a", "b", "c"]);
        RetryingChatClient client = new(inner, FastPolicy);

        List<ChatResponseUpdate> updates = [];
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync([]))
            updates.Add(update);

        Assert.Equal(3, updates.Count);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_TransientFailure_RetriesAndYieldsUpdates()
    {
        StreamingFailThenSucceedClient inner = new(failures: 2, ["x", "y"]);
        RetryingChatClient client = new(inner, FastPolicy);

        List<ChatResponseUpdate> updates = [];
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync([]))
            updates.Add(update);

        Assert.Equal(2, updates.Count);
        Assert.Equal(3, inner.CallCount);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ExhaustsRetries_Throws()
    {
        StreamingFailThenSucceedClient inner = new(failures: 10, ["never"]);
        RetryingChatClient client = new(inner, FastPolicy);

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync([]))
                _ = update;
        });

        Assert.Equal(FastPolicy.MaxRetries + 1, inner.CallCount);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_NoPartialUpdatesOnRetry()
    {
        // Ensures the caller never sees updates from failed attempts — only from the success.
        StreamingFailThenSucceedClient inner = new(failures: 1, ["final"]);
        RetryingChatClient client = new(inner, FastPolicy);

        List<ChatResponseUpdate> updates = [];
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync([]))
            updates.Add(update);

        Assert.Single(updates);
        Assert.Equal("final", updates[0].Text);
    }

    // --- Helpers ---

    static ChatResponse MakeResponse(string text) =>
        new([new ChatMessage(ChatRole.Assistant, text)]);

    static HttpRequestException Make429() =>
        new("rate limited", null, HttpStatusCode.TooManyRequests);

    sealed class FailThenSucceedClient(int failures, ChatResponse success) : IChatClient
    {
        public int CallCount { get; private set; }
        public ChatClientMetadata Metadata => new("fake", null, null);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (CallCount <= failures)
                throw Make429();
            return Task.FromResult(success);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    sealed class NonTransientFailClient : IChatClient
    {
        public int CallCount { get; private set; }
        public ChatClientMetadata Metadata => new("fake", null, null);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("permanent failure");
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    sealed class HttpStatusFailClient(HttpStatusCode status, int successAfter, ChatResponse success) : IChatClient
    {
        public int CallCount { get; private set; }
        public ChatClientMetadata Metadata => new("fake", null, null);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (CallCount <= successAfter)
                throw new HttpRequestException("error", null, status);
            return Task.FromResult(success);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    sealed class StreamingFailThenSucceedClient(int failures, IReadOnlyList<string> texts) : IChatClient
    {
        public int CallCount { get; private set; }
        public ChatClientMetadata Metadata => new("fake", null, null);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            CallCount++;
            await Task.CompletedTask;

            if (CallCount <= failures)
                throw Make429();

            foreach (string text in texts)
                yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new TextContent(text)] };
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
