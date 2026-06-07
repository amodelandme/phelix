using Microsoft.Extensions.AI;
using Phelix.Core.Agent;
using Phelix.Core.Tools;

namespace Phelix.Core.Tests.Tools;

public class ToolRegistryTests
{
    private sealed class StubTool(string name) : ITool
    {
        public string Name => name;
        public string Description => "stub";
        public ApprovalTier ApprovalTier => ApprovalTier.Auto;
        public Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
            => Task.FromResult("ok");

        public AITool ToAITool() => AIFunctionFactory.Create(() => "ok", name, "stub");
    }

    [Fact]
    public void Register_ThenTryGet_ReturnsTool()
    {
        ToolRegistry registry = new();
        StubTool tool = new("my_tool");

        registry.Register(tool);

        bool found = registry.TryGet("my_tool", out ITool? result);

        Assert.True(found);
        Assert.Same(tool, result);
    }

    [Fact]
    public void TryGet_UnknownName_ReturnsFalse()
    {
        ToolRegistry registry = new();

        bool found = registry.TryGet("does_not_exist", out ITool? result);

        Assert.False(found);
        Assert.Null(result);
    }

    [Fact]
    public void Register_DuplicateName_ThrowsArgumentException()
    {
        ToolRegistry registry = new();
        registry.Register(new StubTool("dupe"));

        ArgumentException ex = Assert.Throws<ArgumentException>(() => registry.Register(new StubTool("dupe")));

        Assert.Contains("dupe", ex.Message);
    }

    [Fact]
    public void All_ReflectsRegisteredTools()
    {
        ToolRegistry registry = new();
        StubTool a = new("tool_a");
        StubTool b = new("tool_b");

        registry.Register(a);
        registry.Register(b);

        Assert.Equal(2, registry.All.Count);
        Assert.Contains(a, registry.All);
        Assert.Contains(b, registry.All);
    }
}
