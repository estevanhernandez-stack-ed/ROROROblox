using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ROROROblox.Core.Theming;

/// <summary>
/// Default <see cref="IThemeStore"/> backed by built-in records + a JSON folder. Sanduhr-shape:
/// drop a file, theme appears at next list. Failures (missing fields, bad hex) are logged + the
/// file is skipped — never thrown — so a malformed user theme never breaks the picker.
/// </summary>
public sealed class ThemeStore : IThemeStore
{
    // snake_case JSON → PascalCase record properties. Mirrors Sanduhr's convention so the
    // AGENT_PROMPT examples are copy-paste compatible across both apps.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _userThemesFolder;
    private readonly IReadOnlyList<Theme> _builtIns;

    public ThemeStore() : this(DefaultUserThemesFolder()) { }

    public ThemeStore(string userThemesFolder)
    {
        _userThemesFolder = userThemesFolder ?? throw new ArgumentNullException(nameof(userThemesFolder));
        _builtIns = BuildBuiltIns();
    }

    public string UserThemesFolder => _userThemesFolder;

    public static string DefaultUserThemesFolder()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "ROROROblox", "themes");
    }

    public async Task<IReadOnlyList<Theme>> ListAsync()
    {
        var combined = new List<Theme>(_builtIns);

        try
        {
            Directory.CreateDirectory(_userThemesFolder);
        }
        catch
        {
            return combined;
        }

        IEnumerable<FileInfo> files;
        try
        {
            files = new DirectoryInfo(_userThemesFolder)
                .EnumerateFiles("*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return combined;
        }

        foreach (var file in files)
        {
            var theme = await TryLoadFileAsync(file).ConfigureAwait(false);
            if (theme is null) continue;
            // Drop user themes that collide with a built-in id — the built-in wins. Filename-id
            // is the stable handle, so the user can rename their file to reclaim the slot.
            if (combined.Any(t => string.Equals(t.Id, theme.Id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            combined.Add(theme);
        }

        return combined;
    }

    public async Task<Theme?> GetByIdAsync(string id)
    {
        var list = await ListAsync().ConfigureAwait(false);
        return list.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<Theme?> TryLoadFileAsync(FileInfo file)
    {
        try
        {
            await using var stream = File.OpenRead(file.FullName);
            var raw = await JsonSerializer.DeserializeAsync<RawTheme>(stream, JsonOptions).ConfigureAwait(false);
            if (raw is null)
            {
                return null;
            }

            // Filename minus .json -> theme id. Lowercase-kebab is the convention; we don't
            // enforce it — just pass through.
            var id = Path.GetFileNameWithoutExtension(file.Name);
            return raw.ToTheme(id, isBuiltIn: false);
        }
        catch
        {
            // Malformed JSON, missing required field, bad hex — silently drop. UI shows what's
            // valid; a user troubleshooting can check the log to see why a file didn't appear.
            return null;
        }
    }

    public async Task<Theme> SaveUserThemeAsync(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            throw new InvalidThemeException("Paste a theme JSON first.");
        }

        RawTheme? raw;
        try
        {
            raw = JsonSerializer.Deserialize<RawTheme>(rawJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidThemeException(
                $"Couldn't read the JSON: {ex.Message}. Make sure you pasted the whole object including the curly braces.",
                ex);
        }

        if (raw is null)
        {
            throw new InvalidThemeException("Empty theme JSON.");
        }

        if (string.IsNullOrWhiteSpace(raw.Name))
        {
            throw new InvalidThemeException("Theme JSON is missing the \"name\" field.");
        }

        // Filename derivation. lowercase + only alphanumeric/dash; spaces and other punctuation
        // collapse to single dashes. Two themes with similar names that normalize to the same
        // id will overwrite each other; that's acceptable for "the user pastes again" UX.
        var id = ToKebabId(raw.Name);
        if (string.IsNullOrEmpty(id))
        {
            throw new InvalidThemeException("Theme name produced an empty filename. Pick a different name.");
        }

        // Validate by constructing the Theme — this will throw on missing required fields with
        // a helpful message, which we wrap into our typed exception.
        Theme theme;
        try
        {
            theme = raw.ToTheme(id, isBuiltIn: false);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidThemeException(
                $"Theme JSON is missing a required field: {ex.Message}",
                ex);
        }

        Directory.CreateDirectory(_userThemesFolder);
        var path = Path.Combine(_userThemesFolder, id + ".json");
        // Round-trip via the canonical RawTheme shape so we don't accidentally save extra fields
        // a future Theme version might add (Id, IsBuiltIn) — those are inferred at load time.
        var canonical = JsonSerializer.SerializeToUtf8Bytes(raw, JsonOptions);
        var tempPath = path + ".tmp";
        await File.WriteAllBytesAsync(tempPath, canonical).ConfigureAwait(false);
        File.Move(tempPath, path, overwrite: true);

        return theme;
    }

    private static string ToKebabId(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        var lastWasDash = false;
        foreach (var ch in name.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastWasDash = false;
            }
            else if (!lastWasDash && sb.Length > 0)
            {
                sb.Append('-');
                lastWasDash = true;
            }
        }
        // Trim trailing dash.
        while (sb.Length > 0 && sb[^1] == '-')
        {
            sb.Length--;
        }
        return sb.ToString();
    }

    private static IReadOnlyList<Theme> BuildBuiltIns() => new List<Theme>
    {
        // Brand — the original 626 Labs cyan + magenta on navy. v1.1 default.
        new Theme(
            Id: "brand",
            Name: "Brand",
            Bg: "#0F1F31",
            Cyan: "#17D4FA",
            Magenta: "#F22F89",
            White: "#FFFFFF",
            MutedText: "#9AA8B8",
            Divider: "#1F3149",
            RowBg: "#15263A",
            RowExpiredBg: "#3A2D14",
            RowExpiredAccent: "#F1B232",
            Navy: "#0F1F31",
            IsBuiltIn: true),
        // Midnight — colder + dimmer for late-night play sessions. Same brand hues, lower
        // saturation, slightly darker base so the rows recede until you focus on them.
        new Theme(
            Id: "midnight",
            Name: "Midnight",
            Bg: "#0A1320",
            Cyan: "#3FB8D9",
            Magenta: "#C0407E",
            White: "#E6EDF5",
            MutedText: "#6F7E92",
            Divider: "#162232",
            RowBg: "#0F1B2B",
            RowExpiredBg: "#241B0E",
            RowExpiredAccent: "#C99A2D",
            Navy: "#0A1320",
            IsBuiltIn: true),
        // Magenta Heat — magenta-forward; flips the brand emphasis. Cyan keeps subtle accent
        // duty on selection dots + status. Designed to make Squad Launch / Stop CTAs lead.
        new Theme(
            Id: "magenta-heat",
            Name: "Magenta Heat",
            Bg: "#1A0F1F",
            Cyan: "#F22F89",
            Magenta: "#F22F89",
            White: "#FFE9F4",
            MutedText: "#B091A2",
            Divider: "#2D1832",
            RowBg: "#241432",
            RowExpiredBg: "#3A2D14",
            RowExpiredAccent: "#F1B232",
            Navy: "#1A0F1F",
            IsBuiltIn: true),
    };

    /// <summary>On-disk shape — same field set as <see cref="Theme"/> minus the id (which comes
    /// from the filename). Camelcase JSON ↔ PascalCase property via JsonSerializerOptions.</summary>
    private sealed record RawTheme(
        string? Name,
        string? Bg,
        string? Cyan,
        string? Magenta,
        string? White,
        string? MutedText,
        string? Divider,
        string? RowBg,
        string? RowExpiredBg,
        string? RowExpiredAccent,
        string? Navy)
    {
        public Theme ToTheme(string id, bool isBuiltIn)
        {
            // Required fields throw via the Theme record — bg/cyan/magenta/etc. must be present.
            // We let the throw propagate to TryLoadFileAsync's catch, which silently drops the file.
            return new Theme(
                Id: id,
                Name: Name ?? id,
                Bg: Bg ?? throw new InvalidOperationException("bg is required"),
                Cyan: Cyan ?? throw new InvalidOperationException("cyan is required"),
                Magenta: Magenta ?? throw new InvalidOperationException("magenta is required"),
                White: White ?? throw new InvalidOperationException("white is required"),
                MutedText: MutedText ?? throw new InvalidOperationException("mutedText is required"),
                Divider: Divider ?? throw new InvalidOperationException("divider is required"),
                RowBg: RowBg ?? throw new InvalidOperationException("rowBg is required"),
                RowExpiredBg: RowExpiredBg ?? throw new InvalidOperationException("rowExpiredBg is required"),
                RowExpiredAccent: RowExpiredAccent ?? throw new InvalidOperationException("rowExpiredAccent is required"),
                Navy: Navy ?? throw new InvalidOperationException("navy is required"),
                IsBuiltIn: isBuiltIn);
        }
    }
}
