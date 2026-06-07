using Phelix.Core.Context;

namespace Phelix.Core.Tests.Context;

public class AgentsMdLoaderTests
{
    // --- Load: file absent ---

    [Fact]
    public void Load_WhenNoAgentsMdExists_ReturnsBasePromptUnchanged()
    {
        string tempDir = CreateEmptyTempDirectory();
        string basePrompt = "You are a helpful coding assistant.";

        string result = AgentsMdLoader.Load(basePrompt, tempDir);

        Assert.Equal(basePrompt, result);
    }

    // --- Load: file present ---

    [Fact]
    public void Load_WhenAgentsMdExists_ReturnsComposedPrompt()
    {
        string tempDir = CreateTempDirectoryWithAgentsMd("# Project\nDo not modify legacy/.");
        string basePrompt = "You are a helpful coding assistant.";

        string result = AgentsMdLoader.Load(basePrompt, tempDir);

        Assert.Contains(basePrompt, result);
        Assert.Contains("# Project", result);
        Assert.Contains("Do not modify legacy/.", result);
    }

    [Fact]
    public void Load_WhenAgentsMdExists_BasePromptAppearsBeforeProjectContext()
    {
        string tempDir = CreateTempDirectoryWithAgentsMd("project instructions");
        string basePrompt = "base instructions";

        string result = AgentsMdLoader.Load(basePrompt, tempDir);

        int baseIndex = result.IndexOf("base instructions", StringComparison.Ordinal);
        int projectIndex = result.IndexOf("project instructions", StringComparison.Ordinal);

        Assert.True(baseIndex < projectIndex,
            "Base system prompt must appear before project-context in the composed prompt.");
    }

    // --- BuildComposedPrompt ---

    [Fact]
    public void BuildComposedPrompt_WrapsBaseInSystemTag()
    {
        string composed = AgentsMdLoader.BuildComposedPrompt("base", "project");

        Assert.Contains("<system>", composed);
        Assert.Contains("</system>", composed);
        Assert.Contains("base", composed);
    }

    [Fact]
    public void BuildComposedPrompt_WrapsAgentsMdInProjectContextTag()
    {
        string composed = AgentsMdLoader.BuildComposedPrompt("base", "project");

        Assert.Contains("<project-context>", composed);
        Assert.Contains("</project-context>", composed);
        Assert.Contains("project", composed);
    }

    // --- Load: directory does not exist ---

    [Fact]
    public void Load_WhenWorkingDirectoryDoesNotExist_ReturnsBasePromptUnchanged()
    {
        string nonExistentDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        string basePrompt = "You are a helpful coding assistant.";

        string result = AgentsMdLoader.Load(basePrompt, nonExistentDir);

        Assert.Equal(basePrompt, result);
    }

    // --- helpers ---

    static string CreateEmptyTempDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }

    static string CreateTempDirectoryWithAgentsMd(string content)
    {
        string dir = CreateEmptyTempDirectory();
        File.WriteAllText(Path.Combine(dir, "AGENTS.md"), content);
        return dir;
    }
}
