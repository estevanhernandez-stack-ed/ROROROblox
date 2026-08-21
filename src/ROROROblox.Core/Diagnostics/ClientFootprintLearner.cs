namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Learns what a Roblox client actually costs on THIS machine, instead of trusting one measured
/// on someone else's (F-083).
/// <para>
/// <see cref="MemoryDefaults.ExpectedClientMb"/> is 2650 — median private bytes across 8 concurrent
/// clients, Pet Sim 99, Roblox 733, on a 47 GB machine, on 2026-08-07. Every one of those is a
/// variable: hardware, game, and client build all move the number, and the clan runs 16 to 64 GB.
/// Meanwhile the watchdog samples every tracked client every 30 seconds and has this machine's real
/// answer in hand, and nothing was reading it.
/// </para>
/// <para>
/// <b>WHAT IT FEEDS, AND WHAT IT DELIBERATELY DOES NOT.</b> The launch advisor only — the
/// "how many more will fit" arithmetic, which is an ESTIMATE and should use the best one available.
/// It must never reach:
/// </para>
/// <list type="bullet">
/// <item>the anomaly cap, because a heavy machine would teach itself that heavy is normal and the
/// cap would stop firing — which is F-082 reintroduced by a new route;</item>
/// <item>the headroom trigger, because that reads free memory directly. It is a measurement, and
/// replacing a measurement with an estimate is a downgrade however good the estimate.</item>
/// </list>
/// <para>
/// <b>NOT PERSISTED, deliberately.</b> The learner starts from the seed each launch and re-earns
/// its estimate in about ten minutes of clients running, which this audience does routinely.
/// Persisting it would mean a new settings field, and F-093 is a fresh reminder of what a settings
/// field costs when nothing reads it. <see cref="NoteClientVersion"/> exists anyway so the
/// reset-on-upgrade rule is expressed now rather than retrofitted if that changes.
/// </para>
/// </summary>
public sealed class ClientFootprintLearner
{
    /// <summary>Used until <see cref="MinimumSamples"/> readings exist. The measured 2026-08-07 figure.</summary>
    public const int SeedMb = MemoryDefaults.ExpectedClientMb;

    /// <summary>Below this a "client" is something else — a crashed shell, a process mid-teardown.</summary>
    public const int FloorMb = 1200;

    /// <summary>Above this the sample is an outlier, not a footprint. Well clear of the 3280 MB peak.</summary>
    public const int CeilingMb = 5000;

    /// <summary>
    /// Readings required before the learned value replaces the seed. At one sample per settled
    /// client per 30s, a single client earns this in ten minutes; three clients in under four.
    /// </summary>
    public const int MinimumSamples = 20;

    /// <summary>
    /// Bounded so a machine left running for days does not accumulate an unbounded list, and so the
    /// estimate tracks the recent past rather than averaging over a Roblox update. 200 readings is
    /// roughly the last hour and a half of a single client.
    /// </summary>
    private const int Capacity = 200;

    private readonly List<int> _samplesMb = new(Capacity);
    private int _next;
    private string? _clientVersion;
    private readonly object _gate = new();

    /// <summary>How many readings are informing the current estimate.</summary>
    public int SampleCount { get { lock (_gate) { return _samplesMb.Count; } } }

    /// <summary>
    /// The per-client estimate the advisor should use: the seed until there is enough evidence,
    /// then the 75th percentile of settled readings, clamped.
    /// <para>
    /// p75 rather than the median because the advisor's job is to avoid promising room that is not
    /// there. Half the clients costing more than the median is fine as a description and useless as
    /// a budget — the question being asked is "will another one fit", and the honest answer leans
    /// toward the expensive end of what this machine actually does.
    /// </para>
    /// </summary>
    public int EstimateMb
    {
        get
        {
            lock (_gate)
            {
                if (_samplesMb.Count < MinimumSamples) return SeedMb;

                var ordered = _samplesMb.Order().ToList();

                // Nearest-rank p75. Integer arithmetic on a bounded list, so no interpolation and
                // no floating point in a number that ends up dividing free memory.
                var rank = (int)Math.Ceiling(0.75 * ordered.Count) - 1;
                return Math.Clamp(ordered[Math.Clamp(rank, 0, ordered.Count - 1)], FloorMb, CeilingMb);
            }
        }
    }

    /// <summary>
    /// Records one SETTLED client's private bytes. "Settled" is the caller's judgement — the
    /// watchdog already computes <c>elapsed &gt;= MinimumObservation</c> for its growth slope, and
    /// that is the same question, so it is asked once there rather than twice.
    /// </summary>
    /// <param name="belowReserve">Whether the machine was already past its reserve when this was
    /// read. Those samples are DISCARDED: a client under memory pressure is being squeezed by the
    /// OS, so its footprint describes the pressure rather than the client, and learning from it
    /// would teach the advisor that clients are cheap exactly when they are not.</param>
    public void Observe(long privateBytes, bool belowReserve)
    {
        if (belowReserve) return;

        var mb = (int)(privateBytes / (1024L * 1024));
        if (mb < FloorMb || mb > CeilingMb) return;

        lock (_gate)
        {
            if (_samplesMb.Count < Capacity)
            {
                _samplesMb.Add(mb);
            }
            else
            {
                _samplesMb[_next] = mb;
                _next = (_next + 1) % Capacity;
            }
        }
    }

    /// <summary>
    /// Drops everything learned when the Roblox client build changes. A new build can move the
    /// footprint by hundreds of megabytes, and an estimate averaged across an upgrade describes a
    /// client that no longer exists. The first call simply records the version.
    /// </summary>
    public void NoteClientVersion(string? version)
    {
        lock (_gate)
        {
            if (string.Equals(_clientVersion, version, StringComparison.OrdinalIgnoreCase)) return;

            var hadVersion = _clientVersion is not null;
            _clientVersion = version;
            if (hadVersion)
            {
                _samplesMb.Clear();
                _next = 0;
            }
        }
    }
}
