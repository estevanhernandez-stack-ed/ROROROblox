using System.Security.Cryptography;
using System.Text.Json;

namespace ROROROblox.Core.Discord;

/// <summary>
/// DPAPI-encrypted (per-user, per-machine) Discord settings.
/// <para>
/// The May 2026 design stored this as plaintext JSON, reasoning that a webhook URL was "a
/// clan-shared resource, not a per-user secret." Two things make that wrong: one of the two
/// destinations is a private channel only its owner reads, and a webhook URL is a bearer
/// credential — whoever holds it posts to that channel as you, with no further authentication.
/// Same envelope as accounts.dat.
/// </para>
/// On tamper or a wrong-user envelope, returns defaults rather than throwing: a stray file must
/// not break startup.
/// </summary>
public sealed class DiscordConfigStore : IDiscordConfigStore
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public DiscordConfigStore(string filePath)
        => _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));

    public async Task<DiscordConfig> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new DiscordConfig();
        }

        try
        {
            var encrypted = await File.ReadAllBytesAsync(_filePath).ConfigureAwait(false);
            var decrypted = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<DiscordConfig>(decrypted, JsonOptions) ?? new DiscordConfig();
        }
        catch (CryptographicException) { return new DiscordConfig(); }
        catch (JsonException) { return new DiscordConfig(); }
    }

    public async Task SaveAsync(DiscordConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var json = JsonSerializer.SerializeToUtf8Bytes(config, JsonOptions);
        var encrypted = ProtectedData.Protect(json, optionalEntropy: null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        await File.WriteAllBytesAsync(_filePath, encrypted).ConfigureAwait(false);
    }
}
