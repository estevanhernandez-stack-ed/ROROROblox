namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Private committed bytes for one process. Returns false rather than throwing or guessing —
/// a pid we cannot read is UNKNOWN, and callers must exclude it from aggregates rather than
/// substitute zero. Zero understates growth and delays the warning.
/// </summary>
public interface IProcessMemoryProbe
{
    bool TryReadPrivateBytes(int pid, out long privateBytes);
}
