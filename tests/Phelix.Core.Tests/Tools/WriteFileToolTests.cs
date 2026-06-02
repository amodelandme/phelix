using Phelix.Core.Tools;

namespace Phelix.Core.Tests.Tools;

public class WriteFileToolTests : IDisposable
{
    private readonly string _root;

    public WriteFileToolTests()
    {
        _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private WriteFileTool Tool() => new(_root);

    [Fact]
    public async Task WriteFileTool_NewFile_WritesContentsAndReturnsOk()
    {
        string path = Path.Combine(_root, "output.txt");
        WriteFileTool tool = Tool();

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["path"] = path, ["content"] = "hello" },
            CancellationToken.None);

        Assert.Equal("OK", result);
        Assert.Equal("hello", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task WriteFileTool_ExistingFile_Overwrites()
    {
        string path = Path.Combine(_root, "existing.txt");
        await File.WriteAllTextAsync(path, "old content");
        WriteFileTool tool = Tool();

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["path"] = path, ["content"] = "new content" },
            CancellationToken.None);

        Assert.Equal("OK", result);
        Assert.Equal("new content", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task WriteFileTool_MissingIntermediateDirectories_CreatesThemAndSucceeds()
    {
        string path = Path.Combine(_root, "a", "b", "c", "deep.txt");
        WriteFileTool tool = Tool();

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["path"] = path, ["content"] = "deep" },
            CancellationToken.None);

        Assert.Equal("OK", result);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task WriteFileTool_MissingPathParameter_ReturnsError()
    {
        WriteFileTool tool = Tool();

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["content"] = "hello" },
            CancellationToken.None);

        Assert.StartsWith("Error:", result);
        Assert.Contains("path", result);
    }

    [Fact]
    public async Task WriteFileTool_MissingContentParameter_ReturnsError()
    {
        WriteFileTool tool = Tool();
        string path = Path.Combine(_root, "out.txt");

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["path"] = path },
            CancellationToken.None);

        Assert.StartsWith("Error:", result);
        Assert.Contains("content", result);
    }

    [Fact]
    public async Task WriteFileTool_PathOutsideRoot_ReturnsError()
    {
        WriteFileTool tool = Tool();
        string outsidePath = Path.Combine(Path.GetTempPath(), "escaped.txt");

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["path"] = outsidePath, ["content"] = "x" },
            CancellationToken.None);

        Assert.StartsWith("Error:", result);
        Assert.Contains("outside", result);
        Assert.False(File.Exists(outsidePath));
    }

    [Fact]
    public async Task WriteFileTool_PathTraversalAttempt_ReturnsError()
    {
        WriteFileTool tool = Tool();
        string traversalPath = Path.Combine(_root, "..", "escaped.txt");

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["path"] = traversalPath, ["content"] = "x" },
            CancellationToken.None);

        Assert.StartsWith("Error:", result);
        Assert.Contains("outside", result);
    }
}
