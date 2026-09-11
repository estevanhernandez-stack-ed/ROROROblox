namespace ROROROblox.MetricSmoke;

/// <summary>
/// MetricSmoke's entry point (design
/// docs/superpowers/specs/2026-09-11-metric-smoke-harness-design.md). Task 1 ships the profile guard;
/// the log reader, pipe driver, webhook catcher and scenario runner land in the later tasks of the
/// plan, and this grows into the runner there.
/// <para>
/// The one command already worth having is putting the profile back after an interrupted run, because
/// that is the failure a user feels: a <c>discord.dat</c> left pointing at a dead localhost webhook
/// means alerts stop arriving and two URLs have to be re-pasted out of Discord.
/// </para>
/// <para>
/// Explicitly named and INTERNAL, not top-level statements: top-level statements compile to a
/// <c>Program</c> class in the global namespace, and ROROROblox.Tests references both this project and
/// ROROROblox.App — whose own <c>Program</c> the tests call by its unqualified name. A global
/// <c>Program</c> here shadowed that and broke ProgramPortableDetectionTests. Internal keeps this one
/// out of the test project's sight entirely.
/// </para>
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args is ["--recover"])
        {
            var backupRoot = ProfileGuard.DefaultBackupRoot();
            var attempted = await ProfileGuard.RecoverOrphanedAsync(backupRoot, Console.WriteLine).ConfigureAwait(false);

            // The marker outliving the attempt is the whole answer: it is removed only by a restore
            // that put everything back. Recovery reports ATTEMPTED, not succeeded, and a command
            // whose job is clearing a broken profile must not exit zero having left it broken —
            // including when it refused because a run is still live.
            if (ProfileGuard.HasOrphanedMarker(backupRoot))
            {
                Console.WriteLine($"NOT fully restored: {ProfileGuard.MarkerFileName} is still in {backupRoot}. "
                    + "The lines above say which files could not be put back, or why this refused to try.");
                return 2;
            }

            Console.WriteLine(attempted
                ? "The profile is back: the marker and its backups are gone."
                : $"Nothing to recover: no {ProfileGuard.MarkerFileName} in {backupRoot}.");
            return 0;
        }

        Console.WriteLine("MetricSmoke: the scenario runner is not wired yet (plan tasks 2-5).");
        Console.WriteLine("  --recover    put the profile back after an interrupted run");
        Console.WriteLine("Exit codes: 0 nothing to do or fully restored, 1 bad usage, 2 the profile is still not right.");
        return 1;
    }
}
