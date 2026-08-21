using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using ROROROblox.App.Logging;

namespace ROROROblox.App.About;

internal partial class AboutWindow : Window
{
    private const string RepoUrl = "https://github.com/estevanhernandez-stack-ed/ROROROblox";
    private const string IssuesUrl = "https://github.com/estevanhernandez-stack-ed/ROROROblox/issues";

    // Easter egg: clicking the version number 6 OR 7 times reveals "Koii 4 eva". The exact
    // target is randomized per construction so the click count is non-deterministic.
    private readonly int _eggTarget = Random.Shared.Next(6, 8);
    private int _eggClicks;
    private bool _eggFired;

    public AboutWindow()
    {
        InitializeComponent();
        var version = typeof(AboutWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        VersionText.Text = $"v{version}";
    }

    private void OnRepoClick(object sender, RoutedEventArgs e) => OpenUrl(RepoUrl);
    private void OnIssuesClick(object sender, RoutedEventArgs e) => OpenUrl(IssuesUrl);

    private void OnLicenseClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AppLogging.LogDirectory,
                UseShellExecute = true,
                Verb = "open",
            });
        }
        catch
        {
            // Best-effort.
        }
    }

    /// <summary>
    /// F-039's remaining half. The tour is the only documentation of six unlabelled row
    /// affordances, and it is shown exactly once — before the user has any accounts, i.e. before
    /// any of those affordances exist to look at. Wave 3 gave it a door in the Tools menu; this is
    /// the one the row also asked for, in the window a person opens when they want to know what
    /// something is.
    /// <para>
    /// Owned by About rather than the main window: About is itself modal, and a tour parented past
    /// it would open behind the window that launched it.
    /// </para>
    /// </summary>
    private void OnWelcomeTourClick(object sender, RoutedEventArgs e) => WelcomeWindow.ShowTour(this);

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnVersionClicked(object sender, MouseButtonEventArgs e)
    {
        if (_eggFired) return;
        _eggClicks++;
        if (_eggClicks < _eggTarget) return;

        _eggFired = true;
        EasterEggText.Visibility = Visibility.Visible;
        var fade = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(380),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        EasterEggText.BeginAnimation(OpacityProperty, fade);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch
        {
            // Best-effort.
        }
    }
}
