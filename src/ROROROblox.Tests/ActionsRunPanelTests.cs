using ROROROblox.App.Controls;

namespace ROROROblox.Tests;

/// <summary>
/// The arithmetic behind <see cref="ActionsRunPanel"/> (2026-11-10): the width left for the one
/// child of the account row's actions that gives way, once everything else has what it needs. The
/// layout it drives is measured in seven languages by <c>LabelWrapMeasurementTests</c>; this is the
/// rule on its own.
/// </summary>
public class ActionsRunPanelTests
{
    [Fact]
    public void WithRoomToSpare_TheChildMayUseAllOfIt()
    {
        // 825 wide, 244 before the run, 430 of other actions.
        Assert.Equal(151, ActionsRunPanel.WidthLeftFor(rowWidth: 825, beforeRun: 244, restOfRun: 430, minimum: 128));
    }

    [Fact]
    public void ANarrowerRow_LeavesExactlyThatMuchLess()
    {
        var wide = ActionsRunPanel.WidthLeftFor(825, 244, 420, 128);
        var narrow = ActionsRunPanel.WidthLeftFor(815, 244, 420, 128);

        Assert.Equal(10, wide - narrow);
    }

    [Fact]
    public void WiderButtonsElsewhere_ComeOutOfThisChild_NotOutOfTheLastButton()
    {
        // French "Lancer en tant que" is wider than "Launch As". That width has to come from
        // somewhere, and the picker is the thing that can spare it.
        var english = ActionsRunPanel.WidthLeftFor(825, 244, 400, 128);
        var french = ActionsRunPanel.WidthLeftFor(825, 244, 440, 128);

        Assert.Equal(40, english - french);
    }

    [Fact]
    public void WithNoRoomAtAll_TheChildKeepsItsOwnFloor()
    {
        // The picker never drops below its MinWidth. Past that point the row simply does not fit,
        // and squeezing the picker further would only make it unusable as well.
        Assert.Equal(128, ActionsRunPanel.WidthLeftFor(rowWidth: 600, beforeRun: 244, restOfRun: 430, minimum: 128));
    }

    [Fact]
    public void AFractionalWidth_RoundsDown_SoRoundingNeverCostsTheLastButtonAPixel()
    {
        Assert.Equal(151, ActionsRunPanel.WidthLeftFor(825.7, 244, 430, 128));
    }
}
