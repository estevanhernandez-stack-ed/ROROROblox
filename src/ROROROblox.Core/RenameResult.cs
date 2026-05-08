namespace ROROROblox.Core;

/// <summary>
/// Outcome of the rename popup. Consumed by <c>MainViewModel.RenameItemCommand</c> in App.
/// v1.3.x. Spec §5.5 + §6.2 + §6.3.
/// </summary>
/// <param name="Kind">What the user did — saved, cancelled, or hit "Reset to original."</param>
/// <param name="NewName">
/// Trimmed local name on <see cref="RenameResultKind.Save"/>; <see langword="null"/> on
/// <see cref="RenameResultKind.Reset"/> or <see cref="RenameResultKind.Cancel"/>. Empty /
/// whitespace input on Save normalizes to <see langword="null"/> here too (effective reset).
/// </param>
public sealed record RenameResult(RenameResultKind Kind, string? NewName);

/// <summary>
/// Three terminal states of <c>RenameWindow.ShowAsync</c>. v1.3.x.
/// </summary>
public enum RenameResultKind
{
    Save,
    Cancel,
    Reset,
}
