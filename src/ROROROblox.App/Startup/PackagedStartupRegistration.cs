using Windows.ApplicationModel;

namespace ROROROblox.App.Startup;

/// <summary>
/// Run-on-login for MSIX-packaged installs (Store + sideload), backing the same Settings toggle
/// that <see cref="StartupRegistration"/> backs for unpackaged installs. A packaged process's
/// HKCU Run write lands in the package's virtual registry hive where winlogon never reads it —
/// and reads its own virtual value back, so the Run-key implementation's toggle LIES on packaged
/// installs (verified live 2026-08-30, spec 2026-08-30-packaged-activation-design.md). The
/// packaged path goes through the manifest's <c>desktop:StartupTask</c> instead: Windows owns
/// the persisted state, surfaces it in Settings &gt; Apps &gt; Startup and Task Manager, and the
/// user can override us there — which is why <see cref="Enable"/> can fail in ways the registry
/// path never could.
/// <para>
/// Sync-over-async is deliberate: <see cref="IStartupRegistration"/> is a sync seam called from
/// a toggle click handler, and the WinRT operations complete on the thread pool rather than our
/// dispatcher, so blocking the UI thread on them cannot deadlock. No consent UI is involved for
/// a desktop-bridge app; the calls finish in milliseconds.
/// </para>
/// </summary>
internal sealed class PackagedStartupRegistration : IStartupRegistration
{
    /// <summary>
    /// Must match <c>TaskId</c> in Package.appxmanifest; PackagedActivationManifestTests pins
    /// the two together.
    /// </summary>
    internal const string TaskId = "RoRoRo";

    private readonly Func<StartupTaskState> _getState;
    private readonly Func<StartupTaskState> _requestEnable;
    private readonly Action _disable;

    public PackagedStartupRegistration()
        : this(
            static () => Get().State,
            static () => Get().RequestEnableAsync().AsTask().GetAwaiter().GetResult(),
            static () => Get().Disable())
    {
    }

    /// <summary>
    /// Seam for tests: the WinRT calls themselves require package identity the test host does
    /// not have, so only the state-to-behavior mapping is unit-testable. The live packaged smoke
    /// in the spec's §4 covers the real calls.
    /// </summary>
    internal PackagedStartupRegistration(
        Func<StartupTaskState> getState,
        Func<StartupTaskState> requestEnable,
        Action disable)
    {
        _getState = getState;
        _requestEnable = requestEnable;
        _disable = disable;
    }

    public bool IsEnabled() =>
        _getState() is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

    public void Enable()
    {
        var state = _requestEnable();
        if (state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy)
        {
            return;
        }

        // The Settings page's existing catch shows this message in a warning dialog and reverts
        // the toggle. DisabledByUser is the state RequestEnableAsync cannot flip: once the user
        // turns the task off in Windows' own UI, only that UI can turn it back on.
        throw new InvalidOperationException(state switch
        {
            StartupTaskState.DisabledByUser =>
                "Windows has startup turned off for RoRoRo. Turn it on under Settings > Apps > Startup, then flip this switch again.",
            StartupTaskState.DisabledByPolicy =>
                "A device policy blocks RoRoRo from starting at sign-in on this PC.",
            _ => $"Windows left the startup task in state '{state}'.",
        });
    }

    public void Disable() => _disable();

    private static StartupTask Get() =>
        StartupTask.GetAsync(TaskId).AsTask().GetAwaiter().GetResult();
}
