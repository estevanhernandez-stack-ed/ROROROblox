using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using ROROROblox.App.Discord;
using ROROROblox.App.Startup;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Under a data-folder override RoRoRo neither writes nor removes its machine-wide registrations
/// (item 3b): the roblox-rororo and discord-{id} URI schemes and the run-on-login value. Every
/// registry touch here goes through the classes' internal seams against a throwaway key under
/// <c>HKCU\Software\ROROROblox.Tests\&lt;guid&gt;</c>, deleted in <see cref="Dispose"/>. Nothing here
/// can reach a real <c>Classes</c> or <c>Run</c> value, including when a guard is removed.
/// <para>
/// The override is the <c>AsyncLocal</c> scope (<c>AppDataRoot.OverrideForTest</c>), never the
/// process-wide <c>Configure</c>, so these run in parallel with everything else.
/// </para>
/// </summary>
public sealed class DataRootRegistrationGuardTests : IDisposable
{
    private readonly string _root = $@"Software\ROROROblox.Tests\{Guid.NewGuid():N}";
    private readonly string _tempFolder = Path.Combine(Path.GetTempPath(), "rororo-regguard-" + Guid.NewGuid().ToString("N"));

    private string ClassesPath => _root + @"\Classes";
    private string RunPath => _root + @"\Run";

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);
        // Leave no empty parent behind either. DeleteSubKey refuses a key that still has children,
        // which is the right answer if anything else is ever using it.
        try { Registry.CurrentUser.DeleteSubKey(@"Software\ROROROblox.Tests", throwOnMissingSubKey: false); }
        catch (InvalidOperationException) { }
        if (Directory.Exists(_tempFolder)) Directory.Delete(_tempFolder, recursive: true);
    }

    private IDisposable Override() => AppDataRoot.OverrideForTest(_tempFolder);

    private static bool KeyExists(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key is not null;
    }

    private static object? Value(string path, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key?.GetValue(name);
    }

    private void SeedRun(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunPath);
        key.SetValue(name, value);
    }

    // ---- the predicate ----

    [Fact]
    public void Predicate_IsFalseUnderTheOverride_AndTrueWithout()
    {
        Assert.True(AppDataRoot.MachineWideRegistrationsAllowed);
        using (Override())
        {
            Assert.False(AppDataRoot.MachineWideRegistrationsAllowed);
            Assert.True(AppDataRoot.IsOverridden);
        }
        Assert.True(AppDataRoot.MachineWideRegistrationsAllowed);
    }

    // ---- join scheme ----

    [Fact]
    public void JoinScheme_Register_IsSkippedUnderTheOverride()
    {
        using (Override())
        {
            JoinUriScheme.Register(@"C:\fake\RoRoRo.exe", ClassesPath);
        }

        Assert.False(KeyExists(ClassesPath), "no scheme key may be created under the override");
    }

    [Fact]
    public void JoinScheme_Register_StillWritesWithoutTheOverride()
    {
        JoinUriScheme.Register(@"C:\fake\RoRoRo.exe", ClassesPath);

        Assert.Equal("\"C:\\fake\\RoRoRo.exe\" \"%1\"",
            Value($@"{ClassesPath}\{JoinUriScheme.SchemeName}\shell\open\command", ""));
    }

    [Fact]
    public void DiscordSchemeFixup_IsSkippedUnderTheOverride_AndWritesWithout()
    {
        using (Override())
        {
            JoinUriScheme.FixupDiscordSchemeCommand("123", @"C:\fake\RoRoRo.exe", ClassesPath);
        }
        Assert.False(KeyExists(ClassesPath));

        JoinUriScheme.FixupDiscordSchemeCommand("123", @"C:\fake\RoRoRo.exe", ClassesPath);
        Assert.Equal("\"C:\\fake\\RoRoRo.exe\" \"%1\"",
            Value($@"{ClassesPath}\discord-123\shell\open\command", ""));
    }

    // ---- run on login ----

    [Fact]
    public void RunOnLogin_Enable_IsSkippedUnderTheOverride()
    {
        var subject = new StartupRegistration(RunPath);
        using (Override())
        {
            subject.Enable();
        }

        Assert.False(KeyExists(RunPath), "Enable must not create the Run key or its value under the override");
    }

    [Fact]
    public void RunOnLogin_Disable_LeavesTheOwnersValueAlone_UnderTheOverride()
    {
        SeedRun(StartupRegistration.ValueName, "\"C:\\real\\RoRoRo.exe\"");
        SeedRun(StartupRegistration.LegacyValueName, "\"C:\\older\\RoRoRo.exe\"");
        var subject = new StartupRegistration(RunPath);

        using (Override())
        {
            subject.Disable();
        }

        Assert.NotNull(Value(RunPath, StartupRegistration.ValueName));
        Assert.NotNull(Value(RunPath, StartupRegistration.LegacyValueName));
    }

    [Fact]
    public void RunOnLogin_Toggle_UnderTheOverride_KeepsItsOwnStateAndNeverShowsTheOwnersValue()
    {
        SeedRun(StartupRegistration.ValueName, "\"C:\\real\\RoRoRo.exe\"");
        var subject = new StartupRegistration(RunPath);

        using (Override())
        {
            Assert.False(subject.IsEnabled(), "a test profile starts with run-on-login off");
            subject.Enable();
            Assert.True(subject.IsEnabled());
            subject.Disable();
            Assert.False(subject.IsEnabled());
        }

        Assert.Equal("\"C:\\real\\RoRoRo.exe\"", Value(RunPath, StartupRegistration.ValueName));
    }

    [Fact]
    public void RunOnLogin_EnableAndDisable_StillWriteAndRemoveWithoutTheOverride()
    {
        var subject = new StartupRegistration(RunPath);

        subject.Enable();
        Assert.NotNull(Value(RunPath, StartupRegistration.ValueName));

        subject.Disable();
        Assert.Null(Value(RunPath, StartupRegistration.ValueName));
    }

    // ---- the Discord adapter has no seam: source fences ----

    [Fact]
    public void DiscordAdapter_GuardsLacheesOwnSchemeWrite_BeforeCallingIt()
    {
        var src = File.ReadAllText(Path.Combine(XamlStyleScanner.FindRepoRoot(),
            "src", "ROROROblox.App", "Discord", "Internal", "LacheeDiscordRpcClientAdapter.cs"));

        var guard = src.IndexOf("AppDataRoot.MachineWideRegistrationsAllowed", StringComparison.Ordinal);
        var register = src.IndexOf("_client.RegisterUriScheme()", StringComparison.Ordinal);
        var subscribe = src.IndexOf("_client.Subscribe(", StringComparison.Ordinal);

        Assert.True(guard > 0, "the adapter must consult the registrations predicate");
        Assert.True(register > guard, "the guard must come before Lachee's RegisterUriScheme");
        Assert.True(subscribe > register, "Subscribe sits inside the same guarded block");
        Assert.Single(Regex.Matches(src, @"_client\.RegisterUriScheme\(\)"));
    }

    [Fact]
    public void NoRegistryWriteOutsideTheGuardedClasses()
    {
        // The registry writes an unpackaged start can make live in exactly these two files, and both
        // consult the predicate. A new SetValue/CreateSubKey/DeleteValue elsewhere in App or Core
        // fails here until it is guarded or listed.
        var root = XamlStyleScanner.FindRepoRoot();
        var write = new Regex(@"\.(CreateSubKey|DeleteValue|DeleteSubKey|DeleteSubKeyTree)\(|\bkey\.SetValue\(|\bcommand\.SetValue\(");
        var allowed = new[] { Path.Combine("Discord", "JoinUriScheme.cs"), Path.Combine("Startup", "StartupRegistration.cs") };
        var offenders = new List<string>();
        var seen = 0;
        foreach (var dir in new[] { "ROROROblox.App", "ROROROblox.Core" })
        {
            foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "src", dir), "*.cs", SearchOption.AllDirectories))
            {
                if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                var text = File.ReadAllText(f);
                if (!write.IsMatch(text)) continue;
                if (allowed.Any(a => f.EndsWith(a, StringComparison.OrdinalIgnoreCase)))
                {
                    seen++;
                    Assert.Contains("MachineWideRegistrationsAllowed", text);
                }
                else
                {
                    offenders.Add(f);
                }
            }
        }

        Assert.Empty(offenders);
        Assert.Equal(2, seen);
    }
}
