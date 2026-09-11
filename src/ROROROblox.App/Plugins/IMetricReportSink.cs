namespace ROROROblox.App.Plugins;

/// <summary>
/// Host-side sink for a plugin's <c>ReportMetric</c> RPC. Mirrors
/// <see cref="IAccountActivityMarker"/>'s adapter role: it takes the plugin-facing, stringified
/// shape and does the host-side work, so the RPC handler stays a pass-through with no reasoning
/// of its own.
/// <para>
/// Deliberately returns nothing. The RPC is fire-and-forget: a plugin cannot learn whether its
/// report made the user's phone ring, and does not need to.
/// </para>
/// </summary>
public interface IMetricReportSink
{
    /// <summary>
    /// Record one reported number. Never throws — this is called from a gRPC handler, and a bad
    /// report must cost the report, never the plugin host.
    /// </summary>
    /// <param name="subjectId">A RoRoRo account id as a string, or anything else, which is
    /// treated as the global carrier rather than rejected.</param>
    /// <param name="observedAtUnixMs">When the PLUGIN observed it, UTC. A value meaningfully in
    /// the host's future is dropped and logged.</param>
    void Report(string subjectId, string metricId, double value, long observedAtUnixMs);
}
