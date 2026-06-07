namespace Phelix.Core.Context;

/// <summary>
/// Loads an <c>AGENTS.md</c> file from the working directory and composes it
/// with a base system prompt.
/// </summary>
/// <remarks>
/// <c>AGENTS.md</c> is the per-project instruction file for AI agents working inside
/// a repository. When present in the current working directory, its contents are
/// appended to the harness's base system prompt so the model receives both the
/// harness-level behavioral constraints and the project-specific guidance in one
/// coherent prompt.
///
/// File absence is not an error — the base prompt is returned unchanged.
/// File read failures write a warning to <see cref="Console.Error"/> and fall
/// back to the base prompt; they never throw.
/// </remarks>
public static class AgentsMdLoader
{
    const string AgentsMdFileName = "AGENTS.md";

    /// <summary>
    /// Attempts to read <c>AGENTS.md</c> from <paramref name="workingDirectory"/>
    /// and appends its contents to <paramref name="baseSystemPrompt"/>.
    /// </summary>
    /// <param name="baseSystemPrompt">
    /// The harness-level system prompt from config. Always present in the result.
    /// </param>
    /// <param name="workingDirectory">
    /// The directory to search for <c>AGENTS.md</c>. Typically
    /// <see cref="Directory.GetCurrentDirectory"/>.
    /// </param>
    /// <returns>
    /// <paramref name="baseSystemPrompt"/> unchanged when no <c>AGENTS.md</c> is
    /// found; otherwise the base prompt and the file contents joined with
    /// <see cref="BuildComposedPrompt"/>.
    /// </returns>
    public static string Load(string baseSystemPrompt, string workingDirectory)
    {
        string agentsMdPath = Path.Combine(workingDirectory, AgentsMdFileName);

        if (!File.Exists(agentsMdPath))
            return baseSystemPrompt;

        try
        {
            string agentsMdContent = File.ReadAllText(agentsMdPath);
            return BuildComposedPrompt(baseSystemPrompt, agentsMdContent);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Warning: could not read '{agentsMdPath}': {ex.Message}");
            return baseSystemPrompt;
        }
    }

    /// <summary>
    /// Joins the base system prompt and project context into a single string
    /// using XML-tagged sections to keep the two sources clearly delimited for the model.
    /// </summary>
    /// <param name="baseSystemPrompt">The harness-level behavioral baseline.</param>
    /// <param name="agentsMdContent">The raw contents of the <c>AGENTS.md</c> file.</param>
    /// <returns>The composed prompt string.</returns>
    public static string BuildComposedPrompt(string baseSystemPrompt, string agentsMdContent) =>
        $"""
        <system>
        {baseSystemPrompt}
        </system>

        <project-context>
        {agentsMdContent}
        </project-context>
        """;
}
