using System.IO.Pipes;
using Grpc.Core;
using Grpc.Net.Client;
using ROROROblox.App.Plugins;
using ROROROblox.PluginContract;

namespace ROROROblox.MetricSmoke;

/// <summary>
/// A gRPC client over the named pipe RoRoRo hosts for out-of-process plugins -- reporting a metric
/// exactly as a consented plugin would, no different.
/// <para>
/// Design §0.1 is why this class needs so little: <c>CapabilityInterceptor.EnforceCapability</c>
/// checks only that the method appears in <c>RpcMethodCapabilityMap</c>, that an
/// <c>x-plugin-id</c> request header is present, and that the consent lookup for that id grants the
/// required capability. It never consults the installed-plugin registry. So this is a pipe
/// connection, one header, and whatever consent record the harness's guard already granted --
/// no manifest, no <c>/plugins/</c> install, no consent sheet.
/// </para>
/// <para>
/// The channel construction below is copied from
/// <c>ROROROblox.PluginTestHarness.EndToEndContractTests.ConnectChannel</c> -- the maintained,
/// already-proven way this codebase dials the plugin pipe -- rather than invented fresh. The one
/// addition on top of that copy is <see cref="CallTimeout"/>: the harness's version never needed a
/// bound because its tests always start the server first. This class runs unattended (Task 6 drives
/// it against a live app with nobody watching), so both <see cref="IsHostReachableAsync"/> and
/// <see cref="ReportAsync"/> need to fail fast rather than hang: an unreachable liveness probe has to
/// answer "not reachable" instead of blocking forever, and a wedged report has to surface as a
/// diagnosable exception instead of stalling the run with no clue why.
/// </para>
/// </summary>
public sealed class MetricReporter : IDisposable
{
    /// <summary>
    /// The bound on every pipe connect attempt and every RPC this class makes --
    /// <see cref="SocketsHttpHandler.ConnectTimeout"/> on the channel's handler, the deadline on
    /// <see cref="IsHostReachableAsync"/>'s <c>GetHostInfo</c> probe, and the deadline on
    /// <see cref="ReportAsync"/>'s <c>ReportMetric</c> call. One value for all three: short enough
    /// that an automated run polling the probe in a loop does not stall for long on a host that
    /// isn't up yet, long enough that a healthy pipe under ordinary load answers well inside it.
    /// <c>ReportMetric</c>'s production handler (<c>PluginHostService.ReportMetric</c>) is a
    /// synchronous pass-through -- it calls <c>IMetricReportSink.Report</c> (void, no I/O) and
    /// returns immediately, never awaiting the webhook POST or alert dispatch inside the RPC's
    /// response path -- so under normal conditions the call returns in milliseconds; this bound
    /// exists purely so a wedged pipe turns into a caught, reportable exception within a few
    /// seconds in Task 6's unattended run, rather than an indefinite hang with no diagnosis.
    /// </summary>
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(3);

    private readonly GrpcChannel _channel;
    private readonly RoRoRoHost.RoRoRoHostClient _client;

    public string PipeName { get; }
    public string PluginId { get; }

    /// <summary>
    /// Defaults to the production plugin-host pipe
    /// (<see cref="PluginHostStartupService.DefaultPipeName"/>), sourced from the same constant
    /// production itself binds to -- so a rename of the pipe cannot silently break a caller that
    /// never had to spell its name. Use the two-argument constructor's explicit
    /// <paramref name="pipeName"/> override to point at a scratch or nonexistent pipe (tests do
    /// exactly this).
    /// </summary>
    public MetricReporter(string pluginId)
        : this(PluginHostStartupService.DefaultPipeName, pluginId)
    {
    }

    public MetricReporter(string pipeName, string pluginId)
    {
        PipeName = pipeName;
        PluginId = pluginId;
        _channel = ConnectChannel(pipeName);
        _client = new RoRoRoHost.RoRoRoHostClient(_channel);
    }

