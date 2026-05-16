using System.ComponentModel;
using ROROROblox.App.Plugins;

namespace ROROROblox.Tests;

/// <summary>
/// PluginRow.IsRunning drives the Launch button's IsEnabled binding in XAML. The
/// button only re-enables when the supervisor's PluginExited fires — which mutates
/// IsRunning without rebuilding the Plugins collection — so per-row INPC has to
/// fire on mutation. Test the contract here; the actual Launch click → spawn path
/// is smoke-tested against a real plugin in the install harness.
/// </summary>
public class PluginRowTests
{
    private static InstalledPlugin MakeInstalled() => new()
    {
        Manifest = new PluginManifest
        {
            SchemaVersion = 1,
            Id = "626labs.test",
            Name = "Test",
            Version = "0.1.0",
            ContractVersion = "1.0",
            Publisher = "626 Labs",
            Description = "x",
            Capabilities = Array.Empty<string>(),
        },
        InstallDir = "C:\\fake",
        Consent = new ConsentRecord
        {
            PluginId = "626labs.test",
            GrantedCapabilities = Array.Empty<string>(),
            AutostartEnabled = false,
        },
    };

    [Fact]
    public void IsRunning_DefaultsToFalse()
    {
        var row = new PluginRow(MakeInstalled());
        Assert.False(row.IsRunning);
    }

    [Fact]
    public void IsRunning_HonorsConstructorFlag()
    {
        var row = new PluginRow(MakeInstalled(), isRunning: true);
        Assert.True(row.IsRunning);
    }

    [Fact]
    public void IsRunning_RaisesPropertyChanged_OnMutation()
    {
        var row = new PluginRow(MakeInstalled());
        var fired = new List<string?>();
        ((INotifyPropertyChanged)row).PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        row.IsRunning = true;

        Assert.Contains(nameof(PluginRow.IsRunning), fired);
    }

    [Fact]
    public void IsRunning_IdempotentSet_DoesNotRaise()
    {
        // Avoid binding-loop chatter when the supervisor reports state we already had.
        var row = new PluginRow(MakeInstalled(), isRunning: true);
        var fired = new List<string?>();
        ((INotifyPropertyChanged)row).PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        row.IsRunning = true;

        Assert.DoesNotContain(nameof(PluginRow.IsRunning), fired);
    }

    [Fact]
    public void IsRunning_FlipBackToFalse_RaisesPropertyChanged()
    {
        // PluginExited path: the supervisor fires after a process exits, and the VM flips
        // the row's IsRunning back to false so the Launch button re-enables. Cover the
        // both-directions transition explicitly.
        var row = new PluginRow(MakeInstalled(), isRunning: true);
        var fired = new List<string?>();
        ((INotifyPropertyChanged)row).PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        row.IsRunning = false;

        Assert.Contains(nameof(PluginRow.IsRunning), fired);
        Assert.False(row.IsRunning);
    }
}
