namespace ROROROblox.Core.Diagnostics;
public interface ISystemInputClock
{
    /// <summary>GetLastInputInfo dwTime: the GetTickCount-domain tick of the last system-wide input.</summary>
    uint LastInputTick { get; }

    /// <summary>The current tick in the same domain as <see cref="LastInputTick"/>.</summary>
    uint NowTick { get; }
}
