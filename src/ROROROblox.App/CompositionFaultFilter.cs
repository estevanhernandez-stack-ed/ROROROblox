namespace ROROROblox.App;

/// <summary>
/// Recognises the one dispatcher exception that is a warning, not a crash: a desktop-composition
/// flip (#246). Pure so a test can hold the line; <c>App.WireGlobalExceptionHandlers</c> acts on it.
/// </summary>
internal static class CompositionFaultFilter
{
    /// <summary><c>DWM_E_COMPOSITIONDISABLED</c>, "Desktop composition is disabled".</summary>
    internal const int DwmCompositionDisabled = unchecked((int)0x80263001);

    private const string WindowChromeWorkerFrame = "System.Windows.Shell.WindowChromeWorker";

    /// <summary>
    /// True only for HResult 0x80263001 raised from inside WPF's <c>WindowChromeWorker</c> (its
    /// WM_DWMCOMPOSITIONCHANGED handler calling <c>DwmExtendFrameIntoClientArea</c> while DWM is
    /// mid-flip). Both halves are required: the same HResult from our own code, or any other
    /// exception from the chrome worker, is a real fault and keeps the crash path.
    /// </summary>
    internal static bool IsTransientCompositionFault(int hresult, string? stackTrace) =>
        hresult == DwmCompositionDisabled
        && stackTrace is not null
        && stackTrace.Contains(WindowChromeWorkerFrame, StringComparison.Ordinal);
}
