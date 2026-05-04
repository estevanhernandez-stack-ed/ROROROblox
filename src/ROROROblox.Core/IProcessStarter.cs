namespace ROROROblox.Core;

/// <summary>
/// Tiny abstraction over <see cref="System.Diagnostics.Process.Start(System.Diagnostics.ProcessStartInfo)"/>
/// so <see cref="RobloxLauncher"/> stays unit-testable. Tests stub this to return mock PIDs or
/// throw <see cref="System.ComponentModel.Win32Exception"/> for the missing-protocol-handler path.
/// </summary>
public interface IProcessStarter
{
    /// <summary>
    /// Hands the URI off to the OS via the registered protocol handler.
    /// Returns the spawned process id, or throws <see cref="System.ComponentModel.Win32Exception"/>
    /// when the handler isn't registered (Roblox not installed).
    /// </summary>
    int StartViaShell(string fileNameOrUri);
}
