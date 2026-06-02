using Phelix.Core.Tools;

namespace Phelix.Core.Tests.Tools;

public class ListFilesToolTests : IDisposable
{
    private readonly string _root;

    public ListFilesToolTests()
    {
        _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ListFilesTool Tool() => new(_root);

    private void CreateFile(string relativePath)
    {
        string fullPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, string.Empty);
    }

    [Fact]
    public async Task ListFilesTool_MatchingFiles_ReturnsOnePathPerLine()
    {
        CreateFile("a.cs");
        CreateFile("b.cs");
        CreateFile("notes.txt");
        ListFilesTool tool = Tool();

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["pattern"] = "*.cs" },
            CancellationToken.None);

        string[] lines = result.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.All(lines, l => Assert.EndsWith(".cs", l));
    }

    [Fact]
    public async Task ListFilesTool_RecursivePattern_FindsNestedFiles()
    {
        CreateFile(Path.Combine("src", "Foo.cs"));
        CreateFile(Path.Combine("src", "sub", "Bar.cs"));
        CreateFile("README.md");
        ListFilesTool tool = Tool();

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["pattern"] = "**/*.cs" },
            CancellationToken.None);

        Assert.Contains("Foo.cs", result);
        Assert.Contains("Bar.cs", result);
        Assert.DoesNotContain("README", result);
    }

    [Fact]
    public async Task ListFilesTool_NoMatches_ReturnsNoMatchesMessage()
    {
        ListFilesTool tool = Tool();

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["pattern"] = "*.xyz" },
            CancellationToken.None);

        Assert.Contains("no files matched", result);
    }

    [Fact]
    public async Task ListFilesTool_TruncatesAtMaxResults()
    {
        for (int i = 0; i < 5; i++)
            CreateFile($"file{i}.txt");

        ListFilesTool tool = Tool();

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["pattern"] = "*.txt", ["max_results"] = "3" },
            CancellationToken.None);

        Assert.Contains("truncated", result);
        Assert.Contains("5 total", result);
    }

    [Fact]
    public async Task ListFilesTool_MissingPatternParameter_ReturnsError()
    {
        ListFilesTool tool = Tool();

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?>(),
            CancellationToken.None);

        Assert.StartsWith("Error:", result);
        Assert.Contains("pattern", result);
    }

    [Fact]
    public async Task ListFilesTool_ResultsAreSortedLexicographically()
    {
        CreateFile("z.txt");
        CreateFile("a.txt");
        CreateFile("m.txt");
        ListFilesTool tool = Tool();

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["pattern"] = "*.txt" },
            CancellationToken.None);

        string[] lines = result.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.True(string.Compare(lines[0], lines[1], StringComparison.Ordinal) < 0);
        Assert.True(string.Compare(lines[1], lines[2], StringComparison.Ordinal) < 0);
    }
}
