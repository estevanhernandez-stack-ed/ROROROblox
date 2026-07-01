using ROROROblox.App.ViewModels;
using Xunit;

namespace ROROROblox.Tests;

public class IdleSummaryTests
{
    [Fact]
    public void Format_Zero_ReturnsEmpty() => Assert.Equal("", IdleSummary.Format(0, 15));

    [Fact]
    public void Format_One_Singular() =>
        Assert.Equal("1 account idle > 15m", IdleSummary.Format(1, 15));

    [Fact]
    public void Format_Many_Plural() =>
        Assert.Equal("3 accounts idle > 15m", IdleSummary.Format(3, 15));
}
