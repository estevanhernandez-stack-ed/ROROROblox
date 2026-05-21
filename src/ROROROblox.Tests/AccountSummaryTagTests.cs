using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Tag normalization for <see cref="AccountSummary"/> (v1.5.0 free-text tags). Pure VM logic — no
/// store. AddTag trims, drops empty/whitespace, dedupes case-insensitively, caps tag length at 24
/// chars and total count at 8. RemoveTag removes the exact entry. Spec
/// docs/superpowers/specs/2026-05-20-rororo-presence-account-ux-design.md §"Components > 4".
/// </summary>
public class AccountSummaryTagTests
{
    private static AccountSummary NewSummary(IReadOnlyList<string>? tags = null)
    {
        var account = new Account(
            Id: Guid.NewGuid(),
            DisplayName: "TestAlt",
            AvatarUrl: "https://example.com/avatar.png",
            CreatedAt: DateTimeOffset.UtcNow,
            LastLaunchedAt: null,
            Tags: tags);
        return new AccountSummary(account);
    }

    [Fact]
    public void Ctor_NullTags_InitializesEmptyCollection()
    {
        var s = NewSummary(tags: null);
        Assert.NotNull(s.Tags);
        Assert.Empty(s.Tags);
    }

    [Fact]
    public void Ctor_SeedsTagsFromAccount()
    {
        var s = NewSummary(new[] { "PS99", "RCU" });
        Assert.Equal(new[] { "PS99", "RCU" }, s.Tags);
    }

    [Fact]
    public void AddTag_AppendsTrimmedTag()
    {
        var s = NewSummary();
        s.AddTagCommand.Execute("  PS99  ");
        Assert.Equal(new[] { "PS99" }, s.Tags);
    }

    [Fact]
    public void AddTag_IgnoresNullEmptyAndWhitespace()
    {
        var s = NewSummary();
        s.AddTagCommand.Execute(null);
        s.AddTagCommand.Execute("");
        s.AddTagCommand.Execute("   \t ");
        Assert.Empty(s.Tags);
    }

    [Fact]
    public void AddTag_DedupesCaseInsensitively_KeepsFirstSeenCasing()
    {
        var s = NewSummary();
        s.AddTagCommand.Execute("PS99");
        s.AddTagCommand.Execute("ps99");
        s.AddTagCommand.Execute("Ps99");
        Assert.Equal(new[] { "PS99" }, s.Tags);
    }

    [Fact]
    public void AddTag_CapsLengthAt24Chars()
    {
        var s = NewSummary();
        var longTag = new string('x', 40);
        s.AddTagCommand.Execute(longTag);
        Assert.Single(s.Tags);
        Assert.Equal(24, s.Tags[0].Length);
    }

    [Fact]
    public void AddTag_CapsTotalAt8Tags()
    {
        var s = NewSummary();
        for (var i = 0; i < 12; i++)
        {
            s.AddTagCommand.Execute($"tag{i}");
        }
        Assert.Equal(8, s.Tags.Count);
        // First 8 won, the rest were ignored.
        Assert.Equal("tag0", s.Tags[0]);
        Assert.Equal("tag7", s.Tags[7]);
    }

    [Fact]
    public void RemoveTag_RemovesExactEntry()
    {
        var s = NewSummary(new[] { "PS99", "RCU", "PLAZA" });
        s.RemoveTagCommand.Execute("RCU");
        Assert.Equal(new[] { "PS99", "PLAZA" }, s.Tags);
    }

    [Fact]
    public void RemoveTag_UnknownTag_NoOp()
    {
        var s = NewSummary(new[] { "PS99" });
        s.RemoveTagCommand.Execute("NOPE");
        Assert.Equal(new[] { "PS99" }, s.Tags);
    }
}
