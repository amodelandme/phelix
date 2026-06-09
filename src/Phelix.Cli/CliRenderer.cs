namespace Phelix.Cli;

/// <summary>
/// Renders agent output to the terminal for the CLI REPL.
/// </summary>
/// <remarks>
/// Methods are designed to be passed as callbacks to
/// <see cref="Phelix.Core.Agent.TurnCallbacks"/>. Each matches the
/// <c>Func&lt;string, Task&gt;</c> delegate signature expected by <see cref="Phelix.Core.Agent.AgentLoop"/>.
/// </remarks>
internal static class CliRenderer
{
    /// <summary>
    /// Writes a single streamed text chunk to stdout without a trailing newline.
    /// Tokens appear inline as they arrive, producing a live-typing effect.
    /// </summary>
    /// <param name="chunk">The text fragment to write.</param>
    internal static Task WriteChunk(string chunk)
    {
        Console.Write(chunk);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Writes a warning message to stdout, visually distinct from normal output.
    /// </summary>
    /// <param name="message">The warning text to display.</param>
    internal static void WriteWarning(string message) =>
        Console.WriteLine($"Warning: {message}");
}
