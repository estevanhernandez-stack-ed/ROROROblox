using ROROROblox.App.Preferences;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests;

/// <summary>
/// The Alerts page admits how many accounts are muted, and unmute-all actually clears both halves.
/// <para>
/// WHAT THIS IS FOR. F-024. Muting is per-account and is done from a row's context menu; the page
/// that owns every other alert decision said nothing about it and offered no way back. The count
/// and the clear are the fix, and both read state that already exists — no new persistence.
/// </para>
/// <para>
/// WHY THERE IS NO WINDOW HERE. This suite constructs no <c>Window</c>, by design (see
/// <c>XamlStyleIntegrityTests</c> for why the repo reads markup rather than instantiating it), so
/// the rules live in <see cref="MutedAccountsSummary"/> and the click handler is three lines of
/// plumbing over them. The one thing a test cannot reach is whether the block is genuinely off
/// screen — so the zero case is asserted on
/// <see cref="MutedAccountsSummary.Summary.Any"/>, which is the property the handler assigns
/// <c>Visibility</c> from, rather than on a rendered control. Seeing it collapse is owed to the
/// manual pass.
/// </para>
/// </summary>
public class MutedAccountsSummaryTests
{
    private static AccountSummary Row(bool muted = false)
    {
        var row = new AccountSummary(new Account(
            Id: Guid.NewGuid(),
            DisplayName: "Alt",
            AvatarUrl: "https://example.com/avatar.png",
            CreatedAt: DateTimeOffset.UtcNow,
            LastLaunchedAt: null,
            RobloxUserId: null));
        row.AlertsMuted = muted;
        return row;
    }

    [Fact]
    public void NoMutedAccounts_ReadsAsAbsence()
    {
        // prd Story 1.3: "Zero muted accounts reads as a clean state, not an empty row or a stray
        // '0'." Any is what the handler assigns Visibility from, and the text is empty as well —
        // a collapsed line holding a stale sentence is one refresh away from showing a wrong number.
        var summary = MutedAccountsSummary.Describe([Row(), Row(), Row()]);

        Assert.Equal(0, summary.Count);
        Assert.False(summary.Any);
        Assert.Equal(string.Empty, summary.Text);
    }

    [Fact]
    public void NoAccountsAtAll_ReadsAsAbsenceToo()
    {
        // An empty roster and a roster with nothing muted are the same state to a reader, and both
        // have to reach the same place — a fresh install opening Settings is the commonest case
        // this block will ever be in.
        var summary = MutedAccountsSummary.Describe([]);

        Assert.Equal(0, summary.Count);
        Assert.False(summary.Any);
    }

    [Fact]
    public void OneMutedAccount_CountsOneAndSpeaksSingular()
    {
        var summary = MutedAccountsSummary.Describe([Row(muted: true), Row(), Row()]);

        Assert.Equal(1, summary.Count);
        Assert.True(summary.Any);
        // "1 accounts are muted" is the wart that ships when a count is interpolated into one
        // sentence. Pinned on the pronoun rather than the whole string so rewording stays free.
        Assert.StartsWith("1 account is muted.", summary.Text, StringComparison.Ordinal);
        Assert.Contains("Nothing it does", summary.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void SeveralMutedAccounts_CountsThemAndSpeaksPlural()
    {
        var summary = MutedAccountsSummary.Describe(
            [Row(muted: true), Row(), Row(muted: true), Row(muted: true), Row()]);

        Assert.Equal(3, summary.Count);
        Assert.True(summary.Any);
        Assert.StartsWith("3 accounts are muted.", summary.Text, StringComparison.Ordinal);
        Assert.Contains("Nothing they do", summary.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void UnmuteAll_ClearsEveryMutedRow()
    {
        var muted = Row(muted: true);
        var alsoMuted = Row(muted: true);
        var neverMuted = Row();
        var rows = new[] { muted, neverMuted, alsoMuted };

        var cleared = MutedAccountsSummary.UnmuteRows(rows);

        Assert.All(rows, row => Assert.False(row.AlertsMuted));
        Assert.Equal(0, MutedAccountsSummary.Describe(rows).Count);
        Assert.False(MutedAccountsSummary.Describe(rows).Any);

        // The return value is the undo for a failed save, so it must be exactly what was turned
        // off. A row that was never muted appearing here would come back muted after a write error
        // — a failure inventing a preference the user never set.
        Assert.Equal(2, cleared.Count);
        Assert.Contains(muted, cleared);
        Assert.Contains(alsoMuted, cleared);
        Assert.DoesNotContain(neverMuted, cleared);
    }

    [Fact]
    public void UnmuteAll_OnNothingMuted_ChangesNothingAndUndoesNothing()
    {
        var rows = new[] { Row(), Row() };

        Assert.Empty(MutedAccountsSummary.UnmuteRows(rows));
        Assert.All(rows, row => Assert.False(row.AlertsMuted));
    }

    [Fact]
    public void UnmuteAll_EmptiesThePersistedIdList()
    {
        var config = new DiscordConfig { MutedAccountIds = [Guid.NewGuid(), Guid.NewGuid()] };

        Assert.Empty(MutedAccountsSummary.WithoutMutes(config).MutedAccountIds);
    }

    [Fact]
    public void UnmuteAll_LeavesEveryOtherDiscordSettingAlone()
    {
        // Same hazard AccountMuteTests.MutingOneRow_LeavesEveryOtherSettingAlone pins one level up:
        // the muted list shares a record with the webhook URLs and the presence toggles, so a clear
        // written as a fresh record silently wipes credentials the user then has to re-enter with
        // no explanation.
        var config = new DiscordConfig
        {
            PresenceEnabled = true,
            JoinEnabled = true,
            MineWebhookUrl = "https://discord.com/api/webhooks/1/tok",
            ClanWebhookUrl = "https://discord.com/api/webhooks/2/tok",
            DroppedOutDestination = AlertDestination.Mine,
            MemoryWarningDestination = AlertDestination.Clan,
            MutedAccountIds = [Guid.NewGuid()],
        };

        var cleared = MutedAccountsSummary.WithoutMutes(config);

        Assert.Empty(cleared.MutedAccountIds);
        Assert.True(cleared.PresenceEnabled);
        Assert.True(cleared.JoinEnabled);
        Assert.Equal("https://discord.com/api/webhooks/1/tok", cleared.MineWebhookUrl);
        Assert.Equal("https://discord.com/api/webhooks/2/tok", cleared.ClanWebhookUrl);
        Assert.Equal(AlertDestination.Mine, cleared.DroppedOutDestination);
        Assert.Equal(AlertDestination.Clan, cleared.MemoryWarningDestination);
    }

    [Fact]
    public void TheCountAndThePersistedListAreReadIndependently()
    {
        // The count deliberately reads the ROWS, not DiscordConfig.MutedAccountIds. An id whose
        // account was removed while muted stays in that list forever — nothing prunes it — so a
        // count taken from the file would report accounts the user does not have and cannot find.
        // Unmute-all still clears the orphan, because "all" means all.
        var orphan = Guid.NewGuid();
        var rows = new[] { Row(muted: true) };
        var config = new DiscordConfig { MutedAccountIds = [orphan, rows[0].Id] };

        Assert.Equal(1, MutedAccountsSummary.Describe(rows).Count);
        Assert.Equal(2, config.MutedAccountIds.Count);
        Assert.Empty(MutedAccountsSummary.WithoutMutes(config).MutedAccountIds);
    }
}
