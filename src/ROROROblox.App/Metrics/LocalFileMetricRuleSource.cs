using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ROROROblox.Core.Metrics;

namespace ROROROblox.App.Metrics;

/// <summary>
/// Reads metric rules from a JSON array the user writes by hand. This is the permanent rule
/// source — the signed manifest that was once going to replace it was dropped, so the two
/// allowances this class made for being temporary (one bad row could drop the whole file; the
/// file was re-read and re-parsed on every reported metric) no longer apply and have been fixed.
///
/// <para>
/// <b>Unsigned on purpose, and that is not an oversight.</b> A signed manifest would have named
/// URLs the plugin calls, which hands an attacker this app's request targets — an exfiltration
/// primitive. A rule names only a metric id, a kind, a threshold and a window, so there is
/// nothing here that signing would protect. That is also why dropping the manifest plan was
/// safe: nothing it would have added on the security side is missing from this file.
/// </para>
/// <para>
/// Nothing here may throw. It is read on the gRPC report path, and a malformed file — or a
/// single malformed row inside an otherwise good one — must cost the user their rules, never
/// their plugin host. The parse is cached against a hash of the file's bytes — not its
/// last-write time and length, which a same-size edit inside the filesystem's timestamp
/// resolution can leave unchanged for genuinely different content — so an unchanged file is
/// not re-parsed (and a malformed one is not re-logged) on every report, while an edited file
/// is still picked up without a restart because its hash changes with it. What the cache hands
/// back is read-only, so a caller cannot mutate the shared cached result out from under later
/// calls.
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

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private RuleCache? _cache;

    public IReadOnlyList<MetricRule> CurrentRules()
    {
        string? contentHash = null;
        try
        {
            if (!File.Exists(filePath))
            {
                // Absent is the shipped default for an opt-in feature: silent, not an error, and
                // not cached — if the file later appears, the next call must actually look.
                _cache = null;
                return [];
            }

            var bytes = File.ReadAllBytes(filePath);

            // Keyed on the file's content, not its last-write time plus length: a same-size
            // hand-edit whose write lands inside the filesystem's timestamp resolution, or a
            // restore that preserves both, would otherwise produce an identical signature for
            // different content and serve stale rules forever with no error and no log. The
            // parse below — walking the tree and converting each row — is what the cache exists
            // to skip; hashing the bytes we already hold for that parse costs a small fraction
            // of it, so this is strictly cheaper than the timestamp approach was ever meant to
            // protect against, not a slower-but-safer trade.
            contentHash = Convert.ToHexString(SHA256.HashData(bytes));

            if (_cache is { } cached && cached.ContentHash == contentHash)
            {
                // Same instance on purpose: this is the hot gRPC report path, and a caller that
                // never throttles must not pay a full re-parse per report.
                return cached.Rules;
            }

            var rules = ParseRules(DecodeUtf8(bytes));
            _cache = new RuleCache(contentHash, rules);
            return rules;
        }
        catch (Exception ex)
        {
            // Named at Information, not Warning: a user who has never written this file is the
            // common case and is not in trouble. A user who wrote a broken one needs to be able
            // to find out why, and "no alerts ever" with a silent log is indistinguishable from
            // "the feature does not work".
            log.LogInformation(ex, "Could not read metric rules from {Path}; no rules are active.", filePath);

            // Cache the empty outcome against the hash we already have, so a malformed file's
            // log line fires once per edit, not once per reported metric. If we could not even
            // read the file (a rare race with a concurrent delete), there is no hash to key on —
            // leave the cache alone and let the next call try again.
            if (contentHash is not null) _cache = new RuleCache(contentHash, []);
            return [];
        }
    }

    /// <summary>Decodes the bytes as UTF-8, stripping a leading byte-order mark if present. A raw
    /// byte read (needed so the same bytes can be hashed for the cache key) bypasses
    /// <see cref="File.ReadAllText(string)"/>'s built-in BOM handling, so this replicates it —
    /// a hand-edited file saved with a BOM must parse exactly as it did before.</summary>
    private static string DecodeUtf8(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }

    /// <summary>Parses the whole array, converting each row inside its own try so one
    /// type-mismatched field — a hand-edited file is exactly where a stray quote happens — costs
    /// only that row, never the rest.</summary>
    private IReadOnlyList<MetricRule> ParseRules(string json)
    {
        using var doc = JsonDocument.Parse(json, DocumentOptions);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            log.LogInformation("Metric rules file {Path} is not a JSON array; no rules are active.", filePath);
            return [];
        }

        var rules = new List<MetricRule>();
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            try
            {
                var parsed = row.Deserialize<RuleRow>(Options);
                if (parsed is null) continue;

                // A rule with no metric id matches nothing, so it is not a rule. Keeping it
                // would put a row in the file that looks configured and can never fire.
                if (string.IsNullOrWhiteSpace(parsed.MetricId)) continue;

                if (!Enum.TryParse<MetricRuleKind>(parsed.Kind, ignoreCase: true, out var kind))
                {
                    // One typo must not cost the rules that were fine.
                    log.LogInformation("Metric rule for {MetricId} names an unknown kind {Kind}; skipped.",
                        parsed.MetricId, parsed.Kind);
                    continue;
                }

                rules.Add(new MetricRule(
                    parsed.MetricId,
                    kind,
                    parsed.Threshold,
                    TimeSpan.FromMinutes(parsed.WindowMinutes),
                    parsed.AlertWhenBelow));
            }
            catch (Exception ex)
            {
                // Covers everything a hand-edited row can throw: a type-mismatched field or a
                // null where a number belongs (both from the Deserialize above), and a garbage
                // window reaching TimeSpan.FromMinutes — ArgumentException for NaN/Infinity,
                // OverflowException for a window outside TimeSpan's range. One bad row must not
                // cost the rules that were fine, same as an unknown kind or a missing id above.
                log.LogInformation(ex, "Metric rule row in {Path} could not be read; skipped.", filePath);
            }
        }

        // AsReadOnly() wraps the list rather than exposing it cast to an interface: the wrapper
        // is a distinct concrete type (ReadOnlyCollection<T>) whose mutating members throw, so a
        // caller that casts the returned IReadOnlyList back to something mutable still cannot
        // corrupt the cached instance shared across every later call.
        return rules.AsReadOnly();
    }

    private sealed record RuleCache(string ContentHash, IReadOnlyList<MetricRule> Rules);

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
