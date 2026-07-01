using System;

namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Narrow reverse lookup: which managed account owns a given OS process id.
/// Backed by <see cref="RobloxProcessTracker"/>'s claimed-pid map. Read-only.
/// </summary>
public interface IForegroundAccountResolver
{
    bool TryResolveAccountByPid(int pid, out Guid accountId);
}
