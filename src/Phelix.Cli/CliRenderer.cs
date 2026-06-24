using Spectre.Console;
using Phelix.Core.Agent;
using Phelix.Core.Session;

namespace Phelix.Cli;

/// <remarks>
/// Methods are designed to be passed as callbacks to
/// <see cref="Phelix.Core.Agent.TurnCallbacks"/>. Each matches the delegate
/// signature expected by <see cref="Phelix.Core.Agent.AgentLoop"/>.
///
/// Streamed text chunks go through <see cref="StreamingMarkdownWriter"/> instead of
/// this class. Structural elements (tool events, warnings, separators) go through
/// <see cref="AnsiConsole"/> so they can carry colour and style.
/// </remarks>
internal static class CliRenderer
{
    /// <summary>
    /// Writes a dimmed grey line indicating a tool call is about to execute.
    /// </summary>
    /// <param name="toolName">The name of the tool being invoked.</param>
    /// <param name="args">The resolved arguments passed to the tool.</param>
    internal static Task WriteToolStarted(string toolName, IReadOnlyDictionary<string, object?> args)
    {
        string argList = BuildArgList(args);
        AnsiConsole.MarkupLine($"[#a78bfa]  ◆ {Markup.Escape(toolName)}[/][grey dim]{argList}[/]");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Writes a dimmed grey line indicating a tool call has finished.
    /// Uses a checkmark on success and a cross on failure or denial.
    /// </summary>
    /// <param name="toolName">The name of the tool that ran.</param>
    /// <param name="status">The outcome of the tool call.</param>
    /// <param name="duration">Wall-clock time the tool took to execute.</param>
    internal static Task WriteToolCompleted(string toolName, ToolCallStatus status, TimeSpan duration)
    {
        string ms = $"{duration.TotalMilliseconds:0}ms";

        if (status == ToolCallStatus.Succeeded)
            AnsiConsole.MarkupLine($"[#a78bfa]  ✓ {Markup.Escape(toolName)}[/] [grey dim]({ms})[/]");
        else
            AnsiConsole.MarkupLine($"[red]  ✗ {Markup.Escape(toolName)} ({ms})[/]");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Writes a thin grey horizontal rule to visually separate REPL turns.
    /// </summary>
    internal static void WriteTurnSeparator()
    {
        AnsiConsole.Write(new Rule().RuleStyle("#a78bfa dim"));
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Writes a warning message in yellow, visually distinct from normal output.
    /// </summary>
    /// <param name="message">The warning text to display.</param>
    internal static void WriteWarning(string message) =>
        AnsiConsole.MarkupLine($"[yellow]Warning: {Markup.Escape(message)}[/]");

    /// <summary>
    /// Writes an error message in red.
    /// </summary>
    /// <param name="message">The error text to display.</param>
    internal static void WriteError(string message) =>
        AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(message)}[/]");

    /// <summary>
    /// Writes a dimmed label used for startup prompts such as session naming.
    /// </summary>
    /// <param name="label">The label text to display.</param>
    internal static void WritePromptLabel(string label) =>
        AnsiConsole.MarkupLine($"[grey dim]{Markup.Escape(label)}[/]");

    /// <summary>
    /// Formats the argument dictionary as a compact inline string for tool event lines.
    /// Truncates individual values at 60 characters to keep lines readable.
    /// </summary>
    /// <param name="args">The tool arguments to format.</param>
    /// <returns>A space-prefixed string of key=value pairs, or empty string when no args.</returns>
    static string BuildArgList(IReadOnlyDictionary<string, object?> args)
    {
        if (args.Count == 0)
            return string.Empty;

        IEnumerable<string> pairs = args.Select(kvp =>
        {
            string raw   = kvp.Value?.ToString() ?? "null";
            string value = raw.Length > 60 ? raw[..60] + "…" : raw;
            return $"{Markup.Escape(kvp.Key)}={Markup.Escape(value)}";
        });

        return " " + string.Join(" ", pairs);
    }
}
