namespace ROROROblox.Core.Theming;

/// <summary>
/// Reads built-in + user-supplied themes from disk. Built-ins are baked-in records; user
/// themes are JSON files dropped into <c>%LOCALAPPDATA%\ROROROblox\themes\</c>. The user
/// folder is created on first call. The Sanduhr-style design philosophy: dropping a file is
/// the install gesture; no UI flow needed.
/// </summary>
public interface IThemeStore
{
    /// <summary>
    /// Built-in themes plus every valid <c>*.json</c> in the user themes folder. Ordering:
    /// built-ins first, then user themes alphabetical. Invalid JSON files are silently
    /// skipped (logged at Debug; the user-facing surface is "the file just doesn't appear").
    /// </summary>
    Task<IReadOnlyList<Theme>> ListAsync();

    /// <summary>
    /// Find a theme by id (case-insensitive). Built-in ids are stable strings ("brand",
    /// "midnight", etc.); user-theme ids are derived from filename (lowercase-kebab).
    /// </summary>
    Task<Theme?> GetByIdAsync(string id);

    /// <summary>
    /// Returns the path to the user themes folder. UI surfaces this as an "Open themes folder"
    /// link so the user can drop new files in.
    /// </summary>
    string UserThemesFolder { get; }

    /// <summary>
    /// Persist a JSON blob (typically pasted in from a chat agent) as a new user theme.
    /// Validates the same way <see cref="ListAsync"/> does — a bad blob throws
    /// <see cref="InvalidThemeException"/> carrying the <see cref="InvalidThemeException.Kind"/>
    /// (and, where relevant, the offending field or parser detail). Filename is derived from
    /// the theme's <c>name</c> field (lowercase-kebab); collisions overwrite. Returns the
    /// parsed <see cref="Theme"/> ready for <c>ThemeService</c> to apply.
    /// </summary>
    Task<Theme> SaveUserThemeAsync(string rawJson);
}

/// <summary>Why a pasted theme blob was rejected — one arm per validation failure.</summary>
public enum InvalidThemeKind
{
    /// <summary>Nothing was pasted.</summary>
    EmptyInput,

    /// <summary>The blob is not parseable JSON (Detail carries the parser's reason).</summary>
    UnreadableJson,

    /// <summary>Parsed to null — an empty document.</summary>
    NullPayload,

    /// <summary>No <c>name</c> field, so nothing to title or file the theme under.</summary>
    MissingName,

    /// <summary>The name normalized to an empty filename (e.g. all punctuation).</summary>
    EmptyFilename,

    /// <summary>A required colour field is absent (Detail names it).</summary>
    MissingField,
}

/// <summary>
/// Thrown by <see cref="IThemeStore.SaveUserThemeAsync"/> when the JSON is malformed or missing a
/// required field. Carries DATA, not prose (the Core string boundary, localization Phase D
/// 2026-09-07): the App turns <see cref="Kind"/> (+ <see cref="Detail"/>) into the localized inline
/// message via <c>CoreMessageCatalog.For(InvalidThemeException)</c>. The base <see cref="Exception.Message"/>
/// is a fixed invariant-English diagnostic for logs — never shown to a viewer — so there is no
/// caller-supplied message ctor to reintroduce prose through.
/// </summary>
public sealed class InvalidThemeException : Exception
{
    public InvalidThemeKind Kind { get; }

    /// <summary>Diagnostic data for the <see cref="Kind"/> that needs it — the missing field's name
    /// (<see cref="InvalidThemeKind.MissingField"/>) or the JSON parser's reason
    /// (<see cref="InvalidThemeKind.UnreadableJson"/>). Null for kinds that speak for themselves.</summary>
    public string? Detail { get; }

    public InvalidThemeException(InvalidThemeKind kind, string? detail = null)
        : base($"Invalid theme ({kind}).")
    {
        Kind = kind;
        Detail = detail;
    }

    public InvalidThemeException(InvalidThemeKind kind, Exception inner, string? detail = null)
        : base($"Invalid theme ({kind}).", inner)
    {
        Kind = kind;
        Detail = detail;
    }
}
