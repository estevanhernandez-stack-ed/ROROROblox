namespace ROROROblox.Core.Transport;

/// <summary>
/// The single failure type surfaced by <see cref="IAccountTransport.Import"/>. SECURITY-SENSITIVE:
/// a wrong passphrase, a tampered ciphertext/tag, and a malformed header all throw THIS type with
/// the SAME message — the caller can't distinguish "wrong passphrase" from "damaged file," which
/// closes the oracle that would otherwise help an attacker confirm a guessed passphrase. The inner
/// exception (the underlying <see cref="System.Security.Cryptography.CryptographicException"/>) is
/// kept for diagnostics but never carries the passphrase, key, or plaintext.
/// </summary>
public sealed class AccountTransportException : Exception
{
    public AccountTransportException(string message) : base(message) { }
    public AccountTransportException(string message, Exception inner) : base(message, inner) { }
}
