using Phelix.Cli;
using Spectre.Console.Testing;

namespace Phelix.Cli.Tests;

public class StreamingMarkdownWriterTests
{
    [Fact]
    public async Task Write_AccumulatesChunksWithoutRendering()
    {
        TestConsole console = new();
        StreamingMarkdownWriter writer = new(console, isRedirected: false);

        await writer.Write("# Hea");
        await writer.Write("ding");

        Assert.Equal(string.Empty, console.Output);
    }

    [Fact]
    public async Task Flush_RendersFullAccumulatedTextAndClearsBuffer()
    {
        TestConsole console = new();
        StreamingMarkdownWriter writer = new(console, isRedirected: false);

        await writer.Write("# Hea");
        await writer.Write("ding");
        writer.Flush();

        Assert.Contains("Heading", console.Output);

        int lengthAfterFirstFlush = console.Output.Length;
        writer.Flush();

        Assert.Equal(lengthAfterFirstFlush, console.Output.Length);
    }

    [Fact]
    public void Flush_OnEmptyBuffer_DoesNothing()
    {
        TestConsole console = new();
        StreamingMarkdownWriter writer = new(console, isRedirected: false);

        writer.Flush();

        Assert.Equal(string.Empty, console.Output);
    }

    [Fact]
    public async Task Write_WhenRedirected_WritesChunksStraightThroughWithoutRendering()
    {
        TestConsole console = new();
        StreamingMarkdownWriter writer = new(console, isRedirected: true);

        TextWriter originalOut = Console.Out;
        StringWriter capturedOut = new();
        Console.SetOut(capturedOut);

        try
        {
            await writer.Write("# Heading");
            writer.Flush();
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Equal("# Heading", capturedOut.ToString());
        Assert.Equal(string.Empty, console.Output);
    }
}
