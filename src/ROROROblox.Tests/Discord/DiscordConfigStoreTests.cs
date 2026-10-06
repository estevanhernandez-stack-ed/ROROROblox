using System.Security.Cryptography;
using System.Text;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

public class DiscordConfigStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rororo-discord-{Guid.NewGuid():N}.dat");

    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }

    [Fact]
    public async Task LoadAsync_NoFile_ReturnsDefaultsWithEverythingOff()
    {
        var store = new DiscordConfigStore(_path);

        var config = await store.LoadAsync();

        // For 806 users the safe default is silence.
        Assert.False(config.PresenceEnabled);
        Assert.False(config.JoinEnabled);
        Assert.Null(config.MineWebhookUrl);
        Assert.Equal(AlertDestination.None, config.DroppedOutDestination);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsAcrossInstances()
    {
        var store = new DiscordConfigStore(_path);
        await store.SaveAsync(new DiscordConfig
        {
            PresenceEnabled = true,
            MineWebhookUrl = "https://discord.com/api/webhooks/1/abc",
            DroppedOutDestination = AlertDestination.Mine,
        });

        var reloaded = await new DiscordConfigStore(_path).LoadAsync();

        Assert.True(reloaded.PresenceEnabled);
        Assert.Equal("https://discord.com/api/webhooks/1/abc", reloaded.MineWebhookUrl);
        Assert.Equal(AlertDestination.Mine, reloaded.DroppedOutDestination);
    }

    [Fact]
    public async Task SavedFile_DoesNotContainTheWebhookUrlInPlaintext()
    {
        // THE test for this task. Writing the JSON unencrypted makes it fail, and that is
        // exactly what the May implementation did.
        var store = new DiscordConfigStore(_path);
        await store.SaveAsync(new DiscordConfig { MineWebhookUrl = "https://discord.com/api/webhooks/1/SECRET_TOKEN" });

        var raw = await File.ReadAllBytesAsync(_path);
        var asText = System.Text.Encoding.UTF8.GetString(raw);

        Assert.DoesNotContain("SECRET_TOKEN", asText, StringComparison.Ordinal);
        Assert.DoesNotContain("webhooks", asText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_AFileWrittenBeforeMetricAlerts_StillGetsTheDesktopDefault()
    {
        // A defaulted property and a deserialized-absent property are not automatically the same
        // thing, and getting that wrong here would ship the exact bug the default was added to
        // fix: every discord.dat on disk was written before MetricBreachDestinations existed, so
        // if System.Text.Json left it empty on upgrade the breach would still route nowhere for
        // everyone who has ever opened Settings. The envelope is hand-rolled rather than produced
        // by SaveAsync because SaveAsync writes every property — it cannot express "absent".
        var beforeTheField = """{"PresenceEnabled":true,"DroppedOutDestination":2}""";
        await File.WriteAllBytesAsync(_path, ProtectedData.Protect(
            Encoding.UTF8.GetBytes(beforeTheField), optionalEntropy: null, DataProtectionScope.CurrentUser));

        var config = await new DiscordConfigStore(_path).LoadAsync();

        // The old fields still read back, so this is a real blob and not a silent defaults return.
        Assert.True(config.PresenceEnabled);
        Assert.Equal(AlertDestination.Mine, config.DroppedOutDestination);
        Assert.Equal(AlertDestination.Local,
            Assert.Single(config.DestinationsFor(AlertKind.MetricBreach)));
    }

    [Fact]
    public async Task LoadAsync_AFileWrittenBeforeAutoRejoinPaused_StillGetsTheDesktopDefault()
    {
        // Same shape as the metric-alerts case above, for the same reason (controller correction,
        // 2026-09-29): every discord.dat on disk predates AutoRejoinPausedDestinations, so if
        // System.Text.Json left it empty on upgrade the pause alert would route nowhere for everyone
        // who has ever opened Settings — exactly the "routed nowhere, on every install, forever" bug
        // MetricBreachDestinations' default was added to fix. The envelope is hand-rolled because
        // SaveAsync writes every property and cannot express "absent".
        var beforeTheField = """{"PresenceEnabled":true,"DroppedOutDestination":2}""";
        await File.WriteAllBytesAsync(_path, ProtectedData.Protect(
            Encoding.UTF8.GetBytes(beforeTheField), optionalEntropy: null, DataProtectionScope.CurrentUser));

        var config = await new DiscordConfigStore(_path).LoadAsync();

        Assert.True(config.PresenceEnabled);
        Assert.Equal(AlertDestination.Mine, config.DroppedOutDestination);
        Assert.Equal(AlertDestination.Local,
            Assert.Single(config.DestinationsFor(AlertKind.AutoRejoinPaused)));
    }

    [Fact]
    public async Task SaveThenLoad_ExplicitEmptyAutoRejoinPausedDestinations_RoundTripsAsEmpty()
    {
        // The other half of that same mechanism: once Settings has saved a config at all, SaveAsync
        // writes EVERY property, so unticking all four boxes writes a PRESENT empty list, not an
        // absent one. System.Text.Json overwrites the initializer with that explicit value on load,
        // the same as any other real value — it must not spring back to the shipped default, or a
        // user who deliberately turned this alert off would find it silently back on.
        var store = new DiscordConfigStore(_path);
        await store.SaveAsync(new DiscordConfig { AutoRejoinPausedDestinations = [] });

        var reloaded = await new DiscordConfigStore(_path).LoadAsync();

        Assert.Empty(reloaded.DestinationsFor(AlertKind.AutoRejoinPaused));
    }

    [Fact]
    public async Task LoadAsync_AFileWrittenBeforeIdleDestinations_StillGetsTheDesktopDefault()
    {
        // The third instance of the same shape, and the one with a user-visible regression behind
        // it (v1.33 item 1): idle alerts have shown a desktop toast since v1.8, through a presenter
        // that read one mute flag and no destinations at all. Moving them onto the router means
        // every discord.dat on disk predates IdleDestinations, so an absent key defaulting to empty
        // would silence a notification the user already has — an upgrade taking a feature away.
        // The envelope is hand-rolled because SaveAsync writes every property and cannot express
        // "absent".
        var beforeTheField = """{"PresenceEnabled":true,"DroppedOutDestination":2}""";
        await File.WriteAllBytesAsync(_path, ProtectedData.Protect(
            Encoding.UTF8.GetBytes(beforeTheField), optionalEntropy: null, DataProtectionScope.CurrentUser));

        var config = await new DiscordConfigStore(_path).LoadAsync();

        Assert.True(config.PresenceEnabled);
        Assert.Equal(AlertDestination.Mine, config.DroppedOutDestination);
        Assert.Equal(AlertDestination.Local,
            Assert.Single(config.DestinationsFor(AlertKind.AccountIdle)));
    }

    [Fact]
    public async Task SaveThenLoad_ExplicitEmptyIdleDestinations_RoundTripsAsEmpty()
    {
        // This half matters more here than it does for the pause kind, because something other
        // than a checkbox writes it: the one-time MuteIdleAlerts migration (item 3) turns an
        // existing `true` into an explicitly empty IdleDestinations. If an empty list sprang back
        // to the shipped default on the next load, every user who had muted idle alerts would get
        // them back, once per restart, forever.
        var store = new DiscordConfigStore(_path);
        await store.SaveAsync(new DiscordConfig { IdleDestinations = [] });

        var reloaded = await new DiscordConfigStore(_path).LoadAsync();

        Assert.Empty(reloaded.DestinationsFor(AlertKind.AccountIdle));
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_ReturnsDefaultsInsteadOfThrowing()
    {
        // A stray or wrong-user file must not break app startup. Same rule as ConsentStore.
        await File.WriteAllTextAsync(_path, "this is not a DPAPI envelope");

        var config = await new DiscordConfigStore(_path).LoadAsync();

        Assert.False(config.PresenceEnabled);
    }
}
