using System.Windows;
using System.Windows.Controls;

namespace ROROROblox.App.Controls;

/// <summary>
/// The one page header (F-007). See PageHeader.xaml for why the wordmark defaults off and why the
/// hierarchy is carried by size and weight rather than colour.
/// </summary>
internal partial class PageHeader : UserControl
{
    public PageHeader() => InitializeComponent();

    /// <summary>The page name. Matches the window's Title, per the wave-4 title rule.</summary>
    public static readonly DependencyProperty HeadingProperty =
        DependencyProperty.Register(nameof(Heading), typeof(string), typeof(PageHeader),
            new PropertyMetadata(""));

    public string Heading
    {
        get => (string)GetValue(HeadingProperty);
        set => SetValue(HeadingProperty, value);
    }

    /// <summary>
    /// Optional one-liner under the heading. Collapses when null or empty, so "no descriptor" is
    /// the default rather than something each window has to remember to leave out.
    /// </summary>
    public static readonly DependencyProperty DescriptorProperty =
        DependencyProperty.Register(nameof(Descriptor), typeof(string), typeof(PageHeader),
            new PropertyMetadata(null));

    public string? Descriptor
    {
        get => (string?)GetValue(DescriptorProperty);
        set => SetValue(DescriptorProperty, value);
    }

    /// <summary>
    /// Prefix the heading with the two-tone RoRoRo wordmark. False by default: F-004 reserves it
    /// for MainWindow and About, the two surfaces whose subject is the product itself. Everywhere
    /// else the title bar already names the destination, and a wordmark here would print the
    /// product name twice in one window.
    /// </summary>
    public static readonly DependencyProperty ShowWordmarkProperty =
        DependencyProperty.Register(nameof(ShowWordmark), typeof(bool), typeof(PageHeader),
            new PropertyMetadata(false));

    public bool ShowWordmark
    {
        get => (bool)GetValue(ShowWordmarkProperty);
        set => SetValue(ShowWordmarkProperty, value);
    }
}
