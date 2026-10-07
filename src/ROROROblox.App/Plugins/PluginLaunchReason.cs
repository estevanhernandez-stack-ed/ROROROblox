namespace ROROROblox.App.Plugins;

/// <summary>
/// Why the host is launching a plugin process (v1.33 item 8). Travels to the child as the
/// <c>RORORO_LAUNCH_REASON</c> environment variable so a plugin can tell a boot it chose from a boot
/// that happened to it — Ur Score wants to skip its first-run setup on an update relaunch, and to
/// stay quiet on an autostart the user did not sit down for.
/// <para>
/// AN ENVIRONMENT VARIABLE, NOT ARGV, and that is the whole compatibility story. Every shipped
/// plugin is launched with no arguments and parses none; adding one risks a plugin that treats an
/// unexpected argv as an error. An env var is invisible to a plugin that does not read it, so
/// <c>contractVersion</c> does not move, no RPC is added, no capability is added, and consent is
/// untouched. A plugin built before this existed behaves exactly as it did.
/// </para>
/// <para>
/// An enum rather than the raw strings the call sites would otherwise pass: there are five launch
/// paths across two files, and a typo in one of them would be a value the plugin silently never
/// matches. <see cref="PluginLaunchReasonExtensions.ToWireValue"/> is the single place the spelling
/// lives, and <c>PluginLaunchReasonTests</c> asserts every enum member maps to a distinct non-empty
/// value, so a sixth reason cannot be added without a wire name.
/// </para>
/// </summary>
public enum PluginLaunchReason
{
    /// <summary>The startup sweep, for plugins whose consent has autostart enabled. Nobody clicked.</summary>
    Autostart,

    /// <summary>The user pressed Launch on the plugin's row.</summary>
    Manual,

    /// <summary>First run straight after install and consent, so a new plugin works without a RoRoRo restart.</summary>
    Install,

    /// <summary>Relaunch onto a new version, and only if it was running before the update.</summary>
    Update,

    /// <summary>
    /// The user pressed Restart on the crash banner. Distinct from <see cref="Manual"/> on purpose:
    /// the plugin is coming back from a death rather than being started fresh, which is exactly the
    /// case where it may want to report rather than re-run setup. The checklist named four reasons;
    /// the sweep found five call sites, and folding this one into Manual would have thrown away the
    /// only one a plugin has a real reason to treat differently.
    /// </summary>
    Restart,
}

/// <summary>Wire spelling for <see cref="PluginLaunchReason"/>.</summary>
public static class PluginLaunchReasonExtensions
{
    /// <summary>The environment variable the host sets on every plugin process it starts.</summary>
    public const string EnvironmentVariableName = "RORORO_LAUNCH_REASON";

    /// <summary>
    /// The value a plugin reads. Lowercase and stable — these strings are a published contract the
    /// moment a plugin matches on them, so rename one and you break that plugin silently. Add
    /// members; do not re-spell them.
    /// </summary>
    public static string ToWireValue(this PluginLaunchReason reason) => reason switch
    {
        PluginLaunchReason.Autostart => "autostart",
        PluginLaunchReason.Manual => "manual",
        PluginLaunchReason.Install => "install",
        PluginLaunchReason.Update => "update",
        PluginLaunchReason.Restart => "restart",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason,
            "A PluginLaunchReason with no wire value. Add it here — a plugin matching on the "
            + "variable would see nothing it recognises."),
    };
}
