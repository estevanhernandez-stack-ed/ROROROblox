namespace ROROROblox.Core.Transport;

/// <summary>
/// The single failure type surfaced by <see cref="IAccountTransport.Import"/>. SECURITY-SENSITIVE:
/// a wrong passphrase, a tampered ciphertext/tag, and a malformed header all throw THIS type — the
/// caller can't distinguish "wrong passphrase" from "damaged file," which closes the oracle that
/// would otherwise help an attacker confirm a guessed passphrase. The inner exception (the
/// underlying <see cref="System.Security.Cryptography.CryptographicException"/>) is kept for
/// diagnostics but never carries the passphrase, key, or plaintext.
/// <para>
/// It carries NO caller-supplied message (localization Phase D 2026-09-07, the Core string
/// boundary): the user-facing sentence is prose the App owns and renders from resx
/// (<c>CoreMsg_Transport_ImportFailed</c>), not something Core hands over. The fixed
/// <see cref="Diagnostic"/> below is an invariant-English log/stack-trace string — never shown to a
/// viewer — and is deliberately identical for every failure mode, so nothing distinguishes the two
/// even in a log. Removing the message ctor also means prose CANNOT be reintroduced by construction.
/// </para>
/// </summary>
public sealed class AccountTransportException : Exception
{
    private const string Diagnostic = "Account transport bundle could not be opened.";

    public AccountTransportException() : base(Diagnostic) { }
    public AccountTransportException(Exception inner) : base(Diagnostic, inner) { }
}
