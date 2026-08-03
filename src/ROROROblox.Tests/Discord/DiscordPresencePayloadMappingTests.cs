using DiscordRPC;
using ROROROblox.App.Discord.Internal;

namespace ROROROblox.Tests.Discord;

/// <summary>
/// Unit coverage for <see cref="LacheeDiscordRpcClientAdapter.ToRichPresence"/> — the pure
/// payload-to-library mapping extracted from <c>SetPresence</c>. Touches no IPC, so it is the
/// one seam-adjacent path testable without a live Discord pipe. The Join-secret and Party
/// assertions cover the plan's non-negotiable #2: a Join button silently fails to appear if
/// the secret mapping regresses.
/// </summary>
public class DiscordPresencePayloadMappingTests
{
    [Fact]
    public void PayloadWithParty_MapsSecretAndPartyFieldsOntoRichPresence()
    {
        var party = new DiscordPresenceParty(PartyId: "party-123", JoinSecret: "secret-abc", Size: 2, MaxSize: 5);
        var payload = new DiscordPresencePayload(
            State: "In a private server",
            Details: "Clan Coordination",
            StartedAtUtc: null,
            LargeImageKey: null,
            LargeImageText: null,
            Party: party);

        RichPresence result = LacheeDiscordRpcClientAdapter.ToRichPresence(payload);

        Assert.NotNull(result.Secrets);
        Assert.Equal("secret-abc", result.Secrets!.JoinSecret);

        Assert.NotNull(result.Party);
        Assert.Equal("party-123", result.Party!.ID);
        Assert.Equal(2, result.Party.Size);
        Assert.Equal(5, result.Party.Max);
    }

    [Fact]
    public void PayloadWithNullParty_ProducesNoSecretAndNoParty()
    {
        var payload = new DiscordPresencePayload(
            State: "At Roblox home",
            Details: null,
            StartedAtUtc: null,
            LargeImageKey: null,
            LargeImageText: null,
            Party: null);

        RichPresence result = LacheeDiscordRpcClientAdapter.ToRichPresence(payload);

        Assert.Null(result.Secrets);
        Assert.Null(result.Party);
    }

    [Fact]
    public void PayloadFields_MapAcrossUnchanged()
    {
        var startedAt = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
        var payload = new DiscordPresencePayload(
            State: "In Pet Simulator 99",
            Details: "Squad of 3",
            StartedAtUtc: startedAt,
            LargeImageKey: "roblox-icon",
            LargeImageText: "RoRoRo",
            Party: null);

        RichPresence result = LacheeDiscordRpcClientAdapter.ToRichPresence(payload);

        Assert.Equal("In Pet Simulator 99", result.State);
        Assert.Equal("Squad of 3", result.Details);
        Assert.NotNull(result.Timestamps);
        Assert.NotNull(result.Timestamps!.Start);
        Assert.Equal(startedAt.UtcDateTime, result.Timestamps.Start!.Value);
        Assert.Equal("roblox-icon", result.Assets.LargeImageKey);
        Assert.Equal("RoRoRo", result.Assets.LargeImageText);
    }
}
