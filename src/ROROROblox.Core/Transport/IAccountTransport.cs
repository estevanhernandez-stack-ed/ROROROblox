namespace ROROROblox.Core.Transport;

/// <summary>
/// Pure crypto for the passphrase-protected account transport bundle (spec §1).
/// No DPAPI, no UI, no file I/O — the caller supplies decrypted records on export and
/// re-encrypts on import. This boundary keeps the crypto unit-testable and keeps the
/// at-rest DPAPI model out of the transport path entirely.
/// </summary>
public interface IAccountTransport
{
    /// <summary>
    /// Serialize <paramref name="records"/>, derive a key from <paramref name="passphrase"/>,
    /// AES-256-GCM encrypt, and return the versioned binary bundle (magic | version | iters |
    /// salt | nonce | tag | ciphertext). A fresh random salt and nonce are generated per call,
    /// so two exports of identical input produce different bytes.
    /// </summary>
    byte[] Export(IReadOnlyList<AccountExportRecord> records, string passphrase);

    /// <summary>
    /// Validate the bundle header, derive the key from <paramref name="passphrase"/> + the
    /// header salt, AES-256-GCM decrypt (tag verified inside the BCL), and return the records.
    /// Fails closed: a wrong passphrase, a tampered file, or a malformed header all throw
    /// <see cref="AccountTransportException"/> with the same message — no oracle leak, never
    /// partial data.
    /// </summary>
    IReadOnlyList<AccountExportRecord> Import(byte[] bundle, string passphrase);
}
