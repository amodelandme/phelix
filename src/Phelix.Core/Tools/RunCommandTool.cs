using System.Diagnostics;
using Microsoft.Extensions.AI;

namespace Phelix.Core.Tools;

/// <summary>
/// Runs a shell command and returns its combined stdout and stderr output.
/// </summary>
/// <remarks>
/// Executes via <c>/bin/sh -c</c>. The working directory defaults to
/// <see cref="RootDirectory"/> and is validated against it before execution.
/// Commands run with the current user's permissions — no sandboxing in MVP.
/// </remarks>
public class RunCommandTool : ITool
{
    const int DefaultTimeoutSeconds = 30;
    const int MaxTimeoutSeconds = 120;

    /// <summary>The directory used as the default working directory and the confinement root.</summary>
    public string RootDirectory { get; }

    /// <inheritdoc/>
    public string Name => "run_command";

    /// <inheritdoc/>
    public string Description => "Runs a shell command and returns its exit code and combined stdout/stderr output. The working directory must be within the allowed root. Commands run as the current user with no sandboxing.";

    /// <param name="rootDirectory">
    /// Absolute path of the default working directory and confinement root.
    /// Defaults to <see cref="Directory.GetCurrentDirectory"/> when <c>null</c>.
    /// </param>
    public RunCommandTool(string? rootDirectory = null) =>
        RootDirectory = Path.GetFullPath(rootDirectory ?? Directory.GetCurrentDirectory());

    /// <inheritdoc/>
    /// <remarks>
    /// Expects a required parameter <c>command</c> (string) and optional parameters
    /// <c>working_directory</c> (string) and <c>timeout_seconds</c> (int).
    /// Returns a block of the form:
    /// <code>
    /// Exit code: 0
    /// ---
    /// &lt;stdout + stderr&gt;
    /// </code>
    /// or a descriptive error string on timeout or invalid arguments.
    /// </remarks>
    public async Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        if (!parameters.TryGetValue("command", out object? rawCommand) || rawCommand is null)
            return "Error: required parameter 'command' is missing.";

        string command = rawCommand.ToString()!;

        string workingDirectory = RootDirectory;
        if (parameters.TryGetValue("working_directory", out object? rawWorkDir) && rawWorkDir is not null)
        {
            string requestedDir = rawWorkDir.ToString()!;
            string absoluteDir;
            try
            {
                absoluteDir = Path.GetFullPath(requestedDir);
            }
            catch (Exception ex)
            {
                return $"Error: could not resolve working_directory '{requestedDir}': {ex.Message}";
            }

            if (!absoluteDir.StartsWith(RootDirectory, StringComparison.Ordinal))
                return $"Error: working_directory '{absoluteDir}' is outside the allowed root '{RootDirectory}'.";

            if (!Directory.Exists(absoluteDir))
                return $"Error: working_directory '{absoluteDir}' does not exist.";

            workingDirectory = absoluteDir;
        }

        int timeoutSeconds = DefaultTimeoutSeconds;
        if (parameters.TryGetValue("timeout_seconds", out object? rawTimeout) && rawTimeout is not null)
        {
            if (!int.TryParse(rawTimeout.ToString(), out int parsed) || parsed < 1)
                return $"Error: timeout_seconds must be a positive integer, got '{rawTimeout}'.";

            timeoutSeconds = Math.Min(parsed, MaxTimeoutSeconds);
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = "/bin/sh",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(command);

        using Process process = new() { StartInfo = startInfo };

        System.Text.StringBuilder output = new();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using CancellationTokenSource timeoutCts = new(TimeSpan.FromSeconds(timeoutSeconds));
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                return $"Error: command timed out after {timeoutSeconds} seconds.";
            }

            return $"Exit code: {process.ExitCode}{Environment.NewLine}---{Environment.NewLine}{output}";
        }
        catch (Exception ex)
        {
            return $"Error: could not start command: {ex.Message}";
        }
    }

    /// <inheritdoc/>
    public AITool ToAITool() =>
        AIFunctionFactory.Create(
            (string command, string? working_directory, int? timeout_seconds, CancellationToken ct) =>
            {
                Dictionary<string, object?> parameters = new()
                {
                    ["command"] = command,
                    ["working_directory"] = working_directory,
                    ["timeout_seconds"] = timeout_seconds?.ToString()
                };
                return ExecuteAsync(parameters, ct);
            },
            Name,
            Description);

}
