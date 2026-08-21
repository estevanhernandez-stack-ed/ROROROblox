using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// F-102, and the question that row said was worth asking twice.
/// <para>
/// Streamer mode is a PRIVACY control, and it reported engaged while disengaged: the Settings
/// checkbox was wired to <c>Click</c>, and <c>TogglePattern.Toggle()</c> — the only pattern a
/// CheckBox exposes, and the one every assistive technology and automation path uses — routes
/// through WPF's <c>ToggleButtonAutomationPeer</c>, which raises Checked/Unchecked and never Click.
/// Observed end to end in the Store capture run: toggled via UIA, read back <c>On</c>, and the
/// captured PNG showed real account names.
/// </para>
/// <para>
/// Run on <see cref="WindowRenderHost"/>'s thread, NOT on a fresh <see cref="Sta"/> one. Building a
/// real <c>MenuItem</c> caches WPF's theme resources — unfrozen <c>SolidColorBrush</c>es among them
/// — against the thread that built it, and a fresh thread per call meant the render gates then hit
/// "Cannot access Freezable across threads" on <c>MainWindow</c>'s own menus. Three render tests
/// went red the first time these landed. Sharing the one host thread is what the render gates
/// already do with each other, and it costs nothing here.
/// </para>
/// <para>
/// These pin the WPF behaviour itself rather than our wiring, because "does Toggle() raise Click"
/// is a framework fact that the fix depends on and that nobody should have to re-derive from memory
/// — which is exactly how the original defect survived a comment three lines from the answer.
/// </para>
/// </summary>
public class TogglePatternReachesTheHandlerTests
{
    [Fact]
    public void ACheckBoxToggledThroughAutomationNeverRaisesClick()
    {
        // The defect, stated as a framework fact. If this ever starts failing, WPF changed and the
        // binding-based fix is no longer load-bearing — but until then, any Click handler on a
        // CheckBox is invisible to automation.
        var result = WindowRenderHost.Run<(int Clicks, bool State)>(() =>
        {
            var box = new CheckBox { IsChecked = false };
            var seen = 0;
            box.Click += (_, _) => seen++;

            var peer = new CheckBoxAutomationPeer(box);
            ((IToggleProvider)peer).Toggle();

            return (seen, box.IsChecked == true);
        }, "checkbox toggled through its automation peer");

        Assert.True(result.State, "Toggle() should have flipped IsChecked");
        Assert.Equal(0, result.Clicks);
    }

    [Fact]
    public void AMenuItemToggledThroughAutomationDoesNotRaiseClickEither()
    {
        // F-102 said the same question was worth asking of the tray's checkable MenuItem. It was,
        // and the answer is the one nobody wanted: MenuItemAutomationPeer.Toggle() raises no Click
        // either. This test was WRITTEN expecting the opposite — the assumption was that MenuItem
        // routes through its click path — and the measurement said 0. The tray had the same defect
        // as the Settings checkbox and is now bound the same way.
        var clicks = WindowRenderHost.Run<int>(() =>
        {
            var item = new MenuItem { Header = "Streamer mode", IsCheckable = true, IsChecked = false };
            var seen = 0;
            item.Click += (_, _) => seen++;

            var peer = new MenuItemAutomationPeer(item);
            ((IToggleProvider)peer).Toggle();

            return seen;
        }, "menu item toggled through its automation peer");

        Assert.Equal(0, clicks);
    }

    [Fact]
    public void ATwoWayBoundCheckBoxReachesItsSourceThroughAutomation()
    {
        // The shape the fix uses. A binding is immune to the Click-versus-programmatic split
        // because it is driven by the dependency property, which every path sets.
        var landed = WindowRenderHost.Run<bool>(() =>
        {
            var source = new BindableFlag();
            var box = new CheckBox();
            box.SetBinding(ToggleButton.IsCheckedProperty,
                new System.Windows.Data.Binding(nameof(BindableFlag.On))
                {
                    Source = source,
                    Mode = System.Windows.Data.BindingMode.TwoWay,
                });

            ((IToggleProvider)new CheckBoxAutomationPeer(box)).Toggle();
            return source.On;
        }, "two-way bound checkbox toggled through its automation peer");

        Assert.True(landed, "a two-way binding must carry an automation toggle through to its source");
    }

    private sealed class BindableFlag : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _on;
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public bool On
        {
            get => _on;
            set
            {
                if (_on == value) return;
                _on = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(On)));
            }
        }
    }
}
