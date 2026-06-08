using Spectre.Console;
using Spectre.Console.Rendering;

namespace Phelix.Tui;

/// <summary>
/// Translates a <see cref="TuiState"/> into a Spectre.Console renderable tree.
/// </summary>
/// <remarks>
/// <para>
/// Pure static class — no mutable state, no side effects. Given the same
/// <see cref="TuiState"/>, <see cref="Render"/> always returns the same visual output.
/// The consumer loop in <see cref="TuiSession"/> calls this after every state transition
/// and passes the result to <c>ctx.UpdateTarget</c>.
/// </para>
/// <para>
/// All user-controlled strings (tool names, file paths, model output) are passed through
/// <see cref="Markup.Escape"/> before being embedded in markup to prevent Spectre.Console
/// markup injection.
/// </para>
/// </remarks>
public static class TuiRenderer
{
    // ─── Design tokens ─────────────────────────────────────────────────────────
    // Colours match the HTML design mockup palette as closely as terminal colour
    // rendering allows. Spectre.Console uses HTML hex colours when the terminal
    // supports 24-bit colour.

    const string ColorPurple    = "#a593f5";
    const string ColorMuted     = "#8d87a4";
    const string ColorDim       = "#4e4960";
    const string ColorGreen     = "#72c98a";
    const string ColorOrange    = "#e8a46a";
    const string ColorRed       = "#e87272";
    const string ColorBlue      = "#7ab2e8";
    const string ColorBorder    = "#2e2b3a";
    const string ColorText      = "#e2ddf0";

    /// <summary>
    /// Produces the full terminal frame for <paramref name="state"/>.
    /// </summary>
    /// <param name="state">The current TUI state snapshot.</param>
    /// <returns>A <see cref="IRenderable"/> suitable for use with <c>ctx.UpdateTarget</c>.</returns>
    public static IRenderable Render(TuiState state)
    {
        Rows rows = new(
            RenderTopBar(state),
            RenderConversation(state),
            RenderBottomWidget(state),
            RenderBotBar(state));

        return rows;
    }

    // ─── Top bar ───────────────────────────────────────────────────────────────

    static IRenderable RenderTopBar(TuiState state)
    {
        string sessionShort = state.SessionId.Length >= 5
            ? state.SessionId[..5]
            : state.SessionId;

        string left  = $"[bold {ColorPurple}]◆ phelix[/]  [{ColorText}]{Markup.Escape(state.ModelId)}[/]  " +
                       $"[{ColorDim}]· {Markup.Escape(state.Provider)}[/]";
        string right = $"[{ColorDim}]turn {state.TurnNumber + 1}/{state.MaxTurns}[/]  " +
                       $"[{ColorDim}]session {Markup.Escape(sessionShort)}[/]";

        Markup leftMarkup  = new(left);
        Markup rightMarkup = new(right);

        Columns topBar = new(leftMarkup, new Markup(string.Empty), rightMarkup);

        Rule separator = new() { Style = new Style(Color.FromHex(ColorBorder)) };

        return new Rows(topBar, separator);
    }

    // ─── Conversation area ─────────────────────────────────────────────────────

    static IRenderable RenderConversation(TuiState state)
    {
        List<IRenderable> items = new(state.Messages.Length * 3);

        bool isLastMessage(int index) => index == state.Messages.Length - 1;

        for (int messageIndex = 0; messageIndex < state.Messages.Length; messageIndex++)
        {
            DisplayMessage message = state.Messages[messageIndex];

            items.Add(RenderDivider(message.Speaker, message.Timestamp,
                isLast: isLastMessage(messageIndex)));

            foreach (ToolCard card in message.ToolCards)
            {
                bool collapsed = !isLastMessage(messageIndex) || card.Status != ToolCardStatus.Running;
                items.Add(RenderToolCard(card, collapsed));
            }

            if (!string.IsNullOrEmpty(message.Text))
            {
                string textColor = message.Speaker == "You" ? ColorMuted : ColorText;
                items.Add(new Markup($"[{textColor}]{Markup.Escape(message.Text)}[/]"));
                items.Add(new Text(string.Empty));
            }

            if (state.Phase == TuiPhase.Running && isLastMessage(messageIndex) &&
                message.Speaker == "Phelix" && string.IsNullOrEmpty(message.Text))
            {
                items.Add(RenderSpinner());
            }
        }

        if (state.Messages.IsEmpty && state.Phase == TuiPhase.Running)
            items.Add(RenderSpinner());

        return new Rows(items);
    }

