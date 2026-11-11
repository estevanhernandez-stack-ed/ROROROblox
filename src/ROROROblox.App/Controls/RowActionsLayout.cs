using System.Windows;
using System.Windows.Controls;

namespace ROROROblox.App.Controls;

/// <summary>
/// The account row's grid. A <see cref="Grid"/> in every respect, plus one fact it remembers: the
/// width it was given to work with.
/// </summary>
/// <remarks>
/// The row's actions sit in an Auto column, and a Grid measures an Auto column with unlimited width,
/// so nothing inside that column can know how much room the row really has. This records it at the
/// only moment it is known, the row's own measure, so <see cref="ActionsRunPanel"/> can use it in
/// the same pass. See that class for why (2026-11-10).
/// </remarks>
public sealed class RowGrid : Grid
{
    /// <summary>The width the row was measured with. Infinity until the first measure.</summary>
    internal double WidthGiven { get; private set; } = double.PositiveInfinity;

    protected override Size MeasureOverride(Size constraint)
    {
        if (!WidthGiven.Equals(constraint.Width))
        {
            WidthGiven = constraint.Width;

            // The actions run is measured with unlimited width whatever the row is given, so WPF
            // would skip re-measuring it when only the row's width changed. It has to look again.
            foreach (UIElement child in InternalChildren)
            {
                if (child is ActionsRunPanel run)
                {
                    run.InvalidateMeasure();
                }
            }
        }

        return base.MeasureOverride(constraint);
    }

    /// <summary>
    /// The least width the columns before <paramref name="column"/> can take: a fixed column its
    /// width, a star column its MinWidth (it gives up everything above that before anything is
    /// cut), an Auto column the widest thing in it.
    /// </summary>
    internal double MinimumWidthBefore(int column)
    {
        var total = 0.0;
        for (var i = 0; i < column && i < ColumnDefinitions.Count; i++)
        {
            var definition = ColumnDefinitions[i];
            if (definition.Width.IsAbsolute)
            {
                total += Math.Max(definition.Width.Value, definition.MinWidth);
            }
            else if (definition.Width.IsStar)
            {
                total += definition.MinWidth;
            }
            else
            {
                var widest = definition.MinWidth;
                foreach (UIElement child in InternalChildren)
                {
                    if (GetColumn(child) != i || GetColumnSpan(child) != 1 || child.Visibility == Visibility.Collapsed)
                    {
                        continue;
                    }

                    // The Grid may not have reached this child yet in this pass. An Auto column is
                    // measured with unlimited width, so this is the measure the Grid would do anyway.
                    if (!child.IsMeasureValid)
                    {
                        child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    }

                    widest = Math.Max(widest, child.DesiredSize.Width);
                }

                total += widest;
            }
        }

        return total;
    }
}

/// <summary>
/// The account row's run of actions (swatch, star, Friends, game picker, FPS cap, Launch As,
/// Remove). A horizontal <see cref="StackPanel"/>, except that one child marked
/// <see cref="GivesWayProperty"/> is given only the width that is left once everything else has what
/// it needs.
/// </summary>
/// <remarks>
/// <para>
/// 2026-11-10. With no saved game the picker shows a hint ("Add in the Library") and stretches to
/// fit it. A plain StackPanel gives every child what it asks for, so the stretch came out of the
/// last child: in five of the six translations a new user's first row cut Remove off at the default
/// window size (French by 54 DIP), and English fit with nothing to spare. The picker is the one
/// thing in the run that can be narrower and still do its job (its hint and its game names trim),
/// so it is the one that yields.
/// </para>
/// <para>
/// This happens inside the measure pass. A first version set the hint's width from a SizeChanged
/// handler: it worked, but it needed a second layout pass, and the render tests, which share one
/// thread with every window an earlier test built, measured before that second pass arrived. A rule
/// that runs during measure has no second pass to wait for.
/// </para>
/// <para>
/// Outside a <see cref="RowGrid"/>, or with no marked child, this is exactly a StackPanel.
/// </para>
/// </remarks>
public sealed class ActionsRunPanel : StackPanel
{
    public static readonly DependencyProperty GivesWayProperty = DependencyProperty.RegisterAttached(
        "GivesWay", typeof(bool), typeof(ActionsRunPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static bool GetGivesWay(DependencyObject element) => (bool)element.GetValue(GivesWayProperty);

    public static void SetGivesWay(DependencyObject element, bool value) => element.SetValue(GivesWayProperty, value);

    /// <summary>
    /// The width left for the child that gives way. Pure, so the rule is testable without a window.
    /// </summary>
    /// <param name="rowWidth">The width the row was given.</param>
    /// <param name="beforeRun">The least width the columns before the actions can take.</param>
    /// <param name="restOfRun">Everything in the run except the child that gives way.</param>
    /// <param name="minimum">The child's own floor (its MinWidth plus its margins). It never gets less.</param>
    internal static double WidthLeftFor(double rowWidth, double beforeRun, double restOfRun, double minimum)
        => Math.Max(minimum, Math.Floor(rowWidth - beforeRun - restOfRun));

    protected override Size MeasureOverride(Size constraint)
    {
        // Everything at its natural size first, the yielding child included.
        var natural = base.MeasureOverride(constraint);

        if (Orientation != Orientation.Horizontal
            || Parent is not RowGrid row
            || double.IsInfinity(row.WidthGiven)
            || double.IsNaN(row.WidthGiven))
        {
            return natural;
        }

        FrameworkElement? yielding = null;
        foreach (UIElement child in InternalChildren)
        {
            if (child is FrameworkElement element && element.Visibility != Visibility.Collapsed && GetGivesWay(element))
            {
                yielding = element;
                break;
            }
        }

        if (yielding is null)
        {
            return natural;
        }

        var naturalWidth = yielding.DesiredSize.Width;
        var restOfRun = natural.Width - naturalWidth + Margin.Left + Margin.Right;
        var minimum = yielding.MinWidth + yielding.Margin.Left + yielding.Margin.Right;
        var limit = WidthLeftFor(row.WidthGiven, row.MinimumWidthBefore(Grid.GetColumn(this)), restOfRun, minimum);
        if (naturalWidth <= limit)
        {
            return natural;
        }

        // Measure the one child again, narrower. Its own contents decide how to fit: the hint and
        // the picker's game names both trim.
        yielding.Measure(new Size(limit, constraint.Height));
        return new Size(
            natural.Width - naturalWidth + yielding.DesiredSize.Width,
            Math.Max(natural.Height, yielding.DesiredSize.Height));
    }
}
