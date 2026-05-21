namespace ROROROblox.App.Transport;

/// <summary>
/// Pure passphrase-strength evaluation for the export dialog (v1.6.0 — spec §1 "Export flow").
/// No WPF, no I/O — unit-testable in isolation. The export bundle protects <c>.ROBLOSECURITY</c>
/// cookies behind PBKDF2-600k + AES-256-GCM; the only thing standing between a leaked bundle and an
/// offline brute-force is the passphrase. So the gate is enforced, not advisory: <see cref="IsAcceptable"/>
/// is the hard floor the Export button waits on (≥ <see cref="MinimumLength"/> chars), and
/// <see cref="Evaluate"/> drives the live meter.
///
/// SECURITY-SENSITIVE: this type only reads length + character classes. It never logs, persists, or
/// returns the passphrase itself.
/// </summary>
public static class PassphraseStrength
{
    /// <summary>Enforced floor (spec §1). Below this the Export button stays disabled.</summary>
    public const int MinimumLength = 12;

    /// <summary>
    /// The hard gate the Export button binds to. True only when the passphrase clears the length
    /// floor (whitespace is NOT trimmed away — a 12-space passphrase is rejected because it has no
    /// non-whitespace content, which is the "obviously weak" case the spec calls out). Strength
    /// label/score above the floor is advisory; this predicate is the bar.
    /// </summary>
    public static bool IsAcceptable(string? passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
        {
            return false;
        }
        return passphrase.Length >= MinimumLength;
    }

    /// <summary>
    /// Score (0..4) + a human label for the live meter. Score rises with both length and character
    /// variety so "aaaaaaaaaaaa" reads weaker than a mixed-class passphrase of the same length, and a
    /// long passphrase reads stronger than a short one. Empty / whitespace-only → <see cref="0"/> /
    /// "Too short".
    /// </summary>
    /// <returns>
    /// A tuple of the integer score (0 = weakest, 4 = strongest) and the display label.
    /// </returns>
    public static (int Score, string Label) Evaluate(string? passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
        {
            return (0, "Too short");
        }

        int length = passphrase.Length;

        // Character-class variety: lower, upper, digit, symbol. More classes = harder to brute-force
        // at a given length.
        bool hasLower = false, hasUpper = false, hasDigit = false, hasSymbol = false;
        foreach (char c in passphrase)
        {
            if (char.IsLower(c)) hasLower = true;
            else if (char.IsUpper(c)) hasUpper = true;
            else if (char.IsDigit(c)) hasDigit = true;
            else if (!char.IsWhiteSpace(c)) hasSymbol = true;
        }
        int classes = (hasLower ? 1 : 0) + (hasUpper ? 1 : 0) + (hasDigit ? 1 : 0) + (hasSymbol ? 1 : 0);

        // Length points: nothing below the floor scores above "weak" — the meter should never tell a
        // user a too-short passphrase is "good." At/above the floor, points climb with length.
        int lengthPoints;
        if (length < MinimumLength) lengthPoints = 0;
        else if (length < 16) lengthPoints = 1;
        else if (length < 20) lengthPoints = 2;
        else lengthPoints = 3;

        // Variety points: a passphrase with 3+ classes earns the variety bonus; 2 classes a partial one.
        int varietyPoints = classes >= 3 ? 2 : classes >= 2 ? 1 : 0;

        // Below the floor we cap the score so the meter reads "weak" no matter the variety —
        // the floor is the bar, and the meter must not contradict it.
        if (length < MinimumLength)
        {
            // 0..1 only: a short-but-varied passphrase is still short.
            int shortScore = varietyPoints > 0 ? 1 : 0;
            return (shortScore, shortScore == 0 ? "Too short" : "Too short");
        }

        int raw = lengthPoints + varietyPoints; // 1..5 at/above the floor
        int score = Math.Clamp(raw, 1, 4);

        string label = score switch
        {
            1 => "Weak",
            2 => "Fair",
            3 => "Strong",
            _ => "Very strong",
        };
        return (score, label);
    }
}