    static IRenderable RenderDivider(string speaker, DateTimeOffset timestamp, bool isLast)
    {
        string timeLabel = isLast ? RelativeTime(timestamp) : RelativeTime(timestamp);
        bool isPhelix = speaker == "Phelix";

        string speakerColor = isPhelix ? ColorPurple : ColorMuted;
        string speakerWeight = isPhelix ? "bold " : string.Empty;

        string markup = $"[{speakerWeight}{speakerColor}]{Markup.Escape(speaker)}[/]  " +
                        $"[{ColorDim}]{timeLabel}[/]";

        Rule rule = new(markup)
        {
            Justification = Justify.Left,
            Style = new Style(Color.FromHex(ColorBorder)),
        };

        return rule;
    }

    static IRenderable RenderToolCard(ToolCard card, bool collapsed)
    {
        (string icon, string iconColor) = card.Status switch
        {
            ToolCardStatus.Running => ("◌", ColorOrange),
            ToolCardStatus.Done    => ("✓", ColorGreen),
            ToolCardStatus.Denied  => ("—", ColorDim),
            ToolCardStatus.Failed  => ("✗", ColorRed),
            _                      => ("·", ColorDim),
        };

        string statusLabel = card.Status switch
        {
            ToolCardStatus.Running => "running…",
            ToolCardStatus.Done    => FormatDuration(card.Duration),
            ToolCardStatus.Denied  => "denied",
            ToolCardStatus.Failed  => "failed",
            _                      => string.Empty,
        };

        string borderColor = card.Status == ToolCardStatus.Running ? ColorOrange : ColorBorder;
        string header = $"[{iconColor}]{icon}[/] [{ColorPurple}]{Markup.Escape(card.Name)}[/]  " +
                        $"[{iconColor}]{Markup.Escape(statusLabel)}[/]";

        if (collapsed || card.Args.Count == 0)
        {
            return new Panel(new Markup(header))
            {
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.FromHex(borderColor)),
                Padding = new Padding(1, 0),
            };
        }

        List<IRenderable> body = [new Markup(header), new Text(string.Empty)];

        foreach ((string key, object? value) in card.Args)
        {
            string valueText = value?.ToString() ?? string.Empty;
            body.Add(new Markup(
                $"[{ColorMuted}]{Markup.Escape(key),12}[/]  [{ColorBlue}]{Markup.Escape(valueText)}[/]"));
        }

