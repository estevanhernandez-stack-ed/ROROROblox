using System.Globalization;
using ROROROblox.App.Localization;

namespace ROROROblox.Tests;

/// <summary>
/// The CLDR cardinal plural selector (localization Phase D). English has 2 forms; Russian and
/// Polish have 3 for integer counts (one/few/many) with non-obvious rules — getting these wrong
/// ships subtly broken grammar, which is exactly what Phase D exists to prevent. Vectors below are
/// the CLDR cardinal rules for each shipped language.
/// </summary>
public class PluralsTests
{
    private static string Cat(string lang, long n) => Plurals.Category(CultureInfo.GetCultureInfo(lang), n);

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("es")]
    public void TwoFormLanguages_OneForExactlyOne(string lang)
    {
        Assert.Equal("one", Cat(lang, 1));
        foreach (var n in new long[] { 0, 2, 11, 21, 100, 1000 })
            Assert.Equal("other", Cat(lang, n));
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("pt-BR")]
    public void FrenchAndPortuguese_OneForZeroAndOne(string lang)
    {
        Assert.Equal("one", Cat(lang, 0));
        Assert.Equal("one", Cat(lang, 1));
        foreach (var n in new long[] { 2, 11, 21, 100 })
            Assert.Equal("other", Cat(lang, n));
    }

    [Theory]
    // one: n%10==1 and n%100!=11
    [InlineData(1, "one")]
    [InlineData(21, "one")]
    [InlineData(101, "one")]
    // few: n%10 in 2..4 and n%100 not in 12..14
    [InlineData(2, "few")]
    [InlineData(4, "few")]
    [InlineData(22, "few")]
    [InlineData(24, "few")]
    // many: everything else integer (n%10 0 or 5..9, or n%100 11..14)
    [InlineData(0, "many")]
    [InlineData(5, "many")]
    [InlineData(11, "many")]
    [InlineData(12, "many")]
    [InlineData(14, "many")]
    [InlineData(25, "many")]
    [InlineData(100, "many")]
    [InlineData(111, "many")]
    public void Russian_FollowsCldrCardinal(long n, string expected) => Assert.Equal(expected, Cat("ru", n));

    [Theory]
    // one: exactly 1
    [InlineData(1, "one")]
    // few: n%10 in 2..4 and n%100 not in 12..14
    [InlineData(2, "few")]
    [InlineData(4, "few")]
    [InlineData(22, "few")]
    [InlineData(24, "few")]
    // many: everything else integer (incl. n%10 0/1 when n!=1, n%10 5..9, n%100 12..14)
    [InlineData(0, "many")]
    [InlineData(5, "many")]
    [InlineData(11, "many")]
    [InlineData(12, "many")]
    [InlineData(14, "many")]
    [InlineData(21, "many")]
    [InlineData(25, "many")]
    [InlineData(112, "many")]
    public void Polish_FollowsCldrCardinal(long n, string expected) => Assert.Equal(expected, Cat("pl", n));

    [Fact]
    public void Required_CategoriesMatchTheRules()
    {
        // The lint plural guard reads this to check each language ships every form it needs.
        Assert.Equal(new[] { "one", "other" }, Plurals.Required["en"]);
        Assert.Equal(new[] { "one", "few", "many" }, Plurals.Required["ru"]);
        Assert.Equal(new[] { "one", "few", "many" }, Plurals.Required["pl"]);
    }
}
