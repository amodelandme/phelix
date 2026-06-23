using System.Text;
using Spectre.Console;

namespace Phelix.Cli;

/// <remarks>
/// Constructed once per turn. Accumulates streamed chunks into a buffer and renders
/// the buffer as markdown at segment boundaries — immediately before a tool call and
/// at the end of the turn. When output is redirected, chunks pass straight through
/// unrendered so piped/captured output stays as the model's original markdown.
/// </remarks>
internal sealed class StreamingMarkdownWriter
{
    readonly StringBuilder buffer = new();
    readonly IAnsiConsole console;
    readonly bool isRedirected;

    internal StreamingMarkdownWriter(IAnsiConsole? console = null)
        : this(console, Console.IsOutputRedirected)
    {
    }

    internal StreamingMarkdownWriter(IAnsiConsole? console, bool isRedirected)
    {
        this.console = console ?? AnsiConsole.Console;
        this.isRedirected = isRedirected;
    }

    /// <summary>
    /// Appends <paramref name="chunk"/> to the buffer, or writes it straight to stdout
    /// when output is redirected.
    /// </summary>
    internal Task Write(string chunk)
    {
        if (isRedirected)
            Console.Write(chunk);
        else
            buffer.Append(chunk);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Renders the accumulated buffer as markdown and clears it. No-op when redirected
    /// or when the buffer is empty.
    /// </summary>
    internal void Flush()
    {
        if (isRedirected || buffer.Length == 0)
            return;

        MarkdownBlockRenderer.Render(buffer.ToString(), console);
        buffer.Clear();
    }
}
