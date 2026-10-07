using System.Text.Json;

namespace ROROROblox.Core.Discord;

/// <summary>
/// How often an alert slot is allowed to speak: one global quiet period plus an optional per-kind
/// override. Resolved once from settings and handed to <see cref="AlertRouter.Route"/>, which owned
/// the number as a <c>static readonly TimeSpan Cooldown</c> from v1.0 to v1.32.
/// <para>
/// <b>Zero means every time</b>, in the global and in an override alike. That is already this
/// codebase's idiom for a deliberate off (<c>MemoryCapMb</c>, <c>AppSettings.cs</c>), and it costs
/// one less nullable than the alternative. It is not a sentinel for "unset": nothing here needs to
/// tell zero from absent, because absent means "follow the global" and the global always has a
/// value.
/// </para>
/// <para>
/// <b>Overrides ride as one JSON key rather than one setting per kind.</b> Seven kinds would be
/// seven <c>IAppSettings</c> members, seven fakes across four test files and seven reachability
/// entries, for a feature whose UI is one generated table.
/// </para>
/// <para>
/// <b>The map is keyed by kind NAME, which makes member names load-bearing where order is not.</b>
/// <see cref="AlertKind"/>'s own doc comment says the enum is serialized nowhere and kept stable on
/// principle; this is the first thing that genuinely persists a kind, and it persists the name. A
/// numeric key is therefore rejected rather than accepted through <c>Enum.TryParse</c>'s ordinal
/// path — accepting "1" would quietly re-introduce the positional coupling the enum refuses.
/// </para>
/// </summary>
public sealed class AlertCadence
{
    /// <summary>
    /// Five minutes, which is what <c>AlertRouter.Cooldown</c> held through v1.32. The default of
    /// <c>AlertCadenceMinutes</c> agrees with it so an upgrade changes nobody's pace, and the
    /// constant stays here rather than being retyped at each site that derives a window from it
    /// (the smoke harness's alert window, the routed-nowhere log line).
    /// </summary>
    public static readonly TimeSpan DefaultQuietPeriod = TimeSpan.FromMinutes(5);

    /// <summary>The shipped pace. What every caller with no opinion about cadence passes.</summary>
    public static readonly AlertCadence Default = new(DefaultQuietPeriod, new Dictionary<AlertKind, TimeSpan>());

    private readonly IReadOnlyDictionary<AlertKind, TimeSpan> _overrides;

    private AlertCadence(TimeSpan global, IReadOnlyDictionary<AlertKind, TimeSpan> overrides)
    {
        Global = global;
        _overrides = overrides;
    }

    /// <summary>The quiet period for every kind that has no override of its own.</summary>
    public TimeSpan Global { get; }

    /// <summary>This kind's quiet period: its override if it has a usable one, else the global.</summary>
    public TimeSpan For(AlertKind kind) =>
        _overrides.TryGetValue(kind, out var quietPeriod) ? quietPeriod : Global;

    /// <summary>
    /// Resolve the two persisted values (<c>AlertCadenceMinutes</c>,
    /// <c>AlertCadenceOverridesJson</c>) into a cadence.
    /// <para>
    /// <b>Never throws, and degrades per entry.</b> A blob that is not JSON, not an object, or
    /// carries wrong-typed values yields no overrides at all and every kind follows the global; one
    /// bad row beside a good one drops only the bad row, because a hand-edited file with a typo in
    /// it should not discard the row the user got right. A settings file nobody can parse must not
    /// be able to stop alerting.
    /// </para>
    /// <para>
    /// <b>A negative count is read as the shipped default, not as "every time".</b> A negative is a
    /// corrupt file rather than a choice, and the loudest possible reading is the one direction a
    /// degraded read must not take — the fallback has to be quieter than the corruption, not louder.
    /// </para>
    /// </summary>
    public static AlertCadence FromSettings(int globalMinutes, string? overridesJson)
    {
        var global = globalMinutes >= 0 ? TimeSpan.FromMinutes(globalMinutes) : DefaultQuietPeriod;
        var overrides = ParseOverrides(overridesJson);
        return overrides.Count == 0 && global == DefaultQuietPeriod
            ? Default
            : new AlertCadence(global, overrides);
    }

    /// <summary>
    /// The inverse of <see cref="FromSettings"/>'s override half: a kind-to-minutes map as the
    /// string <c>AlertCadenceOverridesJson</c> holds. Added with the Settings table (v1.33 item 6),
    /// which is the first thing that writes this key.
    /// <para>
    /// <b>Here rather than in the page</b>, because a page that hand-rolls the JSON is a second
    /// encoder for a format <see cref="ParseOverrides"/> already owns — and the two most likely
    /// ways to get it wrong are both things this file has already decided. A numeric key is
    /// REFUSED on read, so a writer that emitted one would produce a file its own reader discards
    /// silently; and a negative count is discarded on read, so emitting one would persist a value
    /// that does nothing.
    /// </para>
    /// <para>
    /// <b>An empty map is the empty string, not <c>"{}"</c>.</b> Empty is what <c>SettingsBlob</c>
    /// ships and what <see cref="ParseOverrides"/> short-circuits on, so a user who sets an
    /// override and then clears it lands back on exactly the shipped value rather than on a
    /// different spelling of it.
    /// </para>
    /// </summary>
    public static string ToOverridesJson(IReadOnlyDictionary<AlertKind, int> overrides)
    {
        var usable = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (kind, minutes) in overrides)
        {
            // Same two refusals the reader makes, in the same direction: an undeclared kind cannot
            // be named and a negative count would be dropped on the way back in.
            if (!Enum.IsDefined(kind) || minutes < 0) continue;
            usable[kind.ToString()] = minutes;
        }

        return usable.Count == 0 ? string.Empty : JsonSerializer.Serialize(usable);
    }

    private static Dictionary<AlertKind, TimeSpan> ParseOverrides(string? overridesJson)
    {
        var resolved = new Dictionary<AlertKind, TimeSpan>();
        if (string.IsNullOrWhiteSpace(overridesJson)) return resolved;

        Dictionary<string, JsonElement>? raw;
        try
        {
            // Deserialized as JsonElement per value, not int, so ONE wrong-typed value loses its
            // own row instead of taking the whole map down with a JsonException.
            raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(overridesJson);
        }
        catch (JsonException)
        {
            return resolved;
        }

        if (raw is null) return resolved;

        foreach (var (name, value) in raw)
        {
            if (!TryParseKindName(name, out var kind)) continue;
            if (value.ValueKind != JsonValueKind.Number) continue;
            if (!value.TryGetInt32(out var minutes) || minutes < 0) continue;

            resolved[kind] = TimeSpan.FromMinutes(minutes);
        }

        return resolved;
    }

    /// <summary>
    /// A kind NAME, case-insensitively — and nothing else. <c>Enum.TryParse</c> also accepts a
    /// numeric string and any comma-separated combination of names, neither of which is a key this
    /// map ever writes, so both are refused here rather than silently given a meaning.
    /// </summary>
    private static bool TryParseKindName(string name, out AlertKind kind)
    {
        kind = default;
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (!char.IsLetter(name[0])) return false;
        if (name.Contains(',', StringComparison.Ordinal)) return false;
        return Enum.TryParse(name, ignoreCase: true, out kind) && Enum.IsDefined(kind);
    }
}
