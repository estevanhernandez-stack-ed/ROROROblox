using System.Linq;
using ROROROblox.App.About;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// The About page's egg reveals "Koii 4 eva" after clicking the version number six OR seven times,
/// and which one it is stays unanswerable on purpose.
/// <para>
/// <b>Why this is worth a test at all.</b> "6-7" is the meme the Pet Sim 99 audience chants, and the
/// joke only works because nobody can pin the number down. That depends entirely on
/// <c>Random.Shared.Next(6, 8)</c> — an exclusive upper bound. Someone tidying that to
/// <c>Next(6, 7)</c>, which reads more natural and is the obvious "fix" for anyone who thinks the 8
/// is a typo, makes the egg fire on six every single time. Nothing would fail, nobody would notice,
/// and the joke would be gone. An Easter egg is exactly the kind of thing that breaks silently for a
/// year, because the only person who would catch it is someone who already knows what it should do.
/// </para>
/// <para>
/// <b>Why a class instead of testing the page.</b> The decision used to be three fields and four
/// lines inside <c>AboutPage.OnVersionClicked</c>, mixed in with a <c>DoubleAnimation</c>, and the
/// target came from <c>Random.Shared</c> at a field initialiser, so neither branch could be pinned.
/// Same split <c>MetricAlertsGateTests</c> records: the decision moves to a plain class that takes
/// its target, and the WPF half — the fade, the visibility — stays with the smoke run.
/// </para>
/// </summary>
public class EasterEggCounterTests
{
    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    public void TheEggFiresOnTheTargetClick_AndNotBefore(int target)
    {
        var counter = new EasterEggCounter(target);

        for (var click = 1; click < target; click++)
        {
            Assert.False(counter.Click(), $"click {click} of {target} fired early.");
            Assert.False(counter.HasFired);
        }

        Assert.True(counter.Click(), $"click {target} of {target} did not fire.");
        Assert.True(counter.HasFired);
    }

    [Fact]
    public void OnceFired_ItNeverFiresAgain()
    {
        var counter = new EasterEggCounter(6);
        for (var click = 1; click < 6; click++) counter.Click();
        Assert.True(counter.Click());

        // The page would otherwise replay its fade on every later click.
        Assert.False(counter.Click());
        Assert.False(counter.Click());
        Assert.True(counter.HasFired);
    }

    /// <summary>
    /// The one that guards the joke. Both six and seven must be reachable: an egg that always fires
    /// on six is not the 6-7 bit, it is a six-tap egg wearing its name.
    /// </summary>
    [Fact]
    public void TheRandomTarget_IsSometimesSixAndSometimesSeven_AndNeverAnythingElse()
    {
        var seen = Enumerable.Range(0, 500)
            .Select(_ => EasterEggCounter.CreateRandom().Target)
            .ToHashSet();

        Assert.Contains(6, seen);
        Assert.Contains(7, seen);
        Assert.True(
            seen.SetEquals(new[] { 6, 7 }),
            $"the target must only ever be 6 or 7; saw {string.Join(", ", seen.OrderBy(n => n))}. " +
            "Next(6, 8) has an EXCLUSIVE upper bound — 'tidying' it to Next(6, 7) pins the egg to six.");
    }
}
