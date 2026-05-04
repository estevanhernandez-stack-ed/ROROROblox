using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ROROROblox.Core;

/// <summary>
/// JSON-file-backed implementation of <see cref="IAppSettings"/>. File lives at
/// <c>%LOCALAPPDATA%\ROROROblox\settings.json</c>. Plaintext (no DPAPI) — these are
/// preferences, not secrets. Atomic write via tmp-file + rename. Thread-safe via
/// <see cref="SemaphoreSlim"/>.
/// </summary>
public sealed class AppSettings : IAppSettings, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);
    private bool _disposed;

    public AppSettings() : this(DefaultPath()) { }

    public AppSettings(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path must not be empty.", nameof(filePath));
        }
        _filePath = filePath;
    }

    public static string DefaultPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "ROROROblox", "settings.json");
    }

    public async Task<string?> GetDefaultPlaceUrlAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var settings = await LoadAsync().ConfigureAwait(false);
            return string.IsNullOrEmpty(settings.DefaultPlaceUrl) ? null : settings.DefaultPlaceUrl;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetDefaultPlaceUrlAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("Place URL must not be empty.", nameof(url));
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var settings = await LoadAsync().ConfigureAwait(false);
            settings = settings with { DefaultPlaceUrl = url };
            await SaveAsync(settings).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> GetLaunchMainOnStartupAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var settings = await LoadAsync().ConfigureAwait(false);
            return settings.LaunchMainOnStartup;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetLaunchMainOnStartupAsync(bool enabled)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var settings = await LoadAsync().ConfigureAwait(false);
            settings = settings with { LaunchMainOnStartup = enabled };
            await SaveAsync(settings).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> GetActiveThemeIdAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var settings = await LoadAsync().ConfigureAwait(false);
            return settings.ActiveThemeId;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetActiveThemeIdAsync(string themeId)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var settings = await LoadAsync().ConfigureAwait(false);
            settings = settings with { ActiveThemeId = themeId };
            await SaveAsync(settings).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SettingsBlob> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new SettingsBlob(Version: 1, DefaultPlaceUrl: null, LaunchMainOnStartup: false, ActiveThemeId: null);
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(_filePath).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return new SettingsBlob(Version: 1, DefaultPlaceUrl: null, LaunchMainOnStartup: false, ActiveThemeId: null);
        }

        if (bytes.Length == 0)
        {
            return new SettingsBlob(Version: 1, DefaultPlaceUrl: null, LaunchMainOnStartup: false, ActiveThemeId: null);
        }

        try
        {
            return JsonSerializer.Deserialize<SettingsBlob>(bytes, JsonOptions)
                ?? new SettingsBlob(Version: 1, DefaultPlaceUrl: null);
        }
        catch (JsonException)
        {
            // Corrupt settings file — return defaults rather than fail. The user can re-set in
            // the settings UI; we log nothing because we have no logger surface in Core yet.
            return new SettingsBlob(Version: 1, DefaultPlaceUrl: null, LaunchMainOnStartup: false, ActiveThemeId: null);
        }
    }

    private async Task SaveAsync(SettingsBlob blob)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(blob, JsonOptions);
        var tempPath = _filePath + ".tmp";
        await File.WriteAllBytesAsync(tempPath, bytes).ConfigureAwait(false);
        File.Move(tempPath, _filePath, overwrite: true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _gate.Dispose();
    }

    // SettingsBlob: missing fields decode as defaults (System.Text.Json), so older v1 blobs
    // without LaunchMainOnStartup load cleanly with it false — no migration step.
    private sealed record SettingsBlob(
        int Version,
        string? DefaultPlaceUrl,
        bool LaunchMainOnStartup = false,
        string? ActiveThemeId = null);
}
