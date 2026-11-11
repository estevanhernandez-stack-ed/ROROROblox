using System.Globalization;
using System.Runtime.CompilerServices;

namespace ROROROblox.Tests;

/// <summary>
/// Runs the whole suite under another regional FORMAT culture (decimal comma, day-first dates),
/// which is a different thing from the UI language the localization tests switch.
/// <para>
/// Set <c>RORORO_TEST_FORMAT_CULTURE</c> before <c>dotnet test</c>, for example <c>pl-PL</c> or
/// <c>pt-BR</c>. Unset, this does nothing. A test that fails only with it set has found either code
/// that formats or parses with the machine's regional settings, or an assertion that assumed
/// English ones; both are worth knowing about.
/// </para>
/// </summary>
internal static class FormatCultureProbe
{
    [ModuleInitializer]
    internal static void Apply()
    {
        var name = Environment.GetEnvironmentVariable("RORORO_TEST_FORMAT_CULTURE");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var culture = CultureInfo.GetCultureInfo(name);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.CurrentCulture = culture;
    }
}
