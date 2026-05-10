using System.Windows;
using System.Windows.Media;

namespace ROROROblox.App.Plugins;

/// <summary>
/// Manifest consent modal — shown right after a plugin's bytes land on disk and right before
/// the consent record gets written. Two namespaces of capability live side by side:
/// <list type="bullet">
///   <item><c>host.*</c> — RoRoRo enforces these via the gRPC interceptor (item 12). Pre-checked.</item>
///   <item><c>system.*</c> — runs locally on the user's machine; RoRoRo can disclose but not sandbox.
///   Default unchecked so the user must affirmatively grant. Pattern matches Windows app-permission UX.</item>
/// </list>
/// Result: <see cref="GrantedCapabilities"/> is the user's selection; the caller passes that
/// to <see cref="ConsentStore.GrantAsync"/>. Cancel returns null from
/// <see cref="ShowAndAwaitDecisionAsync"/> so the caller can roll back the install dir.
/// </summary>
internal partial class ConsentSheet : Window
{
    private readonly List<CapabilityRow> _rows;

    public ConsentSheet(PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        InitializeComponent();

        HeaderTitle.Text = manifest.Name;
        HeaderVersion.Text = $"v{manifest.Version}";
        HeaderPublisher.Text = manifest.Publisher;
        HeaderDescription.Text = manifest.Description;

        _rows = manifest.Capabilities.Select(c => new CapabilityRow(c)).ToList();
        CapabilityList.ItemsSource = _rows;
    }

    public IReadOnlyList<string> GrantedCapabilities { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// Show modal-against-owner and return granted capabilities, or null if cancelled.
    /// Wraps DialogResult so the install flow stays async-friendly even though WPF
    /// modals are blocking.
    /// </summary>
    public static Task<IReadOnlyList<string>?> ShowAndAwaitDecisionAsync(Window? owner, PluginManifest manifest)
    {
        var sheet = new ConsentSheet(manifest);
        if (owner is not null && owner.IsLoaded)
        {
            sheet.Owner = owner;
        }
        var ok = sheet.ShowDialog() == true;
        return Task.FromResult<IReadOnlyList<string>?>(ok ? sheet.GrantedCapabilities : null);
    }

    private void OnInstallClick(object sender, RoutedEventArgs e)
    {
        GrantedCapabilities = _rows.Where(r => r.IsGranted).Select(r => r.Id).ToList();
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// Per-capability row backing model. <c>host.*</c> pre-ticked (RoRoRo enforces);
    /// <c>system.*</c> defaults unchecked so the user must affirmatively grant.
    /// </summary>
    internal sealed class CapabilityRow
    {
        public CapabilityRow(string id)
        {
            Id = id;
            Display = PluginCapability.Display(id);
            IsHostEnforced = PluginCapability.IsHostEnforced(id);
            IsGranted = IsHostEnforced; // host.* default-on, system.* default-off
        }

        public string Id { get; }
        public string Display { get; }
        public bool IsHostEnforced { get; }
        public bool IsGranted { get; set; }

        public string NamespaceLabel => IsHostEnforced
            ? "RoRoRo enforces this on every call."
            : "Runs on your machine -- RoRoRo cannot sandbox it.";

        public Brush NamespaceBrush => IsHostEnforced
            ? (Brush)(Application.Current.TryFindResource("CyanBrush") ?? new SolidColorBrush(Color.FromRgb(0x17, 0xD4, 0xFA)))
            : (Brush)(Application.Current.TryFindResource("MagentaBrush") ?? new SolidColorBrush(Color.FromRgb(0xF2, 0x2F, 0x89)));
    }
}
