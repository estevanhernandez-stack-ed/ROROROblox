using Microsoft.Win32;
using ROROROblox.App.Startup;

namespace ROROROblox.Tests.Startup;

/// <summary>
/// The Run-key registration, and specifically the rename left behind by eb579ed (2026-05-06),
/// which renamed the value from "ROROROblox" to "RORORO" with no migration.
///
/// The consequence was not cosmetic: <see cref="StartupRegistration.IsEnabled"/> read only the
/// new name, so the Settings toggle reported OFF while an old value still launched the app at
/// login, and <see cref="StartupRegistration.Disable"/> deleted only the new name, so the toggle
/// could not turn it off either. The state was unreachable from inside the app. Found live on a
/// maintainer's machine 2026-09-09, launching a 1.22 build under a 1.27 install.
///
/// These run against a scratch key rather than the real Run key — writing autostart entries on a
/// developer's machine from a unit test is exactly the defect being fixed.
/// </summary>
public sealed class StartupRegistrationTests : IDisposable
{
    private readonly string _root = $@"Software\ROROROblox.Tests\{Guid.NewGuid():N}";
    private readonly string _keyPath;

    public StartupRegistrationTests() => _keyPath = _root + @"\Run";

    private StartupRegistration Subject() => new(_keyPath);

    private void Seed(string valueName, string value = @"C:\old\path\ROROROblox.App.exe")
    {
        using var key = Registry.CurrentUser.CreateSubKey(_keyPath);
        key.SetValue(valueName, value);
    }

    private string[] ValueNames()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_keyPath);
        return key?.GetValueNames() ?? [];
    }

    [Fact]
    public void IsEnabled_LegacyValueOnly_ReportsEnabled()
    {
        // THE BUG. A user who enabled run-on-login before the rename has only this value, and the
        // toggle told them autostart was off while the app kept launching at login.
        Seed(StartupRegistration.LegacyValueName);

        Assert.True(Subject().IsEnabled());
    }

    [Fact]
    public void IsEnabled_CurrentValueOnly_ReportsEnabled()
    {
        Seed(StartupRegistration.ValueName);

        Assert.True(Subject().IsEnabled());
    }

    [Fact]
    public void IsEnabled_NeitherValue_ReportsDisabled()
    {
        Assert.False(Subject().IsEnabled());
    }

    [Fact]
    public void Disable_LegacyValueOnly_RemovesIt()
    {
        // The other half of the bug: the only control that could clear the orphan was wired to
        // the wrong name, so there was no in-app way out.
        Seed(StartupRegistration.LegacyValueName);

        Subject().Disable();

        Assert.Empty(ValueNames());
    }

    [Fact]
    public void Disable_BothValues_RemovesBoth()
    {
        Seed(StartupRegistration.LegacyValueName);
        Seed(StartupRegistration.ValueName);

        Subject().Disable();

        Assert.Empty(ValueNames());
    }

    [Fact]
    public void Enable_WithLegacyPresent_CollapsesToASingleValue()
    {
        // Enabling must not leave the app launching twice. The legacy value is ours and nobody
        // else's, so folding it into the current name is safe.
        Seed(StartupRegistration.LegacyValueName);

        Subject().Enable();

        Assert.Equal([StartupRegistration.ValueName], ValueNames());
    }

    [Fact]
    public void Disable_LeavesUnrelatedValuesAlone()
    {
        // A Run key is shared with every other app on the machine. Only our two names are ours.
        Seed("SomeOtherApp");
        Seed(StartupRegistration.LegacyValueName);

        Subject().Disable();

        Assert.Equal(["SomeOtherApp"], ValueNames());
    }

    public void Dispose()
    {
        // The scratch tree, not just the leaf — leaving per-test GUID keys behind would make this
        // suite the thing that litters a developer's registry.
        try { Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false); }
        catch (Exception) { /* best effort: a leaked scratch key must never fail a test run */ }
    }
}
