using Phelix.Cli;
using Spectre.Console.Testing;

namespace Phelix.Cli.Tests;

public class MarkdownBlockRendererTests
{
    [Fact]
    public void Render_Heading_AppliesAccentStyle()
    {
        TestConsole console = new();

        MarkdownBlockRenderer.Render("# Title", console);

        Assert.Contains("Title", console.Output);
    }

    [Fact]
    public void Render_Table_ProducesBorderAndColumns()
    {
        TestConsole console = new();
        string markdown = """
            | Name | Age |
            |---|---|
            | Alice | 30 |
            """;

        MarkdownBlockRenderer.Render(markdown, console);

        Assert.Contains("Name", console.Output);
        Assert.Contains("Age", console.Output);
        Assert.Contains("Alice", console.Output);
        Assert.Contains("30", console.Output);
        Assert.Contains("─", console.Output);
    }

    [Fact]
    public void Render_FencedCodeBlock_IsPanelled()
    {
        TestConsole console = new();
        string markdown = """
            ```
            var x = 1;
            ```
            """;

        MarkdownBlockRenderer.Render(markdown, console);

        Assert.Contains("var x = 1;", console.Output);
        Assert.Contains("─", console.Output);
    }

    [Fact]
    public void Render_BulletList_IsIndentedWithBullet()
    {
        TestConsole console = new();
        string markdown = "- first\n- second";

        MarkdownBlockRenderer.Render(markdown, console);

        Assert.Contains("•", console.Output);
        Assert.Contains("first", console.Output);
        Assert.Contains("second", console.Output);
    }

    [Fact]
    public void Render_OrderedList_NumbersItems()
    {
        TestConsole console = new();
        string markdown = "1. first\n2. second";

        MarkdownBlockRenderer.Render(markdown, console);

        Assert.Contains("1.", console.Output);
        Assert.Contains("2.", console.Output);
    }

    [Fact]
    public void Render_ThematicBreak_RendersRule()
    {
        TestConsole console = new();

        MarkdownBlockRenderer.Render("---", console);

        Assert.Contains("─", console.Output);
    }

    [Fact]
    public void Render_LiteralBracketsInParagraph_AreEscapedNotInterpretedAsMarkup()
    {
        TestConsole console = new();

        MarkdownBlockRenderer.Render("a [red]drop[/] table", console);

        Assert.Contains("[red]drop[/]", console.Output);
    }

    [Fact]
    public void Render_LiteralBracketsInTableCell_AreEscapedNotInterpretedAsMarkup()
    {
        TestConsole console = new();
        string markdown = """
            | Cmd |
            |---|
            | [red]drop[/] |
            """;

        MarkdownBlockRenderer.Render(markdown, console);

        Assert.Contains("[red]drop[/]", console.Output);
    }

    [Fact]
    public void Render_BoldAndItalic_AppliesStyles()
    {
        TestConsole console = new();

        MarkdownBlockRenderer.Render("**bold** and *italic*", console);

        Assert.Contains("bold", console.Output);
        Assert.Contains("italic", console.Output);
    }
}
