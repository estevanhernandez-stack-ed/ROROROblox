using System.Windows;
using System.Windows.Controls;
using ROROROblox.App.Localization;
using ROROROblox.Core;

namespace ROROROblox.App.KnownIssues;

/// <summary>
/// Tools › Known Roblox issues (spec §3). Every entry, serious first, whatever the running version —
/// the version decides only the notice and the count. Rebuilt whenever the model changes or the UI
/// language does; disposed by the shell when its window closes.
/// </summary>
internal sealed partial class KnownRobloxIssuesPage : UserControl, IDisposable
{
    private readonly KnownIssuesNoticeModel _model;
    private readonly Action<KnownIssueFeatureRoute> _goToFeature;
    private readonly IShellOpener _shellOpener;

    public KnownRobloxIssuesPage(KnownIssuesNoticeModel model, Action<KnownIssueFeatureRoute> goToFeature, IShellOpener shellOpener)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _goToFeature = goToFeature ?? throw new ArgumentNullException(nameof(goToFeature));
        _shellOpener = shellOpener ?? throw new ArgumentNullException(nameof(shellOpener));
        InitializeComponent();

        _model.Changed += OnModelChanged;
        TranslationSource.Instance.CultureChanged += OnCultureChanged;
        Rebuild();
    }

    public void Dispose()
    {
        _model.Changed -= OnModelChanged;
        TranslationSource.Instance.CultureChanged -= OnCultureChanged;
    }

    private void OnModelChanged(object? sender, EventArgs e) => Rebuild();

    private void OnCultureChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        var culture = TranslationSource.Instance.CurrentCulture;
        var views = _model.PageIssues.Select(i => KnownIssueView.From(i, _model.RunningVersion, culture)).ToList();

        var line = KnownIssuesStatusLine.For(
            _model.Source, views.Count, _model.CheckedAt, _model.LastOutcome, DateTimeOffset.Now, culture);

        IssueList.ItemsSource = views;
        EmptyText.Visibility = views.Count == 0 && line.ShowAllClear ? Visibility.Visible : Visibility.Collapsed;
        EnglishOnlyText.Visibility = views.Count > 0 && culture.TwoLetterISOLanguageName != "en"
            ? Visibility.Visible
            : Visibility.Collapsed;
        LastCheckedText.Text = line.Text;
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: KnownIssueView view } && view.HelpRoute != KnownIssueFeatureRoute.None)
        {
            _goToFeature(view.HelpRoute);
        }
    }

    private void OnLinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: KnownIssueLinkView link })
        {
            _shellOpener.Open(link.Url);
        }
    }
}
