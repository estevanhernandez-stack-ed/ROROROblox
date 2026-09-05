using System.Security.Cryptography;
using System.Text.Json;

namespace ROROROblox.Core.Notify;

public interface IPhoneNotifyConfigStore
{
    Task<PhoneNotifyConfig> LoadAsync();
    Task SaveAsync(PhoneNotifyConfig config);
}

/// <summary>
/// DPAPI-encrypted (per-user, per-machine) phone-alert settings — <c>notify.dat</c> beside
/// <c>discord.dat</c>, same envelope as <c>DiscordConfigStore</c> and for the same reason: every
/// field in the record is a bearer credential (see <see cref="PhoneNotifyConfig"/>).
/// On tamper or a wrong-user envelope, returns defaults rather than throwing: a stray file must
/// not break startup.
/// </summary>
public sealed class PhoneNotifyConfigStore : IPhoneNotifyConfigStore
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public PhoneNotifyConfigStore(string filePath)
        => _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));

    public async Task<PhoneNotifyConfig> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new PhoneNotifyConfig();
        }

        try
        {
            var encrypted = await File.ReadAllBytesAsync(_filePath).ConfigureAwait(false);
            var decrypted = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<PhoneNotifyConfig>(decrypted, JsonOptions) ?? new PhoneNotifyConfig();
        }
        catch (CryptographicException) { return new PhoneNotifyConfig(); }
        catch (JsonException) { return new PhoneNotifyConfig(); }
    }

    public async Task SaveAsync(PhoneNotifyConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var json = JsonSerializer.SerializeToUtf8Bytes(config, JsonOptions);
        var encrypted = ProtectedData.Protect(json, optionalEntropy: null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

        // tmp + rename (the AccountStore pattern): a torn notify.dat silently resets to defaults
        // on the next load, and unlike a webhook URL the ntfy topic inside cannot be re-pasted —
        // it would have to be regenerated and re-subscribed on the phone, the exact cost the
        // Settings page puts behind a confirm dialog (review 2026-09-04).
        var tmp = _filePath + ".tmp";
        await File.WriteAllBytesAsync(tmp, encrypted).ConfigureAwait(false);
        File.Move(tmp, _filePath, overwrite: true);
    }
}
