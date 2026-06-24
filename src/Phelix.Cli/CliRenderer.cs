using System.Globalization;
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
    /// <summary>The indigo chrome accent, used for labels and structural markers.</summary>
    const string Accent = "#a78bfa";

    /// <summary>A readable mid-grey for secondary detail — far more legible than <c>grey dim</c>.</summary>
    const string Muted = "#9a9ab2";

    /// <summary>The colour for output (generated) token figures.</summary>
    const string OutputAccent = "#5ed3a3";

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
    /// Prints the startup banner: a Figlet "Phelix" wordmark over a rule, followed by the
    /// active model, provider, approval mode, and the REPL hint.
    /// </summary>
    /// <remarks>
    /// Printed once before the REPL loop begins. Not shown on the single-turn path so
    /// piped/scripted output stays clean. The wordmark replaces a plain-text greeting.
    /// </remarks>
    /// <param name="info">The resolved session facts to display.</param>
    internal static void WriteSessionBanner(SessionInfo info)
    {
        AnsiConsole.Write(new FigletText("Phelix").LeftJustified().Color(Color.FromHex(Accent)));
        AnsiConsole.Write(new Rule().RuleStyle($"{Accent} dim"));

        AnsiConsole.MarkupLine(
            $"[{Accent}]  model[/]  [white]{Markup.Escape(info.ModelName)}[/] [{Muted}]({Markup.Escape(info.ModelId)} · {Markup.Escape(info.Provider)})[/]");
        AnsiConsole.MarkupLine(
            $"[{Accent}]  mode[/]   [{Muted}]{Markup.Escape(FormatMode(info.Mode))}[/]");
        AnsiConsole.MarkupLine(
            $"[{Muted}]  type 'exit' to quit, '/status' for session info.[/]");
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Prints the dim per-turn footer: model id, this turn's split token usage, and the
    /// running session total.
    /// </summary>
    /// <remarks>
    /// Skipped when stdout is redirected so piped output stays free of chrome. Renders as
    /// e.g. <c>claude-opus-4-8 · 1.2k↑ 340↓ · session 4.8k</c>.
    /// </remarks>
    /// <param name="modelId">The model that produced this turn's response.</param>
    /// <param name="usage">The turn's aggregated input/output token counts.</param>
    /// <param name="sessionTotalTokens">The cumulative session token total after this turn.</param>
    internal static void WriteTurnFooter(string modelId, UsageSummary usage, int sessionTotalTokens)
    {
        if (Console.IsOutputRedirected)
            return;

        string dot = $"[{Muted}] · [/]";
        string line =
            $"[{Muted}]{Markup.Escape(modelId)}[/]{dot}"
            + $"[{Accent}]{FormatTokenCount(usage.InputTokens)}↑[/] [{OutputAccent}]{FormatTokenCount(usage.OutputTokens)}↓[/]{dot}"
            + $"[{Muted}]session[/] [white]{FormatTokenCount(sessionTotalTokens)}[/]";

        AnsiConsole.MarkupLine(line);
    }

    /// <summary>
    /// Prints the on-demand <c>/status</c> block: model, provider, mode, and session total.
    /// </summary>
    /// <param name="info">The resolved session facts to display.</param>
    /// <param name="sessionTotalTokens">The cumulative session token total so far.</param>
    internal static void WriteStatus(SessionInfo info, int sessionTotalTokens)
    {
        AnsiConsole.MarkupLine(
            $"[{Accent}]  model[/]    [white]{Markup.Escape(info.ModelName)}[/] [{Muted}]({Markup.Escape(info.ModelId)} · {Markup.Escape(info.Provider)})[/]");
        AnsiConsole.MarkupLine(
            $"[{Accent}]  mode[/]     [{Muted}]{Markup.Escape(FormatMode(info.Mode))}[/]");
        AnsiConsole.MarkupLine(
            $"[{Accent}]  session[/]  [white]{FormatTokenCount(sessionTotalTokens)}[/] [{Muted}]tokens[/]");
    }

    /// <summary>
    /// Abbreviates a token count for compact display: exact below 1,000, then <c>N.Nk</c>
    /// and <c>N.NM</c> with a trailing <c>.0</c> trimmed (e.g. <c>340</c>, <c>1.2k</c>,
    /// <c>5k</c>, <c>1.3M</c>).
    /// </summary>
    /// <param name="count">The token count to format. Negative values are clamped to 0.</param>
    /// <returns>The abbreviated string.</returns>
    internal static string FormatTokenCount(int count)
    {
        if (count < 0)
            count = 0;

        if (count < 1_000)
            return count.ToString(CultureInfo.InvariantCulture);

        if (count < 1_000_000)
            return Abbreviate(count / 1_000.0, "k");

        return Abbreviate(count / 1_000_000.0, "M");
    }

    /// <summary>
    /// Rounds <paramref name="value"/> to one decimal, trims a trailing <c>.0</c>, and
    /// appends <paramref name="suffix"/>.
    /// </summary>
    static string Abbreviate(double value, string suffix)
    {
        string text = value.ToString("0.0", CultureInfo.InvariantCulture);

        if (text.EndsWith(".0", StringComparison.Ordinal))
            text = text[..^2];

        return text + suffix;
    }

    /// <summary>
    /// Renders a <see cref="SessionMode"/> as the user-facing label used in the banner
    /// and <c>/status</c> output.
    /// </summary>
    static string FormatMode(SessionMode mode) => mode switch
    {
        SessionMode.Default      => "default",
        SessionMode.AcceptsEdits => "accepts-edits",
        SessionMode.AllowAll     => "allow-all",
        _                        => mode.ToString(),
    };

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
