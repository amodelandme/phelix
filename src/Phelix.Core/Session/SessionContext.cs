namespace Phelix.Core.Session;

/// <summary>
/// Immutable identity for a single Phelix process run.
/// </summary>
/// <remarks>
/// Constructed once at startup and passed to every component that needs to write
/// session artifacts. <see cref="FileSlug"/> is the canonical filename component
/// used by both <see cref="SessionLogger"/> and <see cref="SqliteSessionStore"/>.
/// </remarks>
/// <param name="SessionId">Process-lifetime UUID. One value per process.</param>
/// <param name="SessionName">User-supplied name, sanitized. <c>null</c> when the user skipped naming.</param>
/// <param name="StartedAt">UTC timestamp captured at construction.</param>
public sealed record SessionContext(
    string SessionId,
    string? SessionName,
    DateTimeOffset StartedAt)
{
    /// <summary>
    /// Creates a new <see cref="SessionContext"/> with a fresh UUID and the current UTC time.
    /// </summary>
    /// <param name="rawName">
    /// Raw user input for the session name. Sanitized before storage;
    /// pass <c>null</c> or empty to produce an unnamed session.
    /// </param>
    public static SessionContext Create(string? rawName = null) =>
        new(
            SessionId: Guid.NewGuid().ToString("N"),
            SessionName: Sanitize(rawName),
            StartedAt: DateTimeOffset.UtcNow);

    /// <summary>
    /// The filename component shared by both the <c>.jsonl</c> and <c>.db</c> artifacts.
    /// Format: <c>yyyy-MM-dd-&lt;name&gt;-&lt;sessionId&gt;</c> when named,
    /// <c>yyyy-MM-dd-&lt;sessionId&gt;</c> when unnamed.
    /// </summary>
    public string FileSlug =>
        SessionName is not null
            ? $"{StartedAt:yyyy-MM-dd}-{SessionName}-{SessionId}"
            : $"{StartedAt:yyyy-MM-dd}-{SessionId}";

    /// <summary>
    /// Sanitizes raw user input into a filesystem-safe session name slug.
    /// Returns <c>null</c> when the input is empty or produces no valid characters.
    /// </summary>
    /// <remarks>
    /// Rules applied in order:
    /// <list type="number">
    ///   <item>Trim leading/trailing whitespace.</item>
    ///   <item>Replace runs of whitespace with a single hyphen.</item>
    ///   <item>Strip any character that is not a letter, digit, hyphen, or underscore.</item>
    ///   <item>Truncate to 60 characters.</item>
    /// </list>
    /// </remarks>
    public static string? Sanitize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        string trimmed = raw.Trim();

        System.Text.StringBuilder result = new(trimmed.Length);
        bool lastWasHyphen = false;

        foreach (char c in trimmed)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasHyphen)
                {
                    result.Append('-');
                    lastWasHyphen = true;
                }
            }
            else if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
            {
                result.Append(c);
                lastWasHyphen = c == '-';
            }
        }

        string slug = result.ToString().Trim('-');

        if (slug.Length > 60)
            slug = slug[..60].TrimEnd('-');

        return slug.Length == 0 ? null : slug;
    }
}
