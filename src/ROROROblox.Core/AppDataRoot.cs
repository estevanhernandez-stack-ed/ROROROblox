using System.Threading;

namespace ROROROblox.Core;

/// <summary>
/// The one place that decides where RoRoRo keeps its own data: <c>%LOCALAPPDATA%\ROROROblox</c>, or,
/// on a dev build started with <c>RORORO_DATA_DIR</c>, the folder that variable names.
/// <para>
/// Every path under that folder (accounts, settings, logs, caches, plugins, sentinels) is built
/// through <see cref="Folder"/> or <see cref="Combine"/>, and a source fence
/// (<c>AppDataRootFenceTests</c>) fails if a second piece of code resolves
/// <c>SpecialFolder.LocalApplicationData</c> for it. The Roblox client's own folders
/// (<c>%LOCALAPPDATA%\Roblox</c>, <c>%LOCALAPPDATA%\Packages</c>) are not RoRoRo's and do not go
/// through here; the fence allow-lists those by file and count.
/// </para>
/// <para>
/// <see cref="Folder"/> is resolved on every read and never cached in a <c>static readonly</c>
/// field, so a value derived from it at type initialisation cannot freeze a path before whatever
/// configures this class has run. Callers should follow the same rule: a property or a method,
/// not a static field, for anything derived from the root.
/// </para>
/// <para>
/// <b>The override.</b> <c>Program.Main</c> calls <see cref="Configure"/> once, straight after
/// Velopack's hooks and before anything reads <c>settings.json</c>. The decision is pure
/// (<see cref="Decide"/>): shipped builds (MSIX-packaged or Velopack-laid-out) ignore the variable,
/// and so does any value that is not a fully qualified absolute path. A second configuration with a
/// different outcome throws, so a late caller cannot move the folder under code that already
/// resolved paths. The process-wide value is therefore set by <c>Program.Main</c> and by nothing
/// else; tests use <see cref="OverrideForTest"/>, an <see cref="AsyncLocal{T}"/> scope.
/// </para>
/// </summary>
public static class AppDataRoot
{
    /// <summary>The folder name under <c>%LOCALAPPDATA%</c>. An identifier, not the brand.</summary>
    private const string FolderName = "ROROROblox";

    /// <summary>The environment variable a dev build reads to relocate RoRoRo's data.</summary>
    public const string EnvironmentVariableName = "RORORO_DATA_DIR";

    private static readonly object Gate = new();

    /// <summary>The process-wide override, set at most once by <see cref="Configure"/>. Null = default folder.</summary>
    private static volatile string? _processOverride;

    /// <summary>True once <see cref="Configure"/> has settled the folder (to an override or to the default).</summary>
    private static bool _configured;

    private static DataRootDecision _decision = DataRootDecision.Unset;

    /// <summary>A test's scoped override. Flows with the execution context, so a task started inside
    /// the scope sees it and a sibling task never does.</summary>
    private static readonly AsyncLocal<string?> Scoped = new();

