namespace ROROROblox.Core;

/// <summary>
/// Detects whether Bloxstrap is the registered <c>roblox-player</c> protocol handler.
/// When true, our FFlag write is overridden by Bloxstrap's launch-time rewrite — the user
/// sees a one-time dismissible banner. Spec §5.2.
/// </summary>
public interface IBloxstrapDetector
{
    bool IsBloxstrapHandler();
}
