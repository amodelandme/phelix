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

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Serializes <paramref name="record"/> and appends it to <paramref name="filePath"/>
    /// as a single JSON line.
    /// </summary>
    /// <param name="record">The completed turn record to log.</param>
    /// <param name="context">
    /// Session identity used to derive the default file path. Ignored when
    /// <paramref name="filePath"/> is supplied explicitly.
    /// </param>
    /// <param name="filePath">
    /// Destination <c>.jsonl</c> file. When <c>null</c>, the path is derived from
    /// <paramref name="context"/>. Pass an explicit path in tests.
    /// </param>
    /// <param name="cancellationToken">Propagates cancellation to the file write.</param>
    public static async Task AppendAsync(
        TurnRecord record,
        SessionContext? context = null,
        string? filePath = null,
        CancellationToken cancellationToken = default)
    {
        string resolvedPath = filePath ?? DefaultFilePath(context);

        Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath)!);

        string line = JsonSerializer.Serialize(record, JsonOptions);

        await File.AppendAllTextAsync(resolvedPath, line + Environment.NewLine, cancellationToken);
    }

    /// <summary>
    /// Returns a path like <c>~/.phelix/sessions/&lt;fileSlug&gt;.jsonl</c>.
    /// </summary>
    static string DefaultFilePath(SessionContext? context)
    {
        string slug = context?.FileSlug
            ?? DateTimeOffset.UtcNow.ToString("yyyy-MM-dd") + "-" + Guid.NewGuid().ToString("N");

        return Path.Combine(SessionDir, $"{slug}.jsonl");
    }
}
