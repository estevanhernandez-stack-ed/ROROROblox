using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ROROROblox.Core;

/// <summary>
/// DPAPI-enveloped JSON store of accounts. Whole-blob encryption for v1.1 — simpler than per-cookie;
/// v1.2 upgrades alongside per-account WebView2 profiles (spec §5.4 decisions).
/// Atomic write via temp-file + <see cref="File.Move(string, string, bool)"/>. Thread-safe via a
/// <see cref="SemaphoreSlim"/> gate. Decrypt failures become <see cref="AccountStoreCorruptException"/>.
/// </summary>
public sealed class AccountStore : IAccountStore, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);
    private bool _disposed;

    public AccountStore() : this(DefaultPath()) { }

    public AccountStore(string filePath)
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
        return Path.Combine(localAppData, "ROROROblox", "accounts.dat");
    }

    public async Task<IReadOnlyList<Account>> ListAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            return blob.Accounts
                .Select(a => new Account(a.Id, a.DisplayName, a.AvatarUrl, a.CreatedAt, a.LastLaunchedAt))
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Account> AddAsync(string displayName, string avatarUrl, string cookie)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name must not be empty.", nameof(displayName));
        }
        if (string.IsNullOrEmpty(cookie))
        {
            throw new ArgumentException("Cookie must not be empty.", nameof(cookie));
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var stored = new StoredAccount(
                Id: Guid.NewGuid(),
                DisplayName: displayName,
                AvatarUrl: avatarUrl ?? string.Empty,
                Cookie: cookie,
                CreatedAt: DateTimeOffset.UtcNow,
                LastLaunchedAt: null);
            blob.Accounts.Add(stored);
            await SaveAsync(blob).ConfigureAwait(false);
            return new Account(stored.Id, stored.DisplayName, stored.AvatarUrl, stored.CreatedAt, stored.LastLaunchedAt);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(Guid id)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            blob.Accounts.RemoveAll(a => a.Id == id);
            await SaveAsync(blob).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> RetrieveCookieAsync(Guid id)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var match = blob.Accounts.FirstOrDefault(a => a.Id == id)
                ?? throw new KeyNotFoundException($"Account {id} not found.");
            return match.Cookie;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateCookieAsync(Guid id, string newCookie)
    {
        if (string.IsNullOrEmpty(newCookie))
        {
            throw new ArgumentException("Cookie must not be empty.", nameof(newCookie));
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var index = blob.Accounts.FindIndex(a => a.Id == id);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Account {id} not found.");
            }
            blob.Accounts[index] = blob.Accounts[index] with { Cookie = newCookie };
            await SaveAsync(blob).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task TouchLastLaunchedAsync(Guid id)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var index = blob.Accounts.FindIndex(a => a.Id == id);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Account {id} not found.");
            }
            blob.Accounts[index] = blob.Accounts[index] with { LastLaunchedAt = DateTimeOffset.UtcNow };
            await SaveAsync(blob).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<StoredAccountsBlob> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new StoredAccountsBlob(Version: 1, Accounts: []);
        }

        byte[] encrypted;
        try
        {
            encrypted = await File.ReadAllBytesAsync(_filePath).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            throw new AccountStoreCorruptException($"Could not read {_filePath}.", ex);
        }

        if (encrypted.Length == 0)
        {
            return new StoredAccountsBlob(Version: 1, Accounts: []);
        }

        byte[] plaintext;
        try
        {
            plaintext = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException ex)
        {
            throw new AccountStoreCorruptException(
                "DPAPI decrypt failed; accounts.dat is unreadable on this PC. " +
                "Likely cause: Windows restored from backup that didn't preserve the DPAPI master key, " +
                "SID change, or the file was copied from another machine.",
                ex);
        }

        try
        {
            var blob = JsonSerializer.Deserialize<StoredAccountsBlob>(plaintext, SerializerOptions);
            return blob ?? new StoredAccountsBlob(Version: 1, Accounts: []);
        }
        catch (JsonException ex)
        {
            throw new AccountStoreCorruptException("accounts.dat decrypted but its JSON shape is invalid.", ex);
        }
    }

    private async Task SaveAsync(StoredAccountsBlob blob)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(blob, SerializerOptions);
        var encrypted = ProtectedData.Protect(json, optionalEntropy: null, DataProtectionScope.CurrentUser);

        var tempPath = _filePath + ".tmp";
        await File.WriteAllBytesAsync(tempPath, encrypted).ConfigureAwait(false);
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

    // Internal storage shape — the encrypted blob payload. Cookie lives here, never on the public Account.
    internal sealed record StoredAccount(
        Guid Id,
        string DisplayName,
        string AvatarUrl,
        string Cookie,
        DateTimeOffset CreatedAt,
        DateTimeOffset? LastLaunchedAt);

    internal sealed record StoredAccountsBlob(
        int Version,
        List<StoredAccount> Accounts);
}
