using ROROROblox.App;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// #246: the 2026-10-24 field crash (v1.33.0, twice) was a DWM composition flip surfacing through
/// WPF's WindowChromeWorker. Only that exact shape is downgraded to a warning.
/// </summary>
public class CompositionFaultFilterTests
{
    private const int CompositionDisabled = unchecked((int)0x80263001);
    private const int EFail = unchecked((int)0x80004005);

    // Verbatim from the field log.
    private const string FieldStack =
        "   at Standard.NativeMethods.DwmExtendFrameIntoClientArea(IntPtr hwnd, MARGINS& pMarInset)\r\n" +
        "   at System.Windows.Shell.WindowChromeWorker._ExtendGlassFrame()\r\n" +
        "   at System.Windows.Shell.WindowChromeWorker._UpdateFrameState(Boolean force)\r\n" +
        "   at System.Windows.Shell.WindowChromeWorker._HandleDwmCompositionChanged(WM uMsg, IntPtr wParam, IntPtr lParam, Boolean& handled)\r\n" +
        "   at System.Windows.Shell.WindowChromeWorker._WndProc(IntPtr hwnd, Int32 msg, IntPtr wParam, IntPtr lParam, Boolean& handled)\r\n" +
        "   at System.Windows.Interop.HwndSource.PublicHooksFilterMessage(IntPtr hwnd, Int32 msg, IntPtr wParam, IntPtr lParam, Boolean& handled)\r\n" +
        "   at MS.Win32.HwndWrapper.WndProc(IntPtr hwnd, Int32 msg, IntPtr wParam, IntPtr lParam, Boolean& handled)\r\n" +
        "   at System.Windows.Threading.ExceptionWrapper.InternalRealCall(Delegate callback, Object args, Int32 numArgs)\r\n" +
        "   at System.Windows.Threading.ExceptionWrapper.TryCatchWhen(Object source, Delegate callback, Object args, Int32 numArgs, Delegate catchHandler)";

    [Fact]
    public void FieldStack_IsTransient() =>
        Assert.True(CompositionFaultFilter.IsTransientCompositionFault(CompositionDisabled, FieldStack));

    [Fact]
    public void SameHResult_WithoutChromeWorkerFrame_IsNotTransient() =>
        Assert.False(CompositionFaultFilter.IsTransientCompositionFault(
            CompositionDisabled,
            "   at ROROROblox.App.MainWindow.Foo()\r\n   at MS.Win32.HwndWrapper.WndProc(IntPtr hwnd)"));

    [Fact]
    public void DifferentHResult_WithChromeWorkerFrame_IsNotTransient() =>
        Assert.False(CompositionFaultFilter.IsTransientCompositionFault(EFail, FieldStack));

    [Fact]
    public void NullStack_IsNotTransient() =>
        Assert.False(CompositionFaultFilter.IsTransientCompositionFault(CompositionDisabled, null));
}
