using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ROROROblox.Core.Metrics;

namespace ROROROblox.App.Metrics;

/// <summary>
/// Reads metric rules from a JSON array the user writes by hand. The INTERIM source: plan 3
/// replaces it with a signed manifest, and this class exists so plan 2 has something end-to-end
/// testable without the manifest's signing rig.
///
/// <para>
/// <b>Unsigned on purpose, and that is not an oversight.</b> The manifest plan 3 ships is signed
/// because it names URLs the plugin will call, which makes an unsigned one an attacker choosing
/// this app's request targets. A rule names no URL — a metric id, a kind, a threshold, a window —
/// so there is no exfiltration primitive here to protect. Signing it would be ceremony.
/// </para>
/// <para>
/// Nothing here may throw. It is read on the gRPC report path, and a malformed file must cost the
/// user their rules, never their plugin host.
/// </para>
/// </summary>
public sealed class LocalFileMetricRuleSource(string filePath, ILogger<LocalFileMetricRuleSource> log)
    : IMetricRuleSource
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public IReadOnlyList<MetricRule> CurrentRules()
    {
        List<RuleRow>? rows;
        try
        {
            if (!File.Exists(filePath)) return [];
            rows = JsonSerializer.Deserialize<List<RuleRow>>(File.ReadAllText(filePath), Options);
        }
        catch (Exception ex)
        {
            // Named at Information, not Warning: a user who has never written this file is the
            // common case and is not in trouble. A user who wrote a broken one needs to be able
            // to find out why, and "no alerts ever" with a silent log is indistinguishable from
            // "the feature does not work".
            log.LogInformation(ex, "Could not read metric rules from {Path}; no rules are active.", filePath);
            return [];
        }

        if (rows is null) return [];

        var rules = new List<MetricRule>(rows.Count);
        foreach (var row in rows)
        {
            if (row is null) continue;

            // A rule with no metric id matches nothing, so it is not a rule. Keeping it would put
            // a row in the file that looks configured and can never fire.
            if (string.IsNullOrWhiteSpace(row.MetricId)) continue;

            if (!Enum.TryParse<MetricRuleKind>(row.Kind, ignoreCase: true, out var kind))
            {
                // One typo must not cost the rules that were fine.
                log.LogInformation("Metric rule for {MetricId} names an unknown kind {Kind}; skipped.",
                    row.MetricId, row.Kind);
                continue;
            }

            try
            {
                // TimeSpan.FromMinutes throws ArgumentException for NaN/Infinity and
                // OverflowException for a window outside TimeSpan's range — both reachable from a
                // hand-edited file (a stray "windowMinutes": 1e30). Caught here, not just at the
                // file level, so one row with a garbage number does not cost the rules that were
                // fine, same as an unknown kind above.
                rules.Add(new MetricRule(
                    row.MetricId,
                    kind,
                    row.Threshold,
                    TimeSpan.FromMinutes(row.WindowMinutes),
                    row.AlertWhenBelow));
            }
            catch (Exception ex)
            {
                log.LogInformation(ex, "Metric rule for {MetricId} names an unusable window {WindowMinutes}; skipped.",
                    row.MetricId, row.WindowMinutes);
            }
        }

        return rules;
    }

    /// <summary>The on-disk shape. Separate from <see cref="MetricRule"/> so the file format and
    /// the domain type can drift apart without one dragging the other.</summary>
    private sealed class RuleRow
    {
        [JsonPropertyName("metricId")] public string? MetricId { get; set; }
        [JsonPropertyName("kind")] public string? Kind { get; set; }
        [JsonPropertyName("threshold")] public double Threshold { get; set; }
        [JsonPropertyName("windowMinutes")] public double WindowMinutes { get; set; }
        [JsonPropertyName("alertWhenBelow")] public bool AlertWhenBelow { get; set; } = true;
    }
}
