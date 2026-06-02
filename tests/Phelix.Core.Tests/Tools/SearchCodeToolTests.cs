using Phelix.Core.Tools;

namespace Phelix.Core.Tests.Tools;

public class SearchCodeToolTests : IDisposable
{
    private readonly string _root;

    public SearchCodeToolTests()
    {
        _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private SearchCodeTool Tool() => new(_root);

    private void WriteFile(string relativePath, string contents)
    {
        string fullPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents);
    }

    [Fact]
    public async Task SearchCodeTool_LiteralMatch_ReturnsMatchWithPathAndLineNumber()
    {
        WriteFile("foo.cs", "line one\npublic class Foo {}\nline three");
        SearchCodeTool tool = Tool();

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["pattern"] = "class Foo" },
            CancellationToken.None);

        Assert.Contains("foo.cs", result);
        Assert.Contains(":2:", result);
        Assert.Contains("class Foo", result);
    }

    [Fact]
    public async Task SearchCodeTool_RegexMatch_ReturnsMatchingLines()
    {
        WriteFile("bar.cs", "int x = 1;\nstring y = \"hello\";\nint z = 2;");
        SearchCodeTool tool = Tool();

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["pattern"] = @"int \w+ = \d+",
                ["is_regex"] = "true"
            },
            CancellationToken.None);

        Assert.Contains(":1:", result);
        Assert.Contains(":3:", result);
        Assert.DoesNotContain(":2:", result);
    }

    [Fact]
    public async Task SearchCodeTool_InvalidRegex_ReturnsError()
    {
        SearchCodeTool tool = Tool();

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["pattern"] = "[invalid",
                ["is_regex"] = "true"
            },
            CancellationToken.None);

        Assert.StartsWith("Error:", result);
        Assert.Contains("regex", result);
    }

    [Fact]
    public async Task SearchCodeTool_FileGlobFiltersSearch()
    {
        WriteFile("main.cs", "target string here");
        WriteFile("notes.txt", "target string here");
        SearchCodeTool tool = Tool();

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["pattern"] = "target string",
                ["file_glob"] = "*.cs"
            },
            CancellationToken.None);

        Assert.Contains("main.cs", result);
        Assert.DoesNotContain("notes.txt", result);
    }

    [Fact]
    public async Task SearchCodeTool_NoMatches_ReturnsNoMatchesMessage()
    {
        WriteFile("empty.cs", "nothing interesting here");
        SearchCodeTool tool = Tool();

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["pattern"] = "xyzzy_not_found" },
            CancellationToken.None);

        Assert.Contains("no matches found", result);
    }

    [Fact]
    public async Task SearchCodeTool_TruncatesAtMaxResults()
    {
        string lines = string.Join("\n", Enumerable.Range(0, 10).Select(i => $"match line {i}"));
        WriteFile("big.cs", lines);
        SearchCodeTool tool = Tool();

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["pattern"] = "match line",
                ["max_results"] = "3"
            },
            CancellationToken.None);

        Assert.Contains("truncated", result);
        int matchCount = result.Split('\n').Count(l => l.Contains("match line"));
        Assert.Equal(3, matchCount);
    }

    [Fact]
    public async Task SearchCodeTool_MissingPatternParameter_ReturnsError()
    {
        SearchCodeTool tool = Tool();

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?>(),
            CancellationToken.None);

        Assert.StartsWith("Error:", result);
        Assert.Contains("pattern", result);
    }
}
