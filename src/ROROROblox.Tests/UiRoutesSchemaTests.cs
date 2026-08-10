using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// Validates docs/ui-routes.json as a contract rather than as prose.
/// <para>
/// The route file drives a tool that clicks buttons in the live app against real accounts, so two
/// of these assertions are safety properties, not tidiness: every step must name a control type
/// (five elements in this app share the name "Settings", and a name-only match binds the Window,
/// which carries no InvokePattern), and no route may target a denied control by name or AutomationId
/// (those controls stop Roblox clients, delete accounts, or launch game sessions). Safety properties
/// belong at build time, not discovered while the tool is driving a live app.
/// </para>
/// </summary>
public class UiRoutesSchemaTests
{
    private static readonly string[] KnownVerbs = { "invoke", "select", "expand", "close-window" };

    /// <summary>Control types the PowerShell resolver knows how to map. Keep in lockstep with
    /// the $script:ControlTypes table in scripts/capture-ui.ps1.</summary>
    private static readonly string[] KnownTypes =
        { "Button", "MenuItem", "ListItem", "Window", "ComboBox", "CheckBox", "List", "Text" };

    private static JsonElement LoadRoutes()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.NotNull(root);
        var path = Path.Combine(root!, "docs", "ui-routes.json");
        Assert.True(File.Exists(path), $"route file missing at {path}");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    }

    private static IEnumerable<JsonElement> Surfaces(JsonElement doc) =>
        doc.GetProperty("surfaces").EnumerateArray();

    private static IEnumerable<JsonElement> StepsOf(JsonElement surface)
    {
        foreach (var key in new[] { "open", "close" })
        {
            if (!surface.TryGetProperty(key, out var arr)) continue;
            foreach (var step in arr.EnumerateArray()) yield return step;
        }
    }

    [Fact]
    public void EveryStepNamesATypeAndExactlyOneSelector()
    {
        var doc = LoadRoutes();
        var problems = new List<string>();

        foreach (var surface in Surfaces(doc))
        {
            var id = surface.GetProperty("id").GetString();
            foreach (var step in StepsOf(surface))
            {
                var verb = step.TryGetProperty("do", out var d) ? d.GetString() : null;
                if (verb is null || !KnownVerbs.Contains(verb))
                    problems.Add($"{id}: unknown verb '{verb}'");

                if (!step.TryGetProperty("type", out var t) || string.IsNullOrWhiteSpace(t.GetString()))
                    problems.Add($"{id}: step '{verb}' carries no type");
                else if (!KnownTypes.Contains(t.GetString()))
                    problems.Add($"{id}: step '{verb}' names unknown type '{t.GetString()}'");

                var hasName = step.TryGetProperty("name", out _);
                var hasAid = step.TryGetProperty("aid", out _);
                if (hasName == hasAid)
                    problems.Add($"{id}: step '{verb}' must carry exactly one of name/aid");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void NoRouteTargetsADeniedName()
    {
        var doc = LoadRoutes();
        var deny = doc.GetProperty("deny").EnumerateArray()
            .Select(x => x.GetString()!).ToHashSet(StringComparer.Ordinal);

        // Guards the guard: an empty deny list or missing entries would make this test vacuously pass.
        Assert.Contains("Stop all Roblox instances", deny);
        Assert.Contains("Remove", deny);
        Assert.Contains("Launch As", deny);
        Assert.Contains("Launch multiple", deny);
        Assert.Contains("Squad Launch", deny);
        Assert.Contains("Stop", deny);
        Assert.Contains("Recycle", deny);
        Assert.Contains("Stop all", deny);
        Assert.Equal(8, deny.Count);

        var problems = new List<string>();
        foreach (var surface in Surfaces(doc))
        {
            var id = surface.GetProperty("id").GetString();
            foreach (var step in StepsOf(surface))
            {
                if (step.TryGetProperty("name", out var n) && deny.Contains(n.GetString()!))
                    problems.Add($"{id}: step targets denied name '{n.GetString()}'");

                if (step.TryGetProperty("aid", out var a) && deny.Contains(a.GetString()!))
                    problems.Add($"{id}: step targets denied aid '{a.GetString()}'");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void SurfaceIdsAreUniqueAndEveryCapturedSurfaceHasATarget()
    {
        var doc = LoadRoutes();
        var ids = Surfaces(doc).Select(s => s.GetProperty("id").GetString()!).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());

        var problems = new List<string>();
        foreach (var surface in Surfaces(doc))
        {
            var id = surface.GetProperty("id").GetString();
            var skipped = surface.TryGetProperty("skip", out _);
            var hasCapture = surface.TryGetProperty("capture", out var cap);

            if (skipped && hasCapture) problems.Add($"{id}: skipped surfaces must not declare a capture target");
            if (!skipped && !hasCapture) problems.Add($"{id}: captured surface declares no capture target");

            if (hasCapture)
            {
                if (!cap.TryGetProperty("type", out _)) problems.Add($"{id}: capture target carries no type");
                if (cap.TryGetProperty("name", out _) == cap.TryGetProperty("aid", out _))
                    problems.Add($"{id}: capture target must carry exactly one of name/aid");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void TheFileDescribesTheCampaignItClaimsTo()
    {
        var doc = LoadRoutes();
        var all = Surfaces(doc).ToList();
        var captured = all.Where(s => !s.TryGetProperty("skip", out _)).ToList();

        // Vacuity floor. A file that lost most of its surfaces would otherwise pass every
        // assertion above, since they all quantify over whatever happens to be present.
        Assert.Equal(18, all.Count);
        Assert.True(captured.Count >= 13,
            $"expected at least 13 captured surfaces, found {captured.Count}");

        // 04 was retired by glow wave 2 and must not come back.
        Assert.DoesNotContain(all, s => s.GetProperty("id").GetString() == "04");
    }
}
