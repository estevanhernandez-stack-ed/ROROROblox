using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ROROROblox.Core.Discord;

namespace ROROROblox.App.Tray;

/// <summary>
/// The notification RoRoRo draws for itself, shown through
/// <c>TaskbarIcon.ShowCustomBalloon(UIElement, PopupAnimation, int?)</c> (v1.33 item 4). See
/// AlertBalloon.xaml for why it replaced the shell balloon and what that costs.
/// <para>
/// <b>It carries its own account.</b> <see cref="AccountId"/> is the single account the
/// notification is about, supplied by <c>AlertDispatcher</c> when a coalesced group resolves to
/// exactly one, and a click replays it on <see cref="FocusRequested"/> — which <c>TrayService</c>
/// forwards to <c>ITrayService.RequestFocusAccount</c>, the path a memory-warning balloon has used
/// since Task 8.
/// </para>
/// <para>
/// WHY ON THE CONTROL RATHER THAN IN THE TRAY, which is the whole shape of this item. Through
/// v1.32 the account lived in a <c>TrayService</c> field stamped by <c>ShowMemoryWarning</c>, and
/// the shell's <c>TrayBalloonTipClicked</c> — an event with no payload of its own — replayed
/// whatever that field last held. It needed clearing on every other balloon so a click could not
/// fire a stale account, which is exactly what made item 3's collapse break it invisibly: every
/// balloon began arriving through <c>ShowToast</c>, which cleared it. State that has to be cleared
/// by unrelated callers is state in the wrong place. An id that rides on the balloon cannot go
/// stale, cannot be cleared by someone else's notification, and — the actual win — is not a memory
/// warning's privilege: a drop, a recycle, an idle crossing and a metric breach are all clickable
/// now, for free, because they all route through one producer.
/// </para>
/// <para>
/// <b>Null is a real answer.</b> A group of three accounts has no single row to jump to, so it
/// arrives with no id and stays inert. Surfacing the window and highlighting an arbitrary one of
/// the three would be wrong AND look deliberate.
/// </para>
/// <para>
/// NO SOUND HERE. Item 5 owns that, and it plays from the show path so one event is one sound
/// regardless of fan-out. The reason there is a choice to make at all is that this control draws no
/// OS chrome and therefore triggers no OS sound.
/// </para>
/// </summary>
internal partial class AlertBalloon : UserControl
{
    /// <summary>The desktop envelope: 63 title, 255 body. See <see cref="PayloadLimits.Toast"/>.</summary>
    private static readonly PayloadLimits Envelope = PayloadLimits.For(AlertDestination.Local);

    public AlertBalloon() => InitializeComponent();

    /// <summary>
    /// The alert's headline — <c>WebhookPayload.Title</c> as the dispatcher built it for this
    /// destination. Coerced to the envelope: see <see cref="ClampToEnvelope"/>.
    /// </summary>
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(AlertBalloon),
            new PropertyMetadata("", null, ClampTitle));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>The alert's lines — <c>WebhookPayload.Body</c>. Coerced like <see cref="Title"/>.</summary>
    public static readonly DependencyProperty BodyProperty =
        DependencyProperty.Register(nameof(Body), typeof(string), typeof(AlertBalloon),
            new PropertyMetadata("", null, ClampBody));

    public string Body
    {
        get => (string)GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    /// <summary>
    /// THE ENVELOPE AS A BACKSTOP. Until this control existed, 63/255 was a budget the dispatcher
    /// kept and the SHELL enforced — a caller handing its own longer strings got Windows' silent
    /// mid-line cut. Nothing cuts now except this, so an unclamped 4,000-character body would be a
    /// notification sized by its content rather than by its envelope.
    /// <para>
    /// A COERCION rather than a check in the setter, so it holds however the value arrives — direct
    /// assignment, a binding, a style setter. The cut and its visible <c>…</c> belong to
    /// <see cref="PayloadLimits"/>, which already owns these numbers and the surrogate-pair care.
    /// It should never fire in production; <c>WebhookPayload.ForAlert</c> builds every routed
    /// payload inside the envelope. The marker is there so that if it ever does fire, it is
    /// visible instead of silent.
    /// </para>
    /// </summary>
    private static object ClampTitle(DependencyObject d, object baseValue) =>
        Envelope.ClampTitle((string)baseValue ?? "");

    private static object ClampBody(DependencyObject d, object baseValue) =>
        Envelope.ClampBody((string)baseValue ?? "");

    /// <summary>
    /// The one account this notification is about, or <c>null</c> when it covers several (or none —
    /// the uptime mark is about the machine). Set before the balloon is shown;
    /// <c>AlertDispatcher.SingleAccount</c> decides it.
    /// <para>
    /// A plain property rather than a <see cref="DependencyProperty"/>: nothing binds to it, and
    /// the cursor side effect wants a setter it can see. The cursor IS the only affordance this
    /// control has, so it has to track the one thing that decides whether a click does anything.
    /// </para>
    /// </summary>
    public Guid? AccountId
    {
        get => _accountId;
        set
        {
            _accountId = value;
            // Surface is the named root Border in the markup. Null-guarded because a
            // DependencyProperty-free setter can be assigned in an object initialiser, which runs
            // after InitializeComponent — but a subclass or a designer could reach it earlier.
            if (Surface is not null)
            {
                Surface.Cursor = value is null ? null : Cursors.Hand;
            }
        }
    }

    private Guid? _accountId;

    /// <summary>
    /// Raised when the user clicks a balloon that names one account. <c>TrayService</c> subscribes
    /// per balloon and forwards to <c>ITrayService.RequestFocusAccount</c>, whose handler in
    /// <c>App.xaml.cs</c> surfaces the main window and highlights the row.
    /// </summary>
    public event EventHandler<Guid>? FocusRequested;

    /// <summary>
    /// <c>MouseLeftButtonUp</c>, not <c>MouseLeftButtonDown</c>: a notification that fires on press
    /// takes the window over while the pointer is still moving, and up-after-down is what every
    /// other clickable surface in the app means by a click.
    /// </summary>
    private void OnSurfaceClicked(object sender, MouseButtonEventArgs e)
    {
        if (_accountId is not { } accountId) return;

        // Handled, so the click does not keep bubbling toward the popup's own chrome.
        e.Handled = true;
        FocusRequested?.Invoke(this, accountId);
    }
}