    /// <summary>
    /// The ungated liveness probe (<c>GetHostInfo</c> maps to <c>null</c> in
    /// <c>RpcMethodCapabilityMap</c> -- a free read, no consent needed). Never throws: any failure
    /// to get a response inside <see cref="CallTimeout"/> -- the pipe not existing yet, a
    /// connect that never completes, an RpcException the server or transport raises -- means the
    /// same thing to a caller polling this in an automated run: not reachable yet. Distinguishing
    /// *why* is not this method's job.
    /// <para>
    /// Answered per design §4.3 / the plan's task-3 brief: <c>App.xaml.cs</c>'s
    /// <c>StartPluginHostListener</c> binds the plugin pipe BEFORE the startup gate's modals,
    /// deliberately and fire-and-forget (its own comment: "Binding here means the pipe is
    /// reachable while the dialog waits for a human"). So this probe can succeed while a gate
    /// modal is still on screen with nobody there to click it -- an automated run does not need to
    /// wait for, or simulate, a human dismissing anything before the pipe answers.
    /// </para>
    /// </summary>
    public async Task<bool> IsHostReachableAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(CallTimeout);
            await _client.GetHostInfoAsync(
                new Empty(),
                deadline: DateTime.UtcNow.Add(CallTimeout),
                cancellationToken: cts.Token);
            return true;
        }
        catch (Exception)
        {
            // Broad and deliberate: this method's entire contract is a bool, not an exception
            // hierarchy. An RpcException (Unavailable/DeadlineExceeded from the transport), an
            // OperationCanceledException from the timeout above, or an IOException from
            // NamedPipeClientStream failing to find the pipe at all -- every one of them means
            // "not reachable" to a caller that just wants to know whether it is safe to proceed.
            return false;
        }
    }

    /// <summary>
    /// Reports one metric as the consented plugin <see cref="PluginId"/>. The interceptor resolves
    /// the plugin id from the <c>x-plugin-id</c> header (production wiring: the accessor itself
    /// returns null; the header is the only path), the same convention every call site in
    /// <c>PluginHostService</c>/<c>CapabilityInterceptor</c> and the harness's own tests use -- there
    /// is no shared constant for it in this codebase to reference instead.
    /// <para>
    /// Bounded by <see cref="CallTimeout"/>, same as the liveness probe and for the same reason
    /// (Task 6 runs this unattended): unlike <see cref="IsHostReachableAsync"/> this method does
    /// NOT swallow the failure into a bool -- a report that cannot be confirmed sent is exactly the
    /// kind of silent gap this harness exists to catch, so a timeout here still throws (a
    /// <see cref="RpcException"/> with <see cref="StatusCode.DeadlineExceeded"/>), it just throws
    /// within seconds instead of hanging indefinitely with no diagnosis.
    /// </para>
    /// </summary>
    public async Task ReportAsync(string subjectId, string metricId, double value, DateTimeOffset observedAt)
    {
        var headers = new Metadata { { "x-plugin-id", PluginId } };
        await _client.ReportMetricAsync(
            new MetricReport
            {
                SubjectId = subjectId,
                MetricId = metricId,
                Value = value,
                ObservedAtUnixMs = ConvertToUnixMilliseconds(observedAt),
            },
            headers: headers,
            deadline: DateTime.UtcNow.Add(CallTimeout));
    }

    /// <summary>
    /// The exact conversion <see cref="ReportAsync"/> sends over the wire, pulled out as its own
    /// testable step. <see cref="DateTimeOffset.ToUnixTimeMilliseconds"/> is documented to truncate
    /// rather than round: a sub-millisecond remainder is dropped, never rounded up. That direction
    /// matters here specifically -- <c>observed_at_unix_ms</c>'s own doc comment says the host drops
    /// anything stamped in its own future, so rounding UP on a report observed right now could tip
    /// it into "the future" by a fraction of a millisecond and get it silently dropped. Truncating
    /// down never does that.
    /// </summary>
    public static long ConvertToUnixMilliseconds(DateTimeOffset observedAt) => observedAt.ToUnixTimeMilliseconds();

    public void Dispose() => _channel.Dispose();

    // Copied from ROROROblox.PluginTestHarness.EndToEndContractTests.ConnectChannel, plus
    // ConnectTimeout -- see the class doc comment for why that one addition is needed here and
    // was not in the source it was copied from.
    private static GrpcChannel ConnectChannel(string pipeName)
    {
        return GrpcChannel.ForAddress("http://pipe", new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler
            {
                ConnectTimeout = CallTimeout,
                ConnectCallback = async (ctx, ct) =>
                {
                    var pipe = new NamedPipeClientStream(".", pipeName,
                        PipeDirection.InOut, PipeOptions.Asynchronous);
                    await pipe.ConnectAsync(ct);
                    return pipe;
                },
            },
        });
    }
}
