using System.IO;
using Microsoft.Extensions.Logging;
using ROROROblox.App;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// A run on another data folder says so (item 3a of the data-folder-override run): the log lines,
/// the window-title tag, and where they are wired. The wording is a pure function of the
/// <see cref="DataRootDecision"/>, so none of this needs a running app.
/// </summary>
public class DataRootMarkerTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "rororo-marker-test-folder");

    private static DataRootDecision Used => new(DataRootDecisionReason.Used, Folder);

    [Fact]
    public void Used_IsOneWarning_ThatNamesTheFolder()
    {
        var line = DataRootMarker.LogLine(Used);

        Assert.NotNull(line);
        Assert.Equal(LogLevel.Warning, line!.Level);
        Assert.Contains(Folder, line.Message);
        Assert.Contains(AppDataRoot.EnvironmentVariableName, line.Message);
    }

    [Theory]
    [InlineData(DataRootDecisionReason.NotADevBuild)]
    [InlineData(DataRootDecisionReason.NotAnAbsolutePath)]
    public void Ignored_IsOneInformationLine_SayingWhy(DataRootDecisionReason reason)
    {
        var line = DataRootMarker.LogLine(new DataRootDecision(reason, null));

        Assert.NotNull(line);
        Assert.Equal(LogLevel.Information, line!.Level);
        Assert.Contains("ignored", line.Message);
        Assert.Contains(AppDataRoot.EnvironmentVariableName, line.Message);
    }

    [Fact]
    public void NotADevBuild_AndNotAbsolute_GiveDifferentReasons()
    {
        var a = DataRootMarker.LogLine(new DataRootDecision(DataRootDecisionReason.NotADevBuild, null))!.Message;
        var b = DataRootMarker.LogLine(new DataRootDecision(DataRootDecisionReason.NotAnAbsolutePath, null))!.Message;

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Unset_SaysNothing()
    {
        Assert.Null(DataRootMarker.LogLine(DataRootDecision.Unset));
    }

    [Theory]
    [InlineData(DataRootDecisionReason.Unset)]
    [InlineData(DataRootDecisionReason.NotADevBuild)]
    [InlineData(DataRootDecisionReason.NotAnAbsolutePath)]
    public void IgnoredLines_NeverCarryAFolderOrAPath(DataRootDecisionReason reason)
    {
        // A rejected value is whatever the caller typed; it is not echoed.
        var line = DataRootMarker.LogLine(new DataRootDecision(reason, null));

        Assert.True(line is null || !line.Message.Contains((char)92) && !line.Message.Contains('/'));
    }

    [Fact]
    public void Lines_CarryNothingSecret()
    {
        // Nothing but the variable name and the folder: no cookie prefix, token or key material.
        var all = new[]
        {
            DataRootMarker.LogLine(Used)!.Message,
            DataRootMarker.LogLine(new DataRootDecision(DataRootDecisionReason.NotADevBuild, null))!.Message,
            DataRootMarker.LogLine(new DataRootDecision(DataRootDecisionReason.NotAnAbsolutePath, null))!.Message,
        };

        foreach (var message in all)
        {
            Assert.DoesNotContain("ROBLOSECURITY", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("_|WARNING", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token", message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Log_WritesExactlyOneEntry_AtTheLineLevel_AndNothingWhenUnset()
    {
        var used = new CapturingLogger();
        DataRootMarker.Log(used, Used);
        var entry = Assert.Single(used.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains(Folder, entry.Text);

        var ignored = new CapturingLogger();
        DataRootMarker.Log(ignored, new DataRootDecision(DataRootDecisionReason.NotADevBuild, null));
        Assert.Equal(LogLevel.Information, Assert.Single(ignored.Entries).Level);

        var unset = new CapturingLogger();
        DataRootMarker.Log(unset, DataRootDecision.Unset);
        Assert.Empty(unset.Entries);
    }

    [Fact]
    public void Log_DoesNotTreatTheFolderAsATemplate()
    {
        // A folder with braces must not be re-parsed as a message template.
        var log = new CapturingLogger();
        DataRootMarker.Log(log, new DataRootDecision(DataRootDecisionReason.Used, @"C:\test{0}\data"));

        Assert.Contains(@"C:\test{0}\data", Assert.Single(log.Entries).Text);
    }

    // ---- title tag ----

    [Fact]
    public void TitleTag_IsPresentOnlyUnderTheOverride()
    {
        Assert.Equal(DataRootMarker.TitleTag, DataRootMarker.TitleSuffix(Used));
        Assert.Null(DataRootMarker.TitleSuffix(DataRootDecision.Unset));
        Assert.Null(DataRootMarker.TitleSuffix(new DataRootDecision(DataRootDecisionReason.NotADevBuild, null)));
        Assert.Null(DataRootMarker.TitleSuffix(new DataRootDecision(DataRootDecisionReason.NotAnAbsolutePath, null)));

        Assert.Equal("RoRoRo [test data]", DataRootMarker.Tagged("RoRoRo", Used));
        Assert.Equal("RoRoRo", DataRootMarker.Tagged("RoRoRo", DataRootDecision.Unset));
        Assert.Equal("RoRoRo", DataRootMarker.Tagged("RoRoRo", new DataRootDecision(DataRootDecisionReason.NotADevBuild, null)));
    }

    // ---- wiring (source fences: the app cannot be started in a test) ----

    [Fact]
    public void Wiring_LogFollowsTheStartingLine_AndBothTitlesAreTagged()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "ROROROblox.App", "App.xaml.cs"));
        var start = app.IndexOf("\"ROROROblox starting (v{Version}", StringComparison.Ordinal);
        var call = app.IndexOf("DataRootMarker.Log(_log, AppDataRoot.Decision)", StringComparison.Ordinal);
        Assert.True(start > 0, "the starting line moved; update this fence");
        Assert.True(call > start, "DataRootMarker.Log must come after the starting line");
        var afterLine = app.IndexOf('\n', start) + 1;
        Assert.All(app.Substring(afterLine, call - afterLine).Split('\n'), l => Assert.True(
            l.Trim().Length == 0 || l.Trim().StartsWith("//", StringComparison.Ordinal),
            $"only comments may sit between the starting line and the marker, found: {l.Trim()}"));

    }

    /// <summary>
    /// Both titles are live <c>{loc:Loc}</c> bindings. Assigning a title replaces its binding with
    /// a fixed string, so the tag may only be applied inside the override check. The first build of
    /// this assigned both titles in every build ("no-op otherwise"), which would have frozen the
    /// window title's language on every shipped install, with a green suite.
    /// </summary>
    [Fact]
    public void Wiring_TitlesAreAssignedOnlyInsideTheOverrideCheck()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        const string file = "src/ROROROblox.App/MainWindow.xaml.cs";
        var code = SettingsReachabilityTests.WithoutComments(
            file, File.ReadAllLines(Path.Combine(root!, file.Replace('/', Path.DirectorySeparatorChar))));

        var guard = Array.FindIndex(code, l => l.Contains(
            "if (DataRootMarker.TitleSuffix(AppDataRoot.Decision) is { } testDataTag)", StringComparison.Ordinal));
        Assert.True(guard >= 0, "MainWindow must apply the title tag inside a DataRootMarker.TitleSuffix check.");
        Assert.Equal("{", code[guard + 1].Trim());
        Assert.Equal("Title += testDataTag;", code[guard + 2].Trim());
        Assert.Equal("MainTitleBar.Title += testDataTag;", code[guard + 3].Trim());
        Assert.Equal("}", code[guard + 4].Trim());

        // And nowhere else: no other line of the window assigns either title.
        var assignments = code
            .Select((line, i) => (line: line.Trim(), i))
            .Where(x => System.Text.RegularExpressions.Regex.IsMatch(x.line, @"^(this\.)?(MainTitleBar\.)?Title\s*(\+)?=[^=]"))
            .Select(x => x.i)
            .ToList();
        Assert.Equal([guard + 2, guard + 3], assignments);
        Assert.DoesNotContain(code, l => l.Contains("DataRootMarker.Tagged(", StringComparison.Ordinal));
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Text)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
