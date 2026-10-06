using System;

namespace ROROROblox.App.About;

/// <summary>
/// Counts clicks on the About page's version number and says when the egg fires. Reveals
/// "Koii 4 eva" after six OR seven clicks, and which one it is stays unanswerable on purpose.
/// <para>
/// <b>The number is the joke.</b> "6-7" is what the Pet Sim 99 audience chants at each other all
/// day, and it only works because nobody can pin it down. So the target is drawn per instance and
/// the egg genuinely takes six clicks sometimes and seven others — two people comparing notes
/// disagree honestly. A fixed six would be a six-tap egg wearing the name of the bit.
/// </para>
/// <para>
/// <b>Why the target is a constructor parameter.</b> It used to be
/// <c>Random.Shared.Next(6, 8)</c> at a field initialiser inside <c>AboutPage</c>, which no test
/// could pin, so neither branch was assertable and the exclusive upper bound had nothing guarding
/// it. <c>Next(6, 8)</c> reads like a typo to anyone who has not met this comment; "fixing" it to
/// <c>Next(6, 7)</c> silently pins the egg to six forever. <see cref="EasterEggCounterTests"/> now
/// fails if that happens. The page keeps the half that cannot be tested — the fade and the
/// visibility flip.
/// </para>
/// </summary>
public sealed class EasterEggCounter
{
    private int _clicks;

    public EasterEggCounter(int target)
    {
        if (target < 1) throw new ArgumentOutOfRangeException(nameof(target), target, "An egg needs at least one click.");
        Target = target;
    }

    /// <summary>Clicks required this time round: six or seven when built by <see cref="CreateRandom"/>.</summary>
    public int Target { get; }

    /// <summary>True once the egg has fired; it only ever fires once.</summary>
    public bool HasFired { get; private set; }

    /// <summary>
    /// Six or seven, drawn now. The upper bound is EXCLUSIVE — <c>Next(6, 8)</c> yields 6 or 7, and
    /// that second value is the entire joke.
    /// </summary>
    public static EasterEggCounter CreateRandom() => new(Random.Shared.Next(6, 8));

    /// <summary>
    /// Records a click. Returns true on the click that fires the egg, and only that one, so the
    /// caller can run its reveal exactly once rather than replaying a fade on every later click.
    /// </summary>
    public bool Click()
    {
        if (HasFired) return false;
        if (++_clicks < Target) return false;

        HasFired = true;
        return true;
    }
}
