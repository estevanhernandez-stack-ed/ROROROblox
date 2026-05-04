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
    /// Validates the same way <see cref="ListAsync"/> does — missing required fields throw
    /// <see cref="InvalidThemeException"/> with the offending field. Filename is derived from
    /// the theme's <c>name</c> field (lowercase-kebab); collisions overwrite. Returns the
    /// parsed <see cref="Theme"/> ready for <c>ThemeService</c> to apply.
    /// </summary>
    Task<Theme> SaveUserThemeAsync(string rawJson);
}

/// <summary>
/// Thrown by <see cref="IThemeStore.SaveUserThemeAsync"/> when the JSON is malformed or
/// missing a required field. Message is shaped for direct display in the theme builder UI.
/// </summary>
public sealed class InvalidThemeException : Exception
{
    public InvalidThemeException(string message) : base(message) { }
    public InvalidThemeException(string message, Exception inner) : base(message, inner) { }
}
