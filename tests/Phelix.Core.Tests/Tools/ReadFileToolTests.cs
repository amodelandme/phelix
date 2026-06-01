using Phelix.Core.Tools;

namespace Phelix.Core.Tests.Tools;

public class ReadFileToolTests : IDisposable
{
    private readonly string _root;

    public ReadFileToolTests()
    {
        _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ReadFileTool Tool() => new(_root);

    private string Write(string fileName, string contents)
    {
        string path = Path.Combine(_root, fileName);
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public async Task ReadFileTool_ExistingFile_ReturnsContents()
    {
        string path = Write("hello.txt", "hello world");
        ReadFileTool tool = Tool();

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["path"] = path },
            CancellationToken.None);

        Assert.Equal("hello world", result);
    }

    [Fact]
    public async Task ReadFileTool_MissingPathParameter_ReturnsError()
    {
        ReadFileTool tool = Tool();

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?>(),
            CancellationToken.None);

        Assert.StartsWith("Error:", result);
        Assert.Contains("path", result);
    }

    [Fact]
    public async Task ReadFileTool_PathOutsideRoot_ReturnsError()
    {
        ReadFileTool tool = Tool();
        string outsidePath = Path.GetTempFileName();

        try
        {
            string result = await tool.ExecuteAsync(
                new Dictionary<string, object?> { ["path"] = outsidePath },
                CancellationToken.None);

            Assert.StartsWith("Error:", result);
            Assert.Contains("outside", result);
        }
        finally
        {
            File.Delete(outsidePath);
        }
    }

    [Fact]
    public async Task ReadFileTool_NonexistentFile_ReturnsError()
    {
        ReadFileTool tool = Tool();
        string missingPath = Path.Combine(_root, "does_not_exist.txt");

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["path"] = missingPath },
            CancellationToken.None);

        Assert.StartsWith("Error:", result);
        Assert.Contains("not found", result);
    }

    [Fact]
    public async Task ReadFileTool_PathTraversalAttempt_ReturnsError()
    {
        ReadFileTool tool = Tool();
        string traversalPath = Path.Combine(_root, "..", "escaped.txt");

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["path"] = traversalPath },
            CancellationToken.None);

        Assert.StartsWith("Error:", result);
        Assert.Contains("outside", result);
    }
}
