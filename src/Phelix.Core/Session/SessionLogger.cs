using System.Text.Json;
using Phelix.Core.Agent;

namespace Phelix.Core.Session;

/// <summary>
/// Appends completed turns to a newline-delimited JSON log on disk.
/// </summary>
/// <remarks>
/// One <c>.jsonl</c> file per session, located at <c>~/.phelix/sessions/</c>.
/// Each line is a self-contained <see cref="SessionEntry"/> — parseable independently,
/// no array wrapper needed. The directory is created on first write if absent.
/// </remarks>
public static class SessionLogger
{
    static readonly string SessionDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".phelix", "sessions"
    );

    static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Serializes <paramref name="turn"/> as a <see cref="SessionEntry"/> and appends it
    /// to <paramref name="filePath"/> as a single JSON line.
    /// </summary>
    /// <param name="turn">The completed turn to log.</param>
    /// <param name="userMessage">The user prompt that produced this turn.</param>
    /// <param name="filePath">
    /// Destination <c>.jsonl</c> file. When <c>null</c>, a timestamped file in
    /// <c>~/.phelix/sessions/</c> is used. Pass an explicit path in tests.
    /// </param>
    /// <param name="cancellationToken">Propagates cancellation to the file write.</param>
    public static async Task AppendAsync(
        Turn turn,
        string userMessage,
        string? filePath = null,
        CancellationToken cancellationToken = default)
    {
        string resolvedPath = filePath ?? DefaultFilePath();

        Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath)!);

        SessionEntry entry = SessionEntry.FromTurn(turn, userMessage);
        string line = JsonSerializer.Serialize(entry, JsonOptions);

        await File.AppendAllTextAsync(resolvedPath, line + Environment.NewLine, cancellationToken);
    }

    /// <summary>
    /// Returns a path like <c>~/.phelix/sessions/2026-05-31.jsonl</c>.
    /// One file per calendar day groups sessions naturally without collisions.
    /// </summary>
    static string DefaultFilePath()
    {
        string fileName = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd") + ".jsonl";
        return Path.Combine(SessionDir, fileName);
    }
}
