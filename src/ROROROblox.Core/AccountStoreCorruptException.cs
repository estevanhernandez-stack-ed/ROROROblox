namespace ROROROblox.Core;

/// <summary>
/// Raised when <c>accounts.dat</c> can't be decrypted or parsed — typically means Windows was
/// restored from a backup that didn't preserve the DPAPI master key, the SID changed, the user
/// profile is corrupt, or the file was copied from another machine. Spec §7.4. The MainWindow
/// (item 9) catches this and surfaces the [Start Fresh] / [Quit] modal.
/// </summary>
public sealed class AccountStoreCorruptException : Exception
{
    public AccountStoreCorruptException(string message) : base(message) { }
    public AccountStoreCorruptException(string message, Exception inner) : base(message, inner) { }
}
