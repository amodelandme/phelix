using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Spectre.Console;
using MarkdigTable = Markdig.Extensions.Tables.Table;
using MarkdigTableCell = Markdig.Extensions.Tables.TableCell;
using MarkdigTableRow = Markdig.Extensions.Tables.TableRow;

namespace Phelix.Cli;

/// <remarks>
/// Stateless. Each call parses a complete markdown string and renders it to
/// <paramref name="console"/> in document order. Tables and fenced code blocks are
/// emitted as standalone Spectre <see cref="Spectre.Console.Rendering.IRenderable"/>
/// objects because they do not compose with Spectre's inline markup-string style.
/// </remarks>
internal static class MarkdownBlockRenderer
{
    static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .Build();

    /// <summary>
    /// Parses <paramref name="markdown"/> and renders each block to <paramref name="console"/>.
    /// </summary>
    /// <param name="markdown">A complete, self-contained chunk of markdown text.</param>
    /// <param name="console">The console to render to.</param>
    internal static void Render(string markdown, IAnsiConsole console)
    {
        MarkdownDocument document = Markdown.Parse(markdown, Pipeline);

        foreach (Block block in document)
            RenderBlock(block, console);
    }

    static void RenderBlock(Block block, IAnsiConsole console)
    {
        switch (block)
        {
            case HeadingBlock heading:
                console.MarkupLine($"[bold #a78bfa]{InlinesToMarkup(heading.Inline)}[/]");
                break;

            case MarkdigTable table:
                console.Write(BuildTable(table));
                break;

            case FencedCodeBlock fenced:
                console.Write(new Panel(Markup.Escape(fenced.Lines.ToString())).BorderColor(Color.Grey));
                break;

            case CodeBlock code:
                console.Write(new Panel(Markup.Escape(code.Lines.ToString())).BorderColor(Color.Grey));
                break;

            case ThematicBreakBlock:
                console.Write(new Rule().RuleStyle("grey dim"));
                break;

            case ListBlock list:
                RenderList(list, console, depth: 0);
                break;

            case ParagraphBlock paragraph:
                console.MarkupLine(InlinesToMarkup(paragraph.Inline));
                break;

            case ContainerBlock container:
                foreach (Block child in container)
                    RenderBlock(child, console);
                break;
        }
    }

    static void RenderList(ListBlock list, IAnsiConsole console, int depth)
    {
        string indent = new string(' ', depth * 2);
        int ordinal = list.OrderedStart is not null && int.TryParse(list.OrderedStart, out int start) ? start : 1;

        foreach (Block item in list)
        {
            if (item is not ListItemBlock listItem)
                continue;

            string bullet = list.IsOrdered ? $"{ordinal}." : "[#a78bfa]•[/]";
            ordinal++;

            foreach (Block child in listItem)
            {
                if (child is ParagraphBlock paragraph)
                {
                    console.MarkupLine($"{indent}{bullet} {InlinesToMarkup(paragraph.Inline)}");
                }
                else if (child is ListBlock nested)
                {
                    RenderList(nested, console, depth + 1);
                }
                else
                {
                    RenderBlock(child, console);
                }
            }
        }
    }

    static Spectre.Console.Table BuildTable(MarkdigTable table)
    {
        Spectre.Console.Table spectreTable = new Spectre.Console.Table().Border(TableBorder.Rounded);

        bool headerProcessed = false;

        foreach (MarkdigTableRow row in table)
        {
            if (!headerProcessed && row.IsHeader)
            {
                foreach (MarkdigTableCell cell in row)
                    spectreTable.AddColumn(InlinesToMarkup(GetCellInline(cell)));

                headerProcessed = true;
                continue;
            }

            string[] cellValues = row.Select(c => InlinesToMarkup(GetCellInline((MarkdigTableCell)c))).ToArray();
            spectreTable.AddRow(cellValues);
        }

        return spectreTable;
    }

    static ContainerInline? GetCellInline(MarkdigTableCell cell)
    {
        foreach (Block block in cell)
        {
            if (block is ParagraphBlock paragraph)
                return paragraph.Inline;
        }

        return null;
    }

    static string InlinesToMarkup(ContainerInline? inlines)
    {
        if (inlines is null)
            return string.Empty;

        StringBuilder builder = new();

        for (Inline? inline = inlines.FirstChild; inline is not null; inline = inline.NextSibling)
            builder.Append(InlineToMarkup(inline));

        return builder.ToString();
    }

    static string InlineToMarkup(Inline inline)
    {
        switch (inline)
        {
            case LiteralInline literal:
                return Markup.Escape(literal.Content.ToString());

            case EmphasisInline emphasis:
                string style = emphasis.DelimiterCount >= 2 ? "bold" : "italic";
                return $"[{style}]{InlinesToMarkup(emphasis)}[/]";

            case CodeInline code:
                return $"[white on grey23]{Markup.Escape(code.Content)}[/]";

            case LinkInline link:
                string label = InlinesToMarkup(link);
                return $"[link={Markup.Escape(link.Url ?? string.Empty)}]{label}[/]";

            case LineBreakInline:
                return "\n";

            case ContainerInline container:
                return InlinesToMarkup(container);

            default:
                return string.Empty;
        }
    }
}
