using System.IO;
using ROROROblox.App.Plugins;
using ROROROblox.App.Plugins.Adapters;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// Duplicate plugin ids must cost you one folder, never the whole plugin host.
/// <para>
/// Found the hard way on 2026-08-11: a copy of a plugin's folder, made as a backup before an
/// update, put two <c>manifest.json</c> files declaring <c>626labs.ur-task</c> under the plugins
/// root. <c>Refresh</c> built its index with <c>ToDictionary</c>, which threw
/// <c>ArgumentException</c> on the duplicate key — one statement BELOW the catch whose comment
/// promises "a corrupt plugins root must not stop the App from launching". The constructor threw,
/// resolving <c>PluginHostStartupService</c> threw, and the app logged "plugins disabled this
/// session" at <b>Debug</b>. Every plugin gone, no message anywhere a user would look.
/// </para>
/// </summary>
public class InstalledPluginsLookupAdapterTests : IDisposable
{
    private readonly string _root;
    private readonly string _consentPath;

    public InstalledPluginsLookupAdapterTests()
    {
        var b = Path.Combine(Path.GetTempPath(), $"ROROROblox-dup-{Guid.NewGuid():N}");
        _root = Path.Combine(b, "plugins");
        _consentPath = Path.Combine(b, "consent.dat");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        var b = Path.GetDirectoryName(_root)!;
        if (Directory.Exists(b)) Directory.Delete(b, recursive: true);
    }

    private void WritePlugin(string folder, string id)
    {
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), $$"""
        {
          "schemaVersion": 1,
          "id": "{{id}}",
          "name": "Test",
          "version": "1.0.0",
          "contractVersion": "1.0",
          "publisher": "626",
          "description": "x",
          "capabilities": [],
          "entrypoint": "test.exe"
        }
        """);
    }

    private InstalledPluginsLookupAdapter NewAdapter() =>
        new(new PluginRegistry(_root, new ConsentStore(_consentPath)));

    /// <summary>
    /// The regression. Against the old <c>ToDictionary</c> this throws out of the constructor,
    /// which is what took the whole plugin host down.
    /// </summary>
    [Fact]
    public void TwoFoldersWithTheSameId_DoNotThrow_AndDoNotCostTheOtherPlugins()
    {
        WritePlugin("626labs.ur-task", "626labs.ur-task");
        WritePlugin("626labs.ur-task.backup-0.5.0", "626labs.ur-task");
        WritePlugin("626labs.other", "626labs.other");

        var adapter = NewAdapter();

        // The duplicated plugin is still resolvable — one of the two folders wins.
        Assert.NotNull(adapter.FindById("626labs.ur-task"));

        // And the innocent bystander survives, which is the part that actually mattered: the old
        // behaviour lost every plugin, not just the duplicated one.
        Assert.NotNull(adapter.FindById("626labs.other"));
    }

    [Fact]
    public void DuplicateResolution_IsStable_AcrossRefreshes()
    {
        WritePlugin("aaa-first", "626labs.dupe");
        WritePlugin("zzz-second", "626labs.dupe");

        var adapter = NewAdapter();
        var first = adapter.FindById("626labs.dupe")!.InstallDir;

        adapter.Refresh();
        var second = adapter.FindById("626labs.dupe")!.InstallDir;

        // Which folder wins is not specified; that it does not flip between scans is. A plugin
        // whose install directory changes underneath the supervisor is a worse bug than the one
        // this file exists for.
        Assert.Equal(first, second);
    }

    [Fact]
    public void NoDuplicates_StillIndexesEveryPlugin()
    {
        WritePlugin("a", "626labs.a");
        WritePlugin("b", "626labs.b");

        var adapter = NewAdapter();

        Assert.NotNull(adapter.FindById("626labs.a"));
        Assert.NotNull(adapter.FindById("626labs.b"));
        Assert.Null(adapter.FindById("626labs.missing"));
    }
}
