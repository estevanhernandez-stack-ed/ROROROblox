using System.IO;
using System.Net.Http;
using ROROROblox.App.Distribution;
using ROROROblox.App.Plugins;
using ROROROblox.App.Plugins.Adapters;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// F-099. A recovered failure that is invisible is a failure the user cannot act on.
/// <para>
/// The register recorded the symptom as "the Plugins window shows the surviving plugins and says
/// nothing about the one that was dropped." Re-measured against the tree on 2026-08-20, it was
/// WORSE than that: only the host's <c>InstalledPluginsLookupAdapter</c> deduplicated. The window
/// went through <c>PluginRegistry.ScanAsync</c> and <c>MarketplacePlan.Build</c>, neither of which
/// deduplicates, so it drew the plugin TWICE — two identical rows, same name, same version, each
/// offering Launch, Revoke and Restart, with nothing marking which copy the host had actually
/// loaded. Pressing a button on the wrong row acted on a folder the host had already discarded.
/// </para>
/// <para>
/// So the fix is not only presentation, as the row assumed. The keep-the-first rule became shared
/// (<see cref="PluginDuplicates"/>) so the window and the host cannot disagree, and the dropped
/// copies are named where the user can see them.
/// </para>
/// </summary>
public class PluginDuplicateVisibilityTests : IDisposable
{
    private const string PluginId = "626labs.fake";

    private readonly string _tempRoot;
    private readonly string _pluginsRoot;
    private readonly ConsentStore _consentStore;
    private readonly PluginRegistry _registry;
    private readonly InstalledPluginsLookupAdapter _adapter;

    public PluginDuplicateVisibilityTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"ROROROblox-dupvis-{Guid.NewGuid():N}");
        _pluginsRoot = Path.Combine(_tempRoot, "plugins");
        Directory.CreateDirectory(_pluginsRoot);
        _consentStore = new ConsentStore(Path.Combine(_tempRoot, "consent.dat"));
        _registry = new PluginRegistry(_pluginsRoot, _consentStore);
        _adapter = new InstalledPluginsLookupAdapter(_registry);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    private string WriteFolder(string folder, string id = PluginId)
    {
        var dir = Path.Combine(_pluginsRoot, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"),
            $$"""
            {"schemaVersion":1,"id":"{{id}}","name":"Fake Plugin","version":"0.1.0",
             "contractVersion":"1.0","publisher":"626 Labs","description":"x","capabilities":[]}
            """);
        return dir;
    }

    private PluginsViewModel BuildVm() =>
        new(_registry, _adapter, _consentStore,
            new PluginInstaller(new HttpClient(), _pluginsRoot, (_, _) => Task.CompletedTask, new Version(1, 21, 0, 0)),
            new PluginProcessSupervisor(new NoopStarter()),
            _ => Task.FromResult<IReadOnlyList<string>?>(null),
            // Packaged: the catalog is never fetched (policy 10.2.2), which keeps this off the network.
            new FakeDistributionMode(isPackaged: true),
            new PluginCatalogClient(_ => Task.FromResult("[]")),
            new Version(1, 21, 0, 0));

    [Fact]
    public async Task TheShippedDefect_TwoFoldersDrewTwoIdenticalRows()
    {
        WriteFolder("ur-task");
        WriteFolder("ur-task-backup");

        var vm = BuildVm();
        await vm.LoadAsync();

        // Was 2 before F-099: indistinguishable rows, one of them acting on a discarded folder.
        Assert.Single(vm.Plugins);
    }

    [Fact]
    public async Task TheIgnoredCopyIsNamedWhereTheUserCanSeeIt()
    {
        var a = WriteFolder("ur-task");
        var b = WriteFolder("ur-task-backup");

        var vm = BuildVm();
        await vm.LoadAsync();

        var warning = vm.DuplicateWarning;
        Assert.NotNull(warning);
        Assert.Contains(PluginId, warning!, StringComparison.Ordinal);
        // Both paths, because "delete the unused copy" is not actionable without knowing which one.
        Assert.Contains(a, warning, StringComparison.Ordinal);
        Assert.Contains(b, warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOrdinaryPluginsRootSaysNothing()
    {
        WriteFolder("ur-task");

        var vm = BuildVm();
        await vm.LoadAsync();

        Assert.Null(vm.DuplicateWarning);
        Assert.Single(vm.Plugins);
    }

    [Fact]
    public async Task TheWarningClearsWhenTheDuplicateIsDeleted()
    {
        WriteFolder("ur-task");
        var backup = WriteFolder("ur-task-backup");
        var vm = BuildVm();
        await vm.LoadAsync();
        Assert.NotNull(vm.DuplicateWarning);

        Directory.Delete(backup, recursive: true);
        await vm.LoadAsync();

        // A standing description of the plugins root has to stop standing when the root changes,
        // or it becomes the next warning nobody believes.
        Assert.Null(vm.DuplicateWarning);
    }

    [Fact]
    public void TheWindowAndTheHostKeepTheSameCopy()
    {
        // The point of sharing the rule. If these two ever disagree, the window offers Stop on a
        // process the host never started.
        WriteFolder("ur-task");
        WriteFolder("ur-task-backup");

        var scanned = _registry.ScanAsync().GetAwaiter().GetResult();
        var (kept, dropped) = PluginDuplicates.Resolve(scanned);

        _adapter.Refresh();
        var hostCopy = _adapter.FindById(PluginId);

        Assert.Single(kept);
        Assert.Single(dropped);
        Assert.NotNull(hostCopy);
        Assert.Equal(kept[0].InstallDir, hostCopy!.InstallDir);
    }

    [Fact]
    public void ThreeFoldersDropTwoAndSayHowMany()
    {
        WriteFolder("a");
        WriteFolder("b");
        WriteFolder("c");

        var (kept, dropped) = PluginDuplicates.Resolve(
            _registry.ScanAsync().GetAwaiter().GetResult());

        Assert.Single(kept);
        Assert.Equal(2, dropped.Count);
        Assert.Contains("2 plugin folders", PluginDuplicates.Describe(dropped), StringComparison.Ordinal);
    }

    [Fact]
    public void DistinctIdsAreNotDuplicates()
    {
        WriteFolder("ur-task", "626labs.ur-task");
        WriteFolder("auto-keys", "626labs.auto-keys");

        var (kept, dropped) = PluginDuplicates.Resolve(
            _registry.ScanAsync().GetAwaiter().GetResult());

        Assert.Equal(2, kept.Count);
        Assert.Empty(dropped);
        Assert.Equal("", PluginDuplicates.Describe(dropped));
    }

    private sealed class NoopStarter : IPluginProcessStarter
    {
        public event Action<int>? ProcessExited { add { } remove { } }
        public int Start(string pluginId, string exePath) => 1;
        public void Kill(int pid) { }
        public IReadOnlyList<int> FindRunningUnder(string dirPath) => [];
    }

    private sealed class FakeDistributionMode(bool isPackaged) : IDistributionMode
    {
        public bool IsPackaged { get; } = isPackaged;
    }
}
