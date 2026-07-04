using ROROROblox.App.Plugins;

namespace ROROROblox.Tests;

public class MarketplacePlanTests
{
    private static PluginManifest Manifest(string id, string version) => new()
    {
        SchemaVersion = 1,
        Id = id,
        Name = id,
        Version = version,
        ContractVersion = "1.0",
        Publisher = "626 Labs",
        Description = "x",
        Capabilities = [],
    };

    private static InstalledPlugin Installed(string id, string version) => new()
    {
        Manifest = Manifest(id, version),
        InstallDir = @"C:\x",
        Consent = new ConsentRecord { PluginId = id, GrantedCapabilities = [], AutostartEnabled = false },
    };

    private static PluginCatalogEntry Entry(string id, string latest, string? minHost = null) =>
        new(id, id, "d", "626 Labs", null, latest, $"https://github.com/x/{id}/releases/latest/download/", minHost);

    [Fact]
    public void Build_InstalledMatchesCatalogSameVersion_UpToDate()
    {
        var view = MarketplacePlan.Build([Installed("a.b", "1.0.0")], [Entry("a.b", "1.0.0")], new Version(1, 8, 0, 0));

        var iv = Assert.Single(view.Installed);
        Assert.IsType<PluginUpdateState.UpToDate>(iv.Update);
        Assert.Empty(view.Available); // catalog entry is installed → not in Available
    }

    [Fact]
    public void Build_CatalogNewerThanInstalled_UpdateAvailableWithFromTo()
    {
        var view = MarketplacePlan.Build([Installed("a.b", "1.0.0")], [Entry("a.b", "1.3.0")], new Version(1, 8, 0, 0));

        var iv = Assert.Single(view.Installed);
        var upd = Assert.IsType<PluginUpdateState.UpdateAvailable>(iv.Update);
        Assert.Equal("1.0.0", upd.FromVersion);
        Assert.Equal("1.3.0", upd.ToVersion);
        Assert.Equal("https://github.com/x/a.b/releases/latest/download/", iv.UpdateInstallUrl);
    }

    [Fact]
    public void Build_InstalledNotInCatalog_UpToDateNoUrl()
    {
        var view = MarketplacePlan.Build([Installed("a.b", "1.0.0")], [], new Version(1, 8, 0, 0));

        var iv = Assert.Single(view.Installed);
        Assert.IsType<PluginUpdateState.UpToDate>(iv.Update);
        Assert.Null(iv.UpdateInstallUrl);
    }

    [Fact]
    public void Build_CatalogEntryNotInstalled_AppearsAvailable()
    {
        var view = MarketplacePlan.Build([], [Entry("a.b", "1.0.0")], new Version(1, 8, 0, 0));

        var av = Assert.Single(view.Available);
        Assert.Equal("a.b", av.Entry.Id);
        Assert.True(av.Installable);
    }

    [Fact]
    public void Build_AvailableNeedsNewerHost_NotInstallable()
    {
        var view = MarketplacePlan.Build([], [Entry("a.b", "1.0.0", minHost: "2.0.0.0")], new Version(1, 8, 0, 0));

        var av = Assert.Single(view.Available);
        Assert.False(av.Installable);
    }

    [Fact]
    public void Build_PrereleaseTagTolerated_ComparesNumericHead()
    {
        // installed 1.0.0, catalog 1.0.0-beta → same numeric head → up to date (no downgrade/upgrade churn).
        var view = MarketplacePlan.Build([Installed("a.b", "1.0.0")], [Entry("a.b", "1.0.0-beta")], new Version(1, 8, 0, 0));

        Assert.IsType<PluginUpdateState.UpToDate>(Assert.Single(view.Installed).Update);
    }

    [Fact]
    public void Build_UnparseableCatalogVersion_NoUpdateBadge()
    {
        var view = MarketplacePlan.Build([Installed("a.b", "1.0.0")], [Entry("a.b", "garbage")], new Version(1, 8, 0, 0));

        Assert.IsType<PluginUpdateState.UpToDate>(Assert.Single(view.Installed).Update);
    }

    [Fact]
    public void Build_ThreePartVsFourPartSameRelease_UpToDate()
    {
        // "1.0.0" installed vs "1.0.0.0" in the catalog is the SAME release — no spurious badge.
        var view = MarketplacePlan.Build([Installed("a.b", "1.0.0")], [Entry("a.b", "1.0.0.0")], new Version(1, 8, 0, 0));

        Assert.IsType<PluginUpdateState.UpToDate>(Assert.Single(view.Installed).Update);
    }

    [Fact]
    public void Build_CatalogOlderThanInstalled_UpToDate_NoDowngradePrompt()
    {
        var view = MarketplacePlan.Build([Installed("a.b", "1.3.0")], [Entry("a.b", "1.0.0")], new Version(1, 8, 0, 0));

        Assert.IsType<PluginUpdateState.UpToDate>(Assert.Single(view.Installed).Update);
    }

    [Fact]
    public void Build_UnparseableMinHostVersion_FailsOpenInstallable()
    {
        var view = MarketplacePlan.Build([], [Entry("a.b", "1.0.0", minHost: "garbage")], new Version(1, 8, 0, 0));

        Assert.True(Assert.Single(view.Available).Installable);
    }
}