    /// <summary>
    /// RoRoRo's data folder: a test's scoped override if one is in force, else the process-wide
    /// override, else <c>%LOCALAPPDATA%\ROROROblox</c> resolved through <c>Environment.GetFolderPath</c>
    /// on every read.
    /// </summary>
    public static string Folder => Scoped.Value ?? _processOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        FolderName);

    /// <summary>
    /// True when RoRoRo's data is somewhere other than the default (a test's scoped override or the
    /// process-wide one). Machine-wide registrations hang off this: see <see cref="MachineWideRegistrationsAllowed"/>.
    /// </summary>
    public static bool IsOverridden => Scoped.Value is not null || _processOverride is not null;

    /// <summary>
    /// The one predicate for "may this run write or remove things outside the data folder that
    /// belong to the machine": the roblox-rororo and Discord URI schemes in HKCU\Software\Classes
    /// and the run-on-login value. False under the override, so a run on a test folder cannot
    /// repoint or clear the owner's real registrations. Reads <see cref="IsOverridden"/> on every
    /// call, like <see cref="Folder"/>.
    /// </summary>
    public static bool MachineWideRegistrationsAllowed => !IsOverridden;

    /// <summary>A path under <see cref="Folder"/>. No parts returns the folder itself.</summary>
    public static string Combine(params string[] parts) => Path.Combine([Folder, .. parts]);

    /// <summary>
    /// What <see cref="Configure"/> decided, for the startup log lines that cannot be written at
    /// configuration time (logging is not up yet). <see cref="DataRootDecisionReason.Unset"/> before
    /// <see cref="Configure"/> has run.
    /// </summary>
    public static DataRootDecision Decision
    {
        get { lock (Gate) { return _decision; } }
    }

    /// <summary>
    /// The pure eligibility rule. No I/O, no environment reads: the caller gathers the three inputs.
    /// <list type="bullet">
    /// <item>null, empty or whitespace: <see cref="DataRootDecisionReason.Unset"/>.</item>
    /// <item>packaged, or a Velopack-installed layout, whatever the value: <see cref="DataRootDecisionReason.NotADevBuild"/>.</item>
    /// <item>otherwise the trimmed value must be a fully qualified absolute path
    /// (<see cref="Path.IsPathFullyQualified(string)"/>), else <see cref="DataRootDecisionReason.NotAnAbsolutePath"/>;
    /// an accepted path is normalised with <see cref="Path.GetFullPath(string)"/> and loses any trailing separator.</item>
    /// </list>
    /// </summary>
    public static DataRootDecision Decide(bool isPackaged, bool isVelopackInstalled, string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return DataRootDecision.Unset;
        }

        if (isPackaged || isVelopackInstalled)
        {
            return new DataRootDecision(DataRootDecisionReason.NotADevBuild, null);
        }

        var trimmed = rawValue.Trim();
        try
        {
            if (!Path.IsPathFullyQualified(trimmed))
            {
                return new DataRootDecision(DataRootDecisionReason.NotAnAbsolutePath, null);
            }

            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed));
            return new DataRootDecision(DataRootDecisionReason.Used, full);
        }
        catch (ArgumentException)
        {
            // A NUL or similar: not a usable path, so not an absolute one.
            return new DataRootDecision(DataRootDecisionReason.NotAnAbsolutePath, null);
        }
        catch (NotSupportedException)
        {
            return new DataRootDecision(DataRootDecisionReason.NotAnAbsolutePath, null);
        }
        catch (PathTooLongException)
        {
            return new DataRootDecision(DataRootDecisionReason.NotAnAbsolutePath, null);
        }
    }

    /// <summary>
    /// The startup call: decide, and when the decision is <see cref="DataRootDecisionReason.Used"/>
    /// create the folder if it is missing and set the process-wide override. Called once, from
    /// <c>Program.Main</c>. Does not log: logging is not configured yet, so the outcome is kept in
    /// <see cref="Decision"/> for the caller that writes the log lines later.
    /// <para>
    /// A second call is allowed only when it would leave the folder exactly where it is (same override,
    /// or the default again); anything else throws <see cref="InvalidOperationException"/> before it
    /// creates or changes anything.
    /// </para>
    /// </summary>
    public static DataRootDecision Configure(bool isPackaged, bool isVelopackInstalled, string? rawValue)
    {
        var decision = Decide(isPackaged, isVelopackInstalled, rawValue);

        lock (Gate)
        {
            if (_configured && !SameFolder(_processOverride, decision.Folder))
            {
                throw new InvalidOperationException(
                    $"The data folder is already settled ({_processOverride ?? "the default"}) and cannot be moved to " +
                    $"{decision.Folder ?? "the default"}. AppDataRoot.Configure is a once-at-startup call.");
            }

            if (decision.Reason == DataRootDecisionReason.Used)
            {
                Directory.CreateDirectory(decision.Folder!);
                _processOverride = decision.Folder;
            }

            _decision = decision;
            _configured = true;
            return decision;
        }
    }

    /// <summary>
    /// Runs code "under an override" without touching the process-wide value: an
    /// <see cref="AsyncLocal{T}"/> scope that takes precedence over it, flows into tasks started
    /// inside it, and is restored on dispose. Two scopes on two concurrent tasks never see each other.
    /// </summary>
    internal static IDisposable OverrideForTest(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var previous = Scoped.Value;
        Scoped.Value = folder;
        return new ScopeRestore(previous);
    }

    /// <summary>
    /// Clears the process-wide override, the configured flag and the kept decision. Only for the
    /// single non-parallel test collection that exercises <see cref="Configure"/>; production never calls it.
    /// </summary>
    internal static void ResetProcessWideForTest()
    {
        lock (Gate)
        {
            _processOverride = null;
            _configured = false;
            _decision = DataRootDecision.Unset;
        }
    }

    private static bool SameFolder(string? a, string? b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private sealed class ScopeRestore(string? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Scoped.Value = previous;
            }
        }
    }
}