        return new Panel(new Rows(body))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.FromHex(borderColor)),
            Padding = new Padding(1, 0),
        };
    }

    static IRenderable RenderSpinner()
    {
        return new Markup($"[{ColorMuted}]◌ thinking[/] [{ColorDim}]···[/]");
    }

    // ─── Bottom widget (approval / error / prompt input) ──────────────────────

    static IRenderable RenderBottomWidget(TuiState state) => state.Phase switch
    {
        TuiPhase.AwaitingApproval when state.PendingApproval is not null
            => RenderApprovalPanel(state.PendingApproval),

        TuiPhase.Error when state.ErrorMessage is not null
            => RenderErrorPanel(state.ErrorMessage),

        TuiPhase.Idle
            => RenderPromptInput(state.CurrentInput),

        _ => new Text(string.Empty),
    };

    static IRenderable RenderApprovalPanel(ApprovalRequest approval)
    {
        List<IRenderable> body =
        [
            new Markup($"[{ColorDim}]tool[/]  [{ColorPurple}]{Markup.Escape(approval.ToolName)}[/]"),
            new Text(string.Empty),
        ];

        foreach ((string key, object? value) in approval.Args)
        {
            string valueText = value?.ToString() ?? string.Empty;
            body.Add(new Markup(
                $"[{ColorMuted}]{Markup.Escape(key),12}[/]  [{ColorBlue}]{Markup.Escape(valueText)}[/]"));
        }

        body.Add(new Text(string.Empty));
        body.Add(new Markup(
            $"[{ColorPurple}]y[/] [{ColorMuted}]approve[/]  " +
            $"[{ColorPurple}]n[/] [{ColorMuted}]deny[/]  " +
            $"[{ColorPurple}]esc[/] [{ColorMuted}]cancel[/]"));

        return new Panel(new Rows(body))
        {
            Header = new PanelHeader($"[bold {ColorOrange}]⚠ Tool Approval Required[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.FromHex(ColorOrange)),
            Padding = new Padding(1, 0),
        };
    }

    static IRenderable RenderErrorPanel(string errorMessage)
    {
        List<IRenderable> body =
        [
            new Markup($"[{ColorRed}]{Markup.Escape(errorMessage)}[/]"),
            new Text(string.Empty),
            new Markup(
                $"[{ColorRed}]r[/] [{ColorMuted}]retry[/]  " +
                $"[{ColorRed}]q[/] [{ColorMuted}]quit[/]"),
        ];

        return new Panel(new Rows(body))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.FromHex(ColorRed)),
            Padding = new Padding(1, 0),
        };
    }

    static IRenderable RenderPromptInput(string currentInput)
    {
        string displayText = string.IsNullOrEmpty(currentInput)
            ? $"[{ColorDim}]ask phelix anything…[/] [{ColorPurple}]█[/]"
            : $"[{ColorText}]{Markup.Escape(currentInput)}[/][{ColorPurple}]█[/]";

        return new Markup($"[bold {ColorPurple}]›[/]  {displayText}");
    }

    // ─── Bot bar ───────────────────────────────────────────────────────────────

    static IRenderable RenderBotBar(TuiState state)
    {
        string tokenText = $"[{ColorMuted}]{state.TotalTokens:N0} / 200k tok[/]";

        string toolText = state.Phase == TuiPhase.ToolRunning && state.ActiveTool is not null
            ? $"  [{ColorOrange}]◌ {Markup.Escape(state.ActiveTool.Name)}[/]"
            : string.Empty;

        string keyhints = state.Phase switch
        {
            TuiPhase.Idle             => $"[{ColorDim}]q quit  ? help  ctrl+c cancel[/]",
            TuiPhase.Running          => $"[{ColorDim}]ctrl+c cancel  ? help[/]",
            TuiPhase.ToolRunning      => $"[{ColorDim}]ctrl+c cancel  ? help[/]",
            TuiPhase.AwaitingApproval => $"[{ColorDim}]y approve  n deny  esc cancel[/]",
            TuiPhase.Error            => $"[{ColorDim}]r retry  q quit  ? help[/]",
            _                         => string.Empty,
        };

        Rule separator = new() { Style = new Style(Color.FromHex(ColorBorder)) };
        Markup content = new($"{tokenText}{toolText}  {keyhints}");

        return new Rows(separator, content);
    }

    // ─── Utilities ─────────────────────────────────────────────────────────────

    static string RelativeTime(DateTimeOffset timestamp)
    {
        TimeSpan elapsed = DateTimeOffset.UtcNow - timestamp;

        if (elapsed.TotalSeconds < 60)
            return "now";

        if (elapsed.TotalMinutes < 60)
            return $"{(int)elapsed.TotalMinutes}m ago";

        return $"{(int)elapsed.TotalHours}h ago";
    }

    static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 1)
            return $"{duration.TotalMilliseconds:F0}ms";

        return $"{duration.TotalSeconds:F2}s";
    }
}
