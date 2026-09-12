using ROROROblox.App.Plugins;
using ROROROblox.Core.Discord;
using ROROROblox.Core.Notify;

namespace ROROROblox.MetricSmoke;

/// <summary>
/// MetricSmoke's entry point (design
/// docs/superpowers/specs/2026-09-11-metric-smoke-harness-design.md). The profile guard, the log
/// reader, the pipe driver and the webhook catcher are tasks 1-4; this is task 5's runner, which sets
/// the profile up, replays <see cref="ScenarioTable.All"/> against a live RoRoRo, and puts the profile
/// back.
/// <para>
/// The one command that is not a run is <c>--recover</c>: putting the profile back after an
/// interrupted one, because that is the failure a user feels — a <c>discord.dat</c> left pointing at a
/// dead localhost webhook means alerts stop arriving and two URLs have to be re-pasted out of Discord.
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
    /// <summary>
    /// How long the runner waits for RoRoRo to answer on the pipe after the profile is set up. Long,
    /// because a human is expected to start the app inside it — the setup deliberately happens while
    /// the app is DOWN (see <see cref="RunAsync"/>), so this window is where the operator launches it.
    /// </summary>
    private static readonly TimeSpan AppStartupWait = TimeSpan.FromMinutes(3);

    /// <summary>Set by the first Ctrl-C. Checked between scenarios.</summary>
    private static volatile bool _stopRequested;

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

        string? subjectOverride = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--subject" && i + 1 < args.Length)
            {
                subjectOverride = args[++i];
                continue;
            }

            Usage();
            return 1;
        }

        if (subjectOverride is not null && !Guid.TryParse(subjectOverride, out _))
        {
            Console.WriteLine($"--subject must be an account id (a Guid); got '{subjectOverride}'.");
            return 1;
        }

        return await RunAsync(subjectOverride).ConfigureAwait(false);
    }

    private static void Usage()
    {
        Console.WriteLine("MetricSmoke — replays the harness-covered rows of docs/superpowers/smoke-metric-alerts.md.");
        Console.WriteLine("  (no arguments)        set the profile up, run every scenario, put the profile back");
        Console.WriteLine("  --subject <guid>      the account id the masked-naming row reports against");
        Console.WriteLine("  --recover             put the profile back after an interrupted run");
        Console.WriteLine("Exit codes: 0 every scenario passed, 1 a scenario failed or setup refused, "
            + "2 the profile is still not right.");
    }

    /// <summary>
    /// The run, in the order the design fixes and every step of which is load-bearing:
    /// <list type="number">
    ///   <item>recover an orphaned marker, before anything else touches the profile;</item>
    ///   <item>refuse if RoRoRo is ALREADY running — see <see cref="RefuseIfAppIsUpAsync"/>, this is
    ///   the clan-channel guard and it is the reason the order has this step at all;</item>
    ///   <item>acquire the guard (backs up what needs backing up, writes the marker);</item>
    ///   <item>start the catcher, so the URLs written next point at something already listening;</item>
    ///   <item>write <c>discord.dat</c> and READ IT BACK — abort without reporting a single metric if
    ///   the read-back disagrees;</item>
    ///   <item>wait for the app to answer on the pipe;</item>
    ///   <item>run the scenarios;</item>
    ///   <item>restore, in a <c>finally</c> that also runs on Ctrl-C.</item>
    /// </list>
    /// </summary>
    private static async Task<int> RunAsync(string? subjectOverride)
    {
        var dataRoot = ProfileGuard.DefaultDataRoot();
        var backupRoot = ProfileGuard.DefaultBackupRoot();

        Console.WriteLine($"MetricSmoke — {ScenarioTable.All.Count} scenarios against the real profile at {dataRoot}");
        Console.WriteLine();

        // 1. An orphan first. AcquireAsync would refuse on one anyway, but it refuses by throwing; a
        //    tool whose first job is putting a broken profile back should try that first and say so.
        if (ProfileGuard.HasOrphanedMarker(backupRoot))
        {
            Console.WriteLine($"An earlier run left {ProfileGuard.MarkerFileName} behind. Restoring from it first.");
            await ProfileGuard.RecoverOrphanedAsync(backupRoot, Console.WriteLine).ConfigureAwait(false);
            if (ProfileGuard.HasOrphanedMarker(backupRoot))
            {
                Console.WriteLine();
                Console.WriteLine($"The marker is still in {backupRoot}, so the profile is not known-good. "
                    + "Refusing to start: fresh backups taken now would overwrite the good ones.");
                return 2;
            }
        }

        // 2. The clan-channel guard. Before the guard, before the marker, before anything is written.
        if (await RefuseIfAppIsUpAsync().ConfigureAwait(false)) return 1;

        HookCtrlC();

        ProfileGuard guard;
        try
        {
            guard = await ProfileGuard.AcquireAsync(dataRoot, backupRoot, Console.WriteLine).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"Refusing to start: {ex.Message}");
            return 1;
        }

        var catcher = WebhookCatcher.Start();
        var failures = 0;
        var skips = 0;

        try
        {
            var phoneConfigured = await PhoneIsConfiguredAsync(dataRoot).ConfigureAwait(false);
            if (!await SetUpProfileAsync(guard, catcher, phoneConfigured).ConfigureAwait(false))
            {
                // SetUpProfileAsync has already said why. Nothing was reported, which is the point.
                return 1;
            }

            using var reporter = new MetricReporter(SmokePluginIds.Reporting);
            if (!await WaitForAppAsync(reporter).ConfigureAwait(false)) return 1;

            var logDirectory = Path.Combine(guard.DataRoot, "logs");
            if (!File.Exists(LogWatch.NewestLogFile(logDirectory)))
            {
                Console.WriteLine($"No log file under {logDirectory}. Every assertion here reads that file, "
                    + "so there is nothing to assert against.");
                return 1;
            }

            var context = new ScenarioContext
            {
                Guard = guard,
                Catcher = catcher,
                Reporter = reporter,
                LogDirectory = logDirectory,
                Log = Console.WriteLine,
                PhoneRouted = !phoneConfigured,
            };
            await ResolveNamedSubjectAsync(context, subjectOverride).ConfigureAwait(false);

            Console.WriteLine();
            Console.WriteLine($"Running {ScenarioTable.All.Count} scenarios. Negative rows sit through "
                + $"{SmokeTimings.AlertWindow.TotalSeconds:0}s each (a tenth of the "
                + $"{AlertRouter.Cooldown.TotalMinutes:0}-minute alert cooldown).");
            Console.WriteLine();

            var faultsSeen = catcher.RequestFaults.Count;
            foreach (var scenario in ScenarioTable.All)
            {
                if (_stopRequested)
                {
                    Console.WriteLine("Stopped by Ctrl-C before the remaining scenarios. The profile is being "
                        + "put back now.");
                    failures++;
                    break;
                }

                Console.WriteLine($"→ {scenario.Name}");
                ScenarioOutcome outcome;
                try
                {
                    outcome = await scenario.Run(context).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // A scenario that throws is a failed scenario, not a failed run: the remaining rows
                    // are still worth having, and the restore still has to happen either way.
                    outcome = ScenarioOutcome.Fail($"threw {ex.GetType().Name}: {ex.Message}");
                }

                switch (outcome.Status)
                {
                    case ScenarioStatus.Passed:
                        Console.WriteLine($"   PASS  {scenario.SmokeRow}");
                        break;
                    case ScenarioStatus.Skipped:
                        skips++;
                        Console.WriteLine($"   SKIP  {scenario.SmokeRow}");
                        break;
                    default:
                        failures++;
                        Console.WriteLine($"   FAIL  {scenario.SmokeRow}");
                        break;
                }
                Console.WriteLine($"         {outcome.Detail}");

                // A catcher that stopped hearing turns every later absence into a false green, so a new
                // fault ends the run rather than being noted and walked past.
                if (catcher.RequestFaults.Count > faultsSeen)
                {
                    var fault = catcher.RequestFaults[^1];
                    Console.WriteLine();
                    Console.WriteLine($"The webhook catcher recorded a request fault ({fault.GetType().Name}: "
                        + $"{fault.Message}). A lost POST makes every later row's silence meaningless, so the "
                        + "run stops here.");
                    failures++;
                    break;
                }
            }

            Console.WriteLine();
            Console.WriteLine($"{ScenarioTable.All.Count - failures - skips} passed, {failures} failed, {skips} skipped.");
            if (skips > 0)
            {
                Console.WriteLine("A skipped row is NOT a covered row — its smoke-list box stays unticked.");
            }
            return failures == 0 ? 0 : 1;
        }
        finally
        {
            // Restore first, catcher second: the profile matters and the port does not.
            var report = await guard.RestoreAsync().ConfigureAwait(false);
            Console.WriteLine(report.ToString());
            guard.Dispose();
            await catcher.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Refuses to run while RoRoRo is already up, and this is the only refusal in the tool with an
    /// audience behind it.
    /// <para>
    /// <c>DiscordConfigService</c> loads <c>discord.dat</c> ONCE, at startup, and
    /// <c>AlertDispatcher</c> reads its in-memory <c>Current</c> on every dispatch. Nothing re-reads
    /// the file except a mutation through the app's own UI. So a running app holds the webhook URLs and
    /// the destination set it started with — the REAL ones — and the harness's swap, however cleanly it
    /// lands on disk and reads back, would not reach it. A test breach would then post to whatever that
    /// running app already believes, which on a profile with clan routing configured is the real clan
    /// channel.
    /// </para>
    /// <para>
    /// The read-back guard cannot see this: the file is correct and the app is simply not reading it.
    /// The only way to be sure the app has the harness's config is for the app to start after it, so
    /// that is what the runner requires.
    /// </para>
    /// </summary>
    private static async Task<bool> RefuseIfAppIsUpAsync()
    {
        using var probe = new MetricReporter(SmokePluginIds.Reporting);
        if (!await probe.IsHostReachableAsync().ConfigureAwait(false)) return false;

        Console.WriteLine("RoRoRo is already running, and that is why this is refusing to start.");
        Console.WriteLine();
        Console.WriteLine("  The app reads discord.dat once, at startup. A session that is already up holds");
        Console.WriteLine("  your REAL webhook URLs and your real destination set in memory, and would keep");
        Console.WriteLine("  using them however cleanly this tool rewrites the file. A test alert could land in");
        Console.WriteLine("  your clan channel.");
        Console.WriteLine();
        Console.WriteLine("  Quit RoRoRo from the tray, run this again, and start RoRoRo when it asks you to.");
        return true;
    }

    /// <summary>
    /// Whether phone credentials are saved. Read through the production store so the answer is the same
    /// one <c>AlertRouter</c> gets, rather than "notify.dat exists" — a file can be there with the
    /// provider set to none, and that profile CAN run the fallback row.
    /// <para>
    /// Nothing here keeps, prints or logs a field of that record; the question asked is a single bool.
    /// The harness does not back <c>notify.dat</c> up and never writes it, which is why the fallback row
    /// skips rather than blanking one to become runnable.
    /// </para>
    /// </summary>
    private static async Task<bool> PhoneIsConfiguredAsync(string dataRoot)
    {
        try
        {
            var store = new PhoneNotifyConfigStore(Path.Combine(dataRoot, "notify.dat"));
            return (await store.LoadAsync().ConfigureAwait(false)).IsConfigured;
        }
        catch (Exception ex)
        {
            // Unreadable is treated as CONFIGURED: the cautious direction. Guessing "not configured"
            // would route Phone and could page a real phone on a profile this could not read.
            Console.WriteLine($"[setup] could not read the phone config ({ex.GetType().Name}); assuming "
                + "credentials are saved and leaving Phone out of the destination set.");
            return true;
        }
    }

    /// <summary>
    /// Everything the app has to find on disk before it starts: both webhook URLs pointing at the
    /// catcher, a destination set that exercises all four legs, the opt-in on, streamer mode on, the
    /// rules file, and the harness's own consent grant.
    /// <para>
    /// <b>The read-back is the gate.</b> Of the three things that can go wrong with this harness two are
    /// private annoyances; the only one with an audience is a test alert reaching the real clan channel
    /// because the swap silently did not take. So nothing is reported until
    /// <see cref="ProfileGuard.VerifyDiscordSwapAsync"/> answers true, and a false answer returns here
    /// with no metric ever sent.
    /// </para>
    /// <para>
    /// <b>Streamer mode goes on for the whole run, not just its own row.</b>
    /// <c>StreamerIdentityProvider.IsActive</c> is read once at startup and changed only by the app's
    /// own toggle, so a mid-run flip would have no effect on a running app. Every row except the naming
    /// one is indifferent to it, and the guard's single-key save-and-restore already covered this key —
    /// <c>BooleanSetting.StreamerMode</c> — so nothing needed extending.
    /// </para>
    /// </summary>
    private static async Task<bool> SetUpProfileAsync(
        ProfileGuard guard, WebhookCatcher catcher, bool phoneConfigured)
    {
        // ORDER MATTERS, in two ways, and neither is cosmetic.
        //
        // Phone LAST because AlertRouter resolves in order and dedupes: an unconfigured Phone falls
        // back to Local and must dedupe against the Local already there rather than add a second line.
        //
        // Local NOT FIRST because AlertDispatcher's loop is sequential and its cooldown stamp lands
        // inside it, per destination, after that destination's send. Local's "send" is
        // TrayService.ShowToast, which calls into a WPF TaskbarIcon from whatever gRPC handler thread
        // raised the breach; if that throws, the loop ends there and every later destination in the
        // fan-out is lost along with the stamp. Putting a webhook first means the stamp has already
        // landed and both channel posts have already gone out before anything touches the tray. The
        // desktop row is unaffected either way — the "Alert → Local" line is written BEFORE the send,
        // which is exactly the line the design says stands in for the toast (§2: whether the shell
        // then drew it is the one manual check that stays).
        var destinations = new List<AlertDestination>
        {
            AlertDestination.Mine,
            AlertDestination.Clan,
            AlertDestination.Local,
        };
        if (!phoneConfigured) destinations.Add(AlertDestination.Phone);

        Console.WriteLine($"[setup] metric breaches route to {string.Join(", ", destinations)}");
        if (phoneConfigured)
        {
            Console.WriteLine("[setup] phone credentials are saved, so Phone is NOT routed — the fallback row "
                + "will skip rather than page a real phone.");
        }

        await guard.MutateDiscordAsync(config => config with
        {
            MineWebhookUrl = catcher.MineUrl,
            ClanWebhookUrl = catcher.ClanUrl,
            MetricBreachDestinations = destinations,
        }).ConfigureAwait(false);

        if (!await guard.VerifyDiscordSwapAsync(catcher.MineUrl, catcher.ClanUrl).ConfigureAwait(false))
        {
            Console.WriteLine();
            Console.WriteLine("ABORTING before a single metric is reported: the webhook swap did not read back.");
            Console.WriteLine("  The lines above say which half disagreed. Until both URLs are the catcher's,");
            Console.WriteLine("  a breach could reach your real clan channel, so nothing will be sent. The");
            Console.WriteLine("  profile is about to be put back from the backups this run took.");
            return false;
        }
        Console.WriteLine("[setup] both webhook URLs read back as the catcher's");

        await guard.SetMetricAlertsEnabledAsync(true).ConfigureAwait(false);
        await guard.SetStreamerModeAsync(true).ConfigureAwait(false);
        await guard.GrantConsentAsync(
            SmokePluginIds.Reporting,
            [PluginCapability.HostMetricsReport, PluginCapability.HostQueriesAccounts]).ConfigureAwait(false);
        await guard.WriteRulesAsync(SmokeRules.ToJson(SmokeRules.Canonical)).ConfigureAwait(false);
        Console.WriteLine($"[setup] {SmokeRules.Canonical.Count} rules written");
        return true;
    }

    /// <summary>
    /// Waits for the plugin pipe to answer, which is how the runner knows the app started — and,
    /// because the setup above already landed, that it started holding the harness's config.
    /// <para>
    /// The pipe binds BEFORE the startup gate's modals (<c>App.StartPluginHostListener</c>,
    /// fire-and-forget, deliberately), so this does not wait on a human dismissing a dialog. It may
    /// well wait on a human starting the app, which is what the window is for.
    /// </para>
    /// </summary>
    private static async Task<bool> WaitForAppAsync(MetricReporter reporter)
    {
        if (await reporter.IsHostReachableAsync().ConfigureAwait(false))
        {
            Console.WriteLine("[setup] RoRoRo is answering on the plugin pipe.");
            return true;
        }

        Console.WriteLine();
        Console.WriteLine($"START RORORO NOW. Waiting up to {AppStartupWait.TotalMinutes:0} minutes for it to "
            + "answer on the plugin pipe.");
        Console.WriteLine("  It has to start AFTER this point: that is how it picks up the harness's webhook");
        Console.WriteLine("  URLs, its destination set, the opt-in and streamer mode.");

        var deadline = DateTime.UtcNow + AppStartupWait;
        while (DateTime.UtcNow < deadline)
        {
            if (_stopRequested)
            {
                Console.WriteLine("Stopped by Ctrl-C while waiting for the app.");
                return false;
            }
            if (await reporter.IsHostReachableAsync().ConfigureAwait(false))
            {
                Console.WriteLine("[setup] RoRoRo is answering on the plugin pipe.");
                return true;
            }
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }

        Console.WriteLine($"RoRoRo never answered on {reporter.PipeName}. Nothing was reported.");
        return false;
    }

    /// <summary>
    /// Finds an account id the host can resolve to a name, for the masked-naming row alone. An explicit
    /// <c>--subject</c> wins; otherwise the host is asked through <c>GetAccounts</c>, which is why the
    /// harness's grant includes <c>host.queries.accounts</c>.
    /// <para>
    /// A failure here is never fatal: the naming row skips and says so, and the other fifteen do not
    /// care. The masked name is held in memory for one comparison and never printed — printing it would
    /// put a name the app is masking into a console log.
    /// </para>
    /// </summary>
    private static async Task ResolveNamedSubjectAsync(ScenarioContext context, string? subjectOverride)
    {
        try
        {
            var accounts = await context.Reporter.SavedAccountsAsync().ConfigureAwait(false);
            var chosen = subjectOverride is null
                ? accounts.FirstOrDefault()
                : accounts.FirstOrDefault(a => string.Equals(a.AccountId, subjectOverride, StringComparison.OrdinalIgnoreCase));

            if (chosen is null)
            {
                Console.WriteLine(subjectOverride is null
                    ? "[setup] the host reports no saved accounts, so the masked-naming row will skip."
                    : $"[setup] the host does not know the account id passed to --subject, so the masked-naming "
                        + "row will skip.");
                return;
            }

            context.ResolvableSubject = chosen.AccountId;
            context.MaskedName = chosen.DisplayName;
            if (string.IsNullOrEmpty(chosen.DisplayName))
            {
                Console.WriteLine("[setup] the host reports an empty display name for the chosen account, so the "
                    + "masked-naming row will skip.");
                context.MaskedName = null;
            }
            else
            {
                Console.WriteLine("[setup] the masked-naming row has an account the host can name.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[setup] could not ask the host which accounts exist ({ex.GetType().Name}), so the "
                + "masked-naming row will skip.");
        }
    }

    /// <summary>
    /// Ctrl-C reaches the restore by NOT killing the process: <c>e.Cancel = true</c> stops the CLR
    /// terminating, the flag is checked between scenarios, and the loop unwinds into
    /// <see cref="RunAsync"/>'s <c>finally</c>, which restores. That is why a first Ctrl-C does not
    /// return immediately — the scenario in flight finishes first, because abandoning one mid-write and
    /// restoring underneath it is how a profile gets left half-swapped.
    /// <para>
    /// A SECOND Ctrl-C is allowed to kill. Someone pressing it twice wants out now, and the marker plus
    /// <c>--recover</c> is exactly the net for a run that died — the same net a power cut needs.
    /// </para>
    /// </summary>
    private static void HookCtrlC()
    {
        Console.CancelKeyPress += (_, e) =>
        {
            if (_stopRequested) return;   // second press: e.Cancel stays false and the process dies
            _stopRequested = true;
            e.Cancel = true;
            Console.WriteLine();
            Console.WriteLine("Ctrl-C: stopping after the scenario in flight, then putting the profile back. "
                + "Press Ctrl-C again to kill it instead (then run --recover).");
        };
    }
}
