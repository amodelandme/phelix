using Phelix.Core.Tools;

namespace Phelix.Core.Tests.Tools;

public class BashToolTests : IDisposable
{
    private readonly string _root;

    public BashToolTests()
    {
        _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private BashTool Tool() => new(_root);

    [Fact]
    public async Task BashTool_SuccessfulCommand_ReturnsExitCodeZeroAndOutput()
    {
        BashTool tool = Tool();

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["command"] = "echo hello" },
            CancellationToken.None);

        Assert.Contains("Exit code: 0", result);
        Assert.Contains("hello", result);
    }

    [Fact]
    public async Task BashTool_FailingCommand_ReturnsNonZeroExitCode()
    {
        BashTool tool = Tool();

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["command"] = "exit 1" },
            CancellationToken.None);

        Assert.Contains("Exit code: 1", result);
    }

    [Fact]
    public async Task BashTool_CommandWritesToStderr_CapturesIt()
    {
        BashTool tool = Tool();

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["command"] = "echo error_output >&2" },
            CancellationToken.None);

        Assert.Contains("error_output", result);
    }

    [Fact]
    public async Task BashTool_WorkingDirectoryRespected_PwdMatchesSubdir()
    {
        string subdir = Path.Combine(_root, "subdir");
        Directory.CreateDirectory(subdir);
        BashTool tool = Tool();

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["command"] = "pwd",
                ["working_directory"] = subdir
            },
            CancellationToken.None);

        Assert.Contains("Exit code: 0", result);
        Assert.Contains("subdir", result);
    }

    [Fact]
    public async Task BashTool_WorkingDirectoryOutsideRoot_ReturnsError()
    {
        BashTool tool = Tool();
        string outsideDir = Path.GetTempPath();

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["command"] = "echo hi",
                ["working_directory"] = outsideDir
            },
            CancellationToken.None);

        Assert.StartsWith("Error:", result);
        Assert.Contains("outside", result);
    }

    [Fact]
    public async Task BashTool_TimeoutExceeded_ReturnsTimeoutError()
    {
        BashTool tool = Tool();

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["command"] = "sleep 10",
                ["timeout_seconds"] = "1"
            },
            CancellationToken.None);

        Assert.StartsWith("Error:", result);
        Assert.Contains("timed out", result);
    }

    [Fact]
    public async Task BashTool_MissingCommandParameter_ReturnsError()
    {
        BashTool tool = Tool();

        string result = await tool.ExecuteAsync(
            new Dictionary<string, object?>(),
            CancellationToken.None);

        Assert.StartsWith("Error:", result);
        Assert.Contains("command", result);
    }
}
