using System.IO;
using System.Text.RegularExpressions;

namespace ROROROblox.Tests;

/// <summary>
/// <c>Program.Main</c> must configure the data-folder override straight after Velopack's hooks and
/// BEFORE <c>UiCulture.ApplyFromSettings</c>, because that line already reads <c>settings.json</c>
/// through <c>AppDataRoot</c>. Resolve a RoRoRo path earlier and the override is silently ignored for
/// that read, with a green suite. A source fence, because <c>Main</c> cannot be run in a test.
/// </summary>
public class ProgramDataRootCallOrderTests
{
    private const string ProgramFile = "src/ROROROblox.App/Program.cs";

    private static string[] Code()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate the repo root from the test bin directory.");
        var lines = File.ReadAllLines(Path.Combine(root!, ProgramFile.Replace('/', Path.DirectorySeparatorChar)));
        return SettingsReachabilityTests.WithoutComments(ProgramFile, lines);
    }

    private static int LineOf(string[] code, string needle)
    {
        var hits = Enumerable.Range(0, code.Length)
            .Where(i => code[i].Contains(needle, StringComparison.Ordinal)).ToList();
        Assert.True(hits.Count == 1, $"{ProgramFile} should contain `{needle}` in code exactly once; found {hits.Count}.");
        return hits[0];
    }

    [Fact]
    public void Main_ConfiguresTheDataRoot_AfterVelopack_AndBeforeTheUiCultureRead()
    {
        var code = Code();
        var velopack = LineOf(code, "VelopackApp.Build().SetArgs(args).Run();");
        var configure = LineOf(code, "AppDataRoot.Configure(");
        var culture = LineOf(code, "Localization.UiCulture.ApplyFromSettings();");

        Assert.True(velopack < configure, "AppDataRoot.Configure must come after the Velopack line.");
        Assert.True(configure < culture, "AppDataRoot.Configure must come before UiCulture.ApplyFromSettings, which reads settings.json.");
    }

    [Fact]
    public void Main_BeforeTheConfigureCall_ResolvesNoRoRoRoPath()
    {
        // Nothing between the top of Main and the Configure call may touch the helper's folder, a
        // store, or the settings. Only the Velopack line is allowed to precede it.
        var code = Code();
        var mainStart = LineOf(code, "public static int Main(string[] args)");
        var configure = LineOf(code, "AppDataRoot.Configure(");
        var between = string.Join("\n", code.Skip(mainStart + 1).Take(configure - mainStart - 1));

        Assert.DoesNotMatch(new Regex(@"AppDataRoot\.(Folder|Combine)|AppSettings|AccountStore|DefaultPath|AppLogging|GetFolderPath"), between);
    }

    [Fact]
    public void Main_ReadsTheVariableByItsNamedConstant()
    {
        var code = string.Join("\n", Code());

        Assert.Contains("Environment.GetEnvironmentVariable(AppDataRoot.EnvironmentVariableName)", code, StringComparison.Ordinal);
        Assert.Equal("RORORO_DATA_DIR", ROROROblox.Core.AppDataRoot.EnvironmentVariableName);
    }
}
