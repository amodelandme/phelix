using System.Text.Json;

namespace Phelix.Core.Session;

/// <summary>
/// Appends completed turns to a newline-delimited JSON log on disk.
/// </summary>
/// <remarks>
/// One <c>.jsonl</c> file per session, located at <c>~/.phelix/sessions/</c>.
/// Each line is a self-contained <see cref="TurnRecord"/> — parseable independently,
/// no array wrapper needed. The directory is created on first write if absent.
/// </remarks>
public static class SessionLogger
{
    static readonly string SessionDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".phelix", "sessions"
    );

    /// <summary>
    /// Process-lifetime UUID that identifies this Phelix run. One value per process;
    /// shared across all turns in the session. Used as the file name component and as
    /// the <c>sessionId</c> field in every <see cref="TurnRecord"/> written this run.
    /// </summary>
    public static readonly string SessionId = Guid.NewGuid().ToString("N");

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Serializes <paramref name="record"/> and appends it to <paramref name="filePath"/>
    /// as a single JSON line.
    /// </summary>
    /// <param name="record">The completed turn record to log.</param>
    /// <param name="filePath">
    /// Destination <c>.jsonl</c> file. When <c>null</c>, a timestamped file in
    /// <c>~/.phelix/sessions/</c> is used. Pass an explicit path in tests.
    /// </param>
    /// <param name="cancellationToken">Propagates cancellation to the file write.</param>
    public static async Task AppendAsync(
        TurnRecord record,
        string? filePath = null,
        CancellationToken cancellationToken = default)
    {
        string resolvedPath = filePath ?? DefaultFilePath();

        Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath)!);

        string line = JsonSerializer.Serialize(record, JsonOptions);

        await File.AppendAllTextAsync(resolvedPath, line + Environment.NewLine, cancellationToken);
    }

    /// <summary>
    /// Returns a path like <c>~/.phelix/sessions/2026-05-31-&lt;sessionId&gt;.jsonl</c>.
    /// The session ID is a process-lifetime UUID — one file per run, date-prefixed for sorting.
    /// </summary>
    static string DefaultFilePath()
    {
        string fileName = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd") + $"-{SessionId}.jsonl";
        return Path.Combine(SessionDir, fileName);
    }
}
