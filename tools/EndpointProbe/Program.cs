namespace ROROROblox.EndpointProbe;

/// <summary>
/// Entry point. Explicitly named and internal rather than top-level statements, for the reason
/// MetricSmoke's is: a global <c>Program</c> would shadow the app's in ROROROblox.Tests, which
/// references this project. The command line lives in <see cref="ProbeCli"/>, public so tests can
/// drive it with captured writers.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        try
        {
            return await ProbeCli.RunAsync(args, Console.Out, Console.Error, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
    }
}
