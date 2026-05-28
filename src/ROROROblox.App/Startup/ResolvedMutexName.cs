namespace ROROROblox.App.Startup;

/// <summary>
/// One-field mutable DI seam holding the startup-resolved singleton mutex name. The resolution step
/// in <c>App.OnStartup</c> writes <see cref="Value"/> before the <c>IMutexHolder</c> singleton is
/// first materialized; the holder factory reads it. Defaults to
/// <see cref="ROROROblox.Core.MutexHolder.DefaultMutexName"/> so a never-run resolution still yields
/// a safe name. Spec item #1 (config-driven mutex name).
/// </summary>
public sealed class ResolvedMutexName
{
    public string Value { get; set; } = ROROROblox.Core.MutexHolder.DefaultMutexName;
}
