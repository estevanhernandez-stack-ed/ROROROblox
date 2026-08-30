using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using ROROROblox.App.Discord;
using ROROROblox.App.Startup;
using Windows.ApplicationModel;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// Fences for the packaged-activation design (spec 2026-08-30-packaged-activation-design.md).
/// On MSIX installs the registry paths are virtualized into the package hive, so the manifest is
/// the ONLY thing standing between a Store user and silently dead run-on-login / Join-by-URI.
/// These tests pin the manifest declarations to the constants the code actually uses — the drift
/// they prevent (rename a scheme or the task id in code, forget the manifest) would otherwise
/// ship green and fail only on an installed package.
/// </summary>
public sealed class PackagedActivationManifestTests
{
    private static readonly XNamespace Uap10 =
        "http://schemas.microsoft.com/appx/manifest/uap/windows10/10";

    private static readonly XNamespace Desktop =
        "http://schemas.microsoft.com/appx/manifest/desktop/windows10";

    [Fact]
    public void Startup_task_is_declared_disabled_under_the_task_id_the_code_toggles()
    {
        var task = LoadManifest().Descendants(Desktop + "StartupTask").SingleOrDefault();
        Assert.NotNull(task);
        Assert.Equal(PackagedStartupRegistration.TaskId, (string?)task!.Attribute("TaskId"));

        // Enabled="false": run-on-login stays opt-in on packaged installs, the same default as
        // the unpackaged Run-key path (absent until the user flips the toggle).
        Assert.Equal("false", (string?)task.Attribute("Enabled"));

        var extension = task.Parent;
        Assert.NotNull(extension);
        Assert.Equal("windows.startupTask", (string?)extension!.Attribute("Category"));
    }

    [Fact]
    public void Join_scheme_protocol_is_declared_with_argv_parameters()
    {
        var protocol = FindProtocol(JoinUriScheme.SchemeName);

        // Parameters="%1" makes packaged activation deliver the URI as a plain argv token — the
        // exact shape the unpackaged registry command produces — so JoinUriParser stays the
        // single inbound-URI code path.
        Assert.Equal("%1", (string?)protocol.Attribute("Parameters"));
    }

    [Fact]
    public void Discord_launch_scheme_protocol_matches_the_committed_application_id()
    {
        using var json = JsonDocument.Parse(
            File.ReadAllText(RepoPath("src", "ROROROblox.App", "appsettings.json")));
        var applicationId = json.RootElement
            .GetProperty("Discord").GetProperty("ApplicationId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(applicationId));

        // Discord cold-starts the game via discord-<applicationId>:. Unpackaged installs get
        // that scheme from Lachee's RegisterUriScheme plus our command fixup; packaged installs
        // get it from this manifest declaration alone.
        var protocol = FindProtocol($"discord-{applicationId}");
        Assert.Equal("%1", (string?)protocol.Attribute("Parameters"));
    }

    private static XElement FindProtocol(string name)
    {
        var protocol = LoadManifest().Descendants(Uap10 + "Protocol")
            .SingleOrDefault(p => (string?)p.Attribute("Name") == name);
        Assert.NotNull(protocol);
        return protocol!;
    }

    private static XDocument LoadManifest() =>
        XDocument.Load(RepoPath("src", "ROROROblox.App", "Package.appxmanifest"));

    private static string RepoPath(params string[] segments)
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate the repo root from the test bin directory.");
        return Path.Combine(new[] { root! }.Concat(segments).ToArray());
    }
}

/// <summary>
/// State-to-behavior mapping for <see cref="PackagedStartupRegistration"/> over its test seam.
/// The real WinRT calls require package identity the test host does not have; the live packaged
/// smoke in the spec's §4 covers those.
/// </summary>
public sealed class PackagedStartupRegistrationTests
{
    [Theory]
    [InlineData(StartupTaskState.Enabled, true)]
    [InlineData(StartupTaskState.EnabledByPolicy, true)]
    [InlineData(StartupTaskState.Disabled, false)]
    [InlineData(StartupTaskState.DisabledByUser, false)]
    [InlineData(StartupTaskState.DisabledByPolicy, false)]
    public void IsEnabled_maps_windows_state_to_the_toggle(StartupTaskState state, bool expected)
    {
        var sut = Build(getState: () => state);
        Assert.Equal(expected, sut.IsEnabled());
    }

    [Fact]
    public void Enable_succeeds_silently_when_windows_grants_the_request()
    {
        var sut = Build(requestEnable: () => StartupTaskState.Enabled);
        sut.Enable();
    }

    [Fact]
    public void Enable_names_windows_settings_when_the_user_disabled_the_task_there()
    {
        // DisabledByUser is not programmatically reversible — RequestEnableAsync returns it
        // unchanged. The message must route the user to the one place that can flip it.
        var sut = Build(requestEnable: () => StartupTaskState.DisabledByUser);
        var ex = Assert.Throws<InvalidOperationException>(sut.Enable);
        Assert.Contains("Settings > Apps > Startup", ex.Message);
    }

    [Fact]
    public void Enable_reports_policy_blocks_as_policy()
    {
        var sut = Build(requestEnable: () => StartupTaskState.DisabledByPolicy);
        var ex = Assert.Throws<InvalidOperationException>(sut.Enable);
        Assert.Contains("policy", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Disable_forwards_to_the_windows_task()
    {
        var disabled = false;
        var sut = Build(disable: () => disabled = true);
        sut.Disable();
        Assert.True(disabled);
    }

    private static PackagedStartupRegistration Build(
        Func<StartupTaskState>? getState = null,
        Func<StartupTaskState>? requestEnable = null,
        Action? disable = null)
        => new(
            getState ?? (() => StartupTaskState.Disabled),
            requestEnable ?? (() => StartupTaskState.Disabled),
            disable ?? (() => { }));
}
