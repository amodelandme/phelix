using Phelix.Core.Agent;

namespace Phelix.Core.Tests.Agent;

public class ApprovalGateTests
{
    // --- AutoApproveGate ---

    [Theory]
    [InlineData(ApprovalTier.Auto)]
    [InlineData(ApprovalTier.Prompt)]
    [InlineData(ApprovalTier.Confirm)]
    public async Task AutoApproveGate_ApprovesAllTiers(ApprovalTier tier)
    {
        AutoApproveGate gate = new();

        bool approved = await gate.RequestApprovalAsync("any_tool", tier, "summary", new Dictionary<string, object?>(), CancellationToken.None);

        Assert.True(approved);
    }

    // --- InteractiveApprovalGate: Auto tier ---

    [Theory]
    [InlineData(SessionMode.Default)]
    [InlineData(SessionMode.AcceptsEdits)]
    public async Task InteractiveGate_AutoTier_ApprovesWithoutReading(SessionMode mode)
    {
        StringWriter output = new();
        // Empty input — if the gate reads it, ReadLine returns null which denies
        StringReader input = new(string.Empty);
        InteractiveApprovalGate gate = new(mode, input, output);

        bool approved = await gate.RequestApprovalAsync("read_file", ApprovalTier.Auto, "/some/path", new Dictionary<string, object?>(), CancellationToken.None);

        Assert.True(approved);
        Assert.Empty(output.ToString());
    }

    // --- InteractiveApprovalGate: Prompt tier, Default mode ---

    [Theory]
    [InlineData("y")]
    [InlineData("yes")]
    [InlineData("Y")]
    [InlineData("YES")]
    public async Task InteractiveGate_PromptTier_DefaultMode_ApprovesOnYesVariants(string response)
    {
        InteractiveApprovalGate gate = new(SessionMode.Default, new StringReader(response), new StringWriter());

        bool approved = await gate.RequestApprovalAsync("write_file", ApprovalTier.Prompt, "/some/file.cs", new Dictionary<string, object?>(), CancellationToken.None);

        Assert.True(approved);
    }

    [Theory]
    [InlineData("n")]
    [InlineData("no")]
    [InlineData("")]
    [InlineData("maybe")]
    public async Task InteractiveGate_PromptTier_DefaultMode_DeniesOnNonYesInput(string response)
    {
        InteractiveApprovalGate gate = new(SessionMode.Default, new StringReader(response), new StringWriter());

        bool approved = await gate.RequestApprovalAsync("write_file", ApprovalTier.Prompt, "/some/file.cs", new Dictionary<string, object?>(), CancellationToken.None);

        Assert.False(approved);
    }

    // --- InteractiveApprovalGate: Prompt tier, AcceptsEdits mode ---

    [Fact]
    public async Task InteractiveGate_PromptTier_AcceptsEditsMode_ApprovesWithoutReading()
    {
        StringWriter output = new();
        StringReader input = new(string.Empty);
        InteractiveApprovalGate gate = new(SessionMode.AcceptsEdits, input, output);

        bool approved = await gate.RequestApprovalAsync("write_file", ApprovalTier.Prompt, "/some/file.cs", new Dictionary<string, object?>(), CancellationToken.None);

        Assert.True(approved);
        Assert.Empty(output.ToString());
    }

    // --- InteractiveApprovalGate: Confirm tier ---

    [Theory]
    [InlineData("yes")]
    [InlineData("YES")]
    [InlineData("Yes")]
    public async Task InteractiveGate_ConfirmTier_ApprovesOnFullYes(string response)
    {
        InteractiveApprovalGate gate = new(SessionMode.Default, new StringReader(response), new StringWriter());

        bool approved = await gate.RequestApprovalAsync("bash", ApprovalTier.Confirm, "rm -rf /tmp/scratch", new Dictionary<string, object?>(), CancellationToken.None);

        Assert.True(approved);
    }

    [Theory]
    [InlineData("y")]
    [InlineData("n")]
    [InlineData("")]
    [InlineData("sure")]
    public async Task InteractiveGate_ConfirmTier_DeniesOnAnythingButFullYes(string response)
    {
        InteractiveApprovalGate gate = new(SessionMode.Default, new StringReader(response), new StringWriter());

        bool approved = await gate.RequestApprovalAsync("bash", ApprovalTier.Confirm, "rm -rf /tmp/scratch", new Dictionary<string, object?>(), CancellationToken.None);

        Assert.False(approved);
    }

    [Fact]
    public async Task InteractiveGate_ConfirmTier_AcceptsEditsMode_StillRequiresFullYes()
    {
        InteractiveApprovalGate gate = new(SessionMode.AcceptsEdits, new StringReader("y"), new StringWriter());

        bool approved = await gate.RequestApprovalAsync("bash", ApprovalTier.Confirm, "dotnet build", new Dictionary<string, object?>(), CancellationToken.None);

        Assert.False(approved);
    }

    // --- Prompt output ---

    [Fact]
    public async Task InteractiveGate_PromptTier_WritesToolNameAndSummaryToOutput()
    {
        StringWriter output = new();
        InteractiveApprovalGate gate = new(SessionMode.Default, new StringReader("n"), output);

        await gate.RequestApprovalAsync("write_file", ApprovalTier.Prompt, "/src/Foo.cs", new Dictionary<string, object?>(), CancellationToken.None);

        string printed = output.ToString();
        Assert.Contains("write_file", printed);
        Assert.Contains("/src/Foo.cs", printed);
    }

    [Fact]
    public async Task InteractiveGate_ConfirmTier_ShowsFullYesHintInOutput()
    {
        StringWriter output = new();
        InteractiveApprovalGate gate = new(SessionMode.Default, new StringReader("yes"), output);

        await gate.RequestApprovalAsync("bash", ApprovalTier.Confirm, "make clean", new Dictionary<string, object?>(), CancellationToken.None);

        Assert.Contains("yes", output.ToString());
    }
}
