using System.IO;
using System.Text.RegularExpressions;

namespace ROROROblox.Tests;

/// <summary>
/// Every test class that sets the process-wide <c>TranslationSource.Instance.CurrentCulture</c> must sit in the
/// <c>MutatesUiCulture</c> collection, or it runs in parallel with a render test and that test measures German. Found
/// 2026-11-03: four v1.36 classes set the culture outside the collection, and
/// <c>MainWindowOneAccountFitsTests</c> failed to find its "FAVOURITES" heading whenever the label-wrap test ran
/// beside it.
/// </summary>
public class MutatesUiCultureFenceTests
{
    [Fact]
    public void EveryClassThatSetsTheUiCulture_IsInTheMutatesUiCultureCollection()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.GetFiles(dir, "ROROROblox.slnx").Any()) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var tests = Path.Combine(dir!, "src", "ROROROblox.Tests");

        var mutators = Directory.EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"TranslationSource\.Instance\.CurrentCulture\s*="))
            .ToList();

        Assert.True(mutators.Count >= 8, $"found only {mutators.Count} culture-mutating files; the scan has probably stopped matching");
        var missing = mutators
            .Where(f => !File.ReadAllText(f).Contains("[Collection(\"MutatesUiCulture\")]", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(tests, f))
            .ToList();
        Assert.True(missing.Count == 0, "Set the UI culture outside the MutatesUiCulture collection:\n" + string.Join("\n", missing));
    }
}
