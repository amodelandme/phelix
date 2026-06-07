namespace Phelix.Tui;

/// <summary>
/// Renders agent output to the terminal.
/// </summary>
/// <remarks>
/// Methods on this class are designed to be passed as callbacks to
/// <see cref="Phelix.Core.Agent.AgentLoop.RunTurnAsync"/>. Each method
/// matches the <c>Func&lt;string, Task&gt;</c> delegate signature.
/// </remarks>
public static class TerminalRenderer
{
    /// <summary>
    /// Writes a single streamed text chunk to stdout without a trailing newline.
    /// Tokens appear inline as they arrive, producing a live-typing effect.
    /// </summary>
    /// <param name="chunk">The text fragment to write.</param>
    public static Task WriteChunk(string chunk)
    {
        Console.Write(chunk);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Writes a warning message to stdout, visually distinct from normal output.
    /// </summary>
    /// <param name="message">The warning text to display.</param>
    public static void WriteWarning(string message) =>
        Console.WriteLine($"Warning: {message}");
}
