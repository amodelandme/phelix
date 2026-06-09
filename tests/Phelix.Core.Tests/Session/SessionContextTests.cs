using Phelix.Core.Session;

namespace Phelix.Core.Tests.Session;

public class SessionContextTests
{
    // ── Sanitize ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null,              null)]
    [InlineData("",               null)]
    [InlineData("   ",            null)]
    [InlineData("---",            null)]
    [InlineData("auth rewrite",   "auth-rewrite")]
    [InlineData("  auth rewrite", "auth-rewrite")]
    [InlineData("auth  rewrite",  "auth-rewrite")]
    [InlineData("auth-rewrite",   "auth-rewrite")]
    [InlineData("auth_rewrite",   "auth_rewrite")]
    [InlineData("Auth Rewrite 2", "Auth-Rewrite-2")]
    [InlineData("my/session!",    "mysession")]
    [InlineData("a b  c",         "a-b-c")]
    public void Sanitize_ProducesExpectedSlug(string? input, string? expected)
    {
        string? result = SessionContext.Sanitize(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Sanitize_TruncatesAt60Characters()
    {
        string longInput = new string('a', 80);
        string? result = SessionContext.Sanitize(longInput);

        Assert.NotNull(result);
        Assert.True(result!.Length <= 60);
    }

    [Fact]
    public void Sanitize_TruncatedSlug_DoesNotEndWithHyphen()
    {
        // Construct a string where truncation at 60 would land on a hyphen.
        // "aaa...aaa -extra" — spaces before the cutoff become hyphens,
        // so the truncated result must have trailing hyphens trimmed.
        string input = string.Concat(new string('a', 59), " extra");
        string? result = SessionContext.Sanitize(input);

        Assert.NotNull(result);
        Assert.DoesNotMatch("-$", result);
    }

    // ── FileSlug ──────────────────────────────────────────────────────────────

    [Fact]
    public void FileSlug_WithName_IncludesDateNameAndId()
    {
        DateTimeOffset startedAt = new(2026, 6, 9, 0, 0, 0, TimeSpan.Zero);
        SessionContext ctx = new("abc123", "auth-rewrite", startedAt);

        Assert.Equal("2026-06-09-auth-rewrite-abc123", ctx.FileSlug);
    }

    [Fact]
    public void FileSlug_WithoutName_IncludesDateAndId()
    {
        DateTimeOffset startedAt = new(2026, 6, 9, 0, 0, 0, TimeSpan.Zero);
        SessionContext ctx = new("abc123", null, startedAt);

        Assert.Equal("2026-06-09-abc123", ctx.FileSlug);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_WithName_SanitizesAndPopulatesAllFields()
    {
        SessionContext ctx = SessionContext.Create("my feature");

        Assert.NotNull(ctx.SessionId);
        Assert.Equal("my-feature", ctx.SessionName);
        Assert.True(DateTimeOffset.UtcNow - ctx.StartedAt < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Create_WithNullName_ProducesNullSessionName()
    {
        SessionContext ctx = SessionContext.Create(null);

        Assert.Null(ctx.SessionName);
    }

    [Fact]
    public void Create_TwoCalls_ProduceDifferentSessionIds()
    {
        SessionContext a = SessionContext.Create();
        SessionContext b = SessionContext.Create();

        Assert.NotEqual(a.SessionId, b.SessionId);
    }
}
