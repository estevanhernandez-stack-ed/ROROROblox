namespace ROROROblox.Core.Diagnostics;

/// <summary>Machine-wide physical memory. Total drives derived settings defaults; available drives the projection.</summary>
public interface ISystemMemoryProbe
{
    bool TryRead(out long totalPhysicalBytes, out long availablePhysicalBytes);
}
