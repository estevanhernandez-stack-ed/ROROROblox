using System.IO;
using System.Text;

namespace ROROROblox.Core.Theming;

/// <summary>Why <see cref="ThemeShare.ValidateImport"/> refused a file.</summary>
public enum ThemeImportRefusal
{
    /// <summary>Not refused.</summary>
    None,

    /// <summary>The store's loader would not take it (unreadable JSON, missing name, missing field). See
    /// <see cref="ThemeImportCheck.Invalid"/> for which.</summary>
    Invalid,

    /// <summary>The theme's id is one of the built-ins, which always win, so the import would never appear.</summary>
    BuiltInId,
}

/// <summary>
/// Result of <see cref="ThemeShare.ValidateImport"/>. Data, not prose: the App turns the refusal into
/// a localized sentence (the Core string boundary, same as <see cref="InvalidThemeException"/>).
/// </summary>
public sealed record ThemeImportCheck(Theme? Theme, ThemeImportRefusal Refusal, InvalidThemeException? Invalid)
{
    public bool IsValid => Refusal == ThemeImportRefusal.None && Theme is not null;
}

/// <summary>An exported theme: the bytes to write and the file name to offer.</summary>
public sealed record ThemeExport(byte[] Bytes, string SuggestedFileName);

/// <summary>What <see cref="ThemeShare.ImportInto"/> did.</summary>
public enum ThemeImportOutcome
{
    /// <summary>The file was written.</summary>
    Written,

    /// <summary>A custom theme with this id is already in the folder and overwrite was not asked for.</summary>
    AlreadyExists,
}

public sealed record ThemeImportResult(ThemeImportOutcome Outcome, Theme Theme);

/// <summary>
/// Share a custom theme as a file, take one in. No new format: export is the JSON
/// <see cref="ThemeStore"/> writes for a custom theme, and import is validated by the store's own
/// loader (<c>ThemeStore.ParseUserTheme</c>), so a file that imports here is a file the picker will list.
/// </summary>
public static class ThemeShare
{
    /// <summary>The theme's JSON as the store would write it, plus <c>&lt;safe theme name&gt;.json</c>.
    /// Built-ins are ours and ship with the app, so they are not exportable.</summary>
    public static ThemeExport ExportBytes(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (theme.IsBuiltIn)
        {
            throw new ArgumentException("Built-in themes ship with the app and are not exported.", nameof(theme));
        }
        return new ThemeExport(ThemeStore.ToCanonicalBytes(theme), SafeFileName(theme));
    }

    /// <summary>Parses through the store's own loader, then refuses an id a built-in owns.</summary>
    public static ThemeImportCheck ValidateImport(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        Theme theme;
        try
        {
            theme = ThemeStore.ParseUserTheme(DecodeUtf8(bytes)).Theme;
        }
        catch (InvalidThemeException ex)
        {
            return new ThemeImportCheck(null, ThemeImportRefusal.Invalid, ex);
        }

        if (ThemeStore.IsBuiltInId(theme.Id))
        {
            return new ThemeImportCheck(theme, ThemeImportRefusal.BuiltInId, null);
        }
        return new ThemeImportCheck(theme, ThemeImportRefusal.None, null);
    }

    /// <summary>Writes <c>&lt;id&gt;.json</c> into the themes folder. Throws
    /// <see cref="InvalidThemeException"/> if the bytes do not validate and
    /// <see cref="ArgumentException"/> for a built-in id; returns <see cref="ThemeImportOutcome.AlreadyExists"/>
    /// without touching the file when a custom theme already holds the id and <paramref name="overwrite"/> is false.</summary>
    public static ThemeImportResult ImportInto(string themesFolder, byte[] bytes, bool overwrite = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(themesFolder);
        var check = ValidateImport(bytes);
        if (check.Invalid is not null) throw check.Invalid;
        if (check.Refusal == ThemeImportRefusal.BuiltInId)
        {
            throw new ArgumentException("The theme's id belongs to a built-in theme.", nameof(bytes));
        }

        var theme = check.Theme!;
        Directory.CreateDirectory(themesFolder);
        var path = Path.Combine(themesFolder, theme.Id + ".json");
        if (File.Exists(path) && !overwrite)
        {
            return new ThemeImportResult(ThemeImportOutcome.AlreadyExists, theme);
        }

        // Write the store's canonical form, not the raw upload, so a hand-edited file lands the same as a saved one.
        var canonical = ThemeStore.ParseUserTheme(DecodeUtf8(bytes)).CanonicalBytes;
        var tempPath = path + ".tmp";
        File.WriteAllBytes(tempPath, canonical);
        File.Move(tempPath, path, overwrite: true);
        return new ThemeImportResult(ThemeImportOutcome.Written, theme);
    }

    private static string DecodeUtf8(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
    }

    private static string SafeFileName(Theme theme)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(theme.Name.Length);
        foreach (var ch in theme.Name)
        {
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '-' : ch);
        }
        var name = sb.ToString().Trim().Trim('.');
        return (name.Length == 0 ? theme.Id : name) + ".json";
    }
}
