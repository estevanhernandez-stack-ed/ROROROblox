using System.Windows;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.App.Modals;

/// <summary>
/// Warns before a launch the machine probably cannot hold. <c>DialogResult == true</c> means the
/// user chose to go ahead anyway. Advisory only — see the markup for why this does not block.
/// </summary>
internal partial class LaunchHeadroomWindow : Window
{
    private bool _answered;

    public LaunchHeadroomWindow(LaunchHeadroomAdvisor.Verdict verdict, int requested, int roomFor,
        long availableBytes, long aggregateClientBytes)
    {
        InitializeComponent();

        var gb = (double b) => b / 1024d / 1024d / 1024d;

        Heading.Text = verdict == LaunchHeadroomAdvisor.Verdict.WontFit
            ? "There may not be room for another client"
            : $"There may only be room for {roomFor} more";

        // Says "may". The footprint is a measured constant, not a promise about this machine and
        // this game — overstating it would be the same mistake as blocking on it.
        BodyText.Text = requested == 1
            ? "A Roblox client typically needs about 2.6 GB. Starting one now could push the "
              + "machine past what it can hold, and Windows may close a client that is already "
              + "running to make space."
            : $"You are launching {requested} accounts, and a Roblox client typically needs about "
              + "2.6 GB each. Windows may close clients that are already running to make space.";

        Numbers.Text =
            $"{gb(availableBytes):F1} GB free  ·  {gb(aggregateClientBytes):F1} GB held by "
            + $"{(aggregateClientBytes > 0 ? "running clients" : "nothing yet")}  ·  room for about {roomFor} more";
    }

    /// <summary>
    /// True once a labelled button was pressed. WPF closes a modal with <c>DialogResult == false</c>
    /// whether the user chose Cancel or clicked the X, so <c>DialogResult</c> alone cannot tell an
    /// answer from a dismissal — the same trap the theme-consent dialog hit, verified there by
    /// sending WM_CLOSE to a live window.
    /// </summary>
    internal bool Answered => _answered;

    /// <summary>
    /// Asks, if there is anything worth asking about, and reports whether to proceed. Returns true
    /// when the launch should go ahead — including every case where there is nothing to warn about,
    /// so callers can treat this as a plain "may I continue?".
    /// </summary>
    internal static bool ShouldProceed(
        MemoryPressureSnapshot snapshot, long reserveBytes, int requested, Window? owner)
    {
        // Reads the watchdog's own sample rather than taking a fresh probe: the view model already
        // holds it, memory does not move meaningfully in 30 seconds, and this avoids adding a
        // dependency to a constructor whose own comment complains about having twenty-odd.
        //
        // AvailableBytes is 0 when the probe FAILED (the watchdog collapses both cases writing the
        // snapshot), so a zero has to be read as UNKNOWN, not as "no memory left". Warning on a
        // failed read would fire this dialog on every launch for anyone whose probe is broken.
        var available = snapshot.AvailableBytes;
        var aggregateClientBytes = snapshot.AggregateClientBytes;
        var (verdict, roomFor) = LaunchHeadroomAdvisor.Evaluate(available > 0, available, reserveBytes, requested);

        // Fits, or we could not measure. An UNKNOWN reading must not manufacture a warning any more
        // than it may manufacture reassurance — it simply has nothing to say.
        if (verdict is LaunchHeadroomAdvisor.Verdict.Fits or LaunchHeadroomAdvisor.Verdict.Unknown)
        {
            return true;
        }

        var dialog = new LaunchHeadroomWindow(verdict, requested, roomFor, available, aggregateClientBytes);
        if (owner is not null && owner.IsLoaded)
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.ShowDialog();

        // Dismissed with the X is not consent to launch. Unlike the theme dialog — where declining
        // and dismissing both leave the theme alone — here the two outcomes differ, so an ambiguous
        // close must take the safe one.
        return dialog.Answered && dialog.DialogResult == true;
    }

    private void OnLaunchClick(object sender, RoutedEventArgs e)
    {
        _answered = true;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        _answered = true;
        DialogResult = false;
        Close();
    }
}
