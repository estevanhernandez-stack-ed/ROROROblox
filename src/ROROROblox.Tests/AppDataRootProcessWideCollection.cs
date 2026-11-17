namespace ROROROblox.Tests;

/// <summary>
/// Tests that set the PROCESS-WIDE <c>AppDataRoot</c> override (the one <c>Program.Main</c> sets) share
/// this collection so they never run alongside any other test: every test that resolves a RoRoRo
/// path would see that folder. Each one resets it on the way out. Everything else uses the
/// <c>AsyncLocal</c> seam (<c>AppDataRoot.OverrideForTest</c>), which cannot leak.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AppDataRootProcessWideCollection
{
    public const string Name = "AppDataRootProcessWide";
}
