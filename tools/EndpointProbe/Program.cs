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
        return await RunAsync(args, Console.Out, Console.Error, cts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// <see cref="Main"/> behind a writer pair so a test can drive the catch blocks. The generic catch
    /// prints the exception TYPE only: an unhandled exception's message can echo whatever it was given,
    /// and a cookie is the one thing this tool must never print.
    /// </summary>
    internal static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        try
        {
            return await ProbeCli.RunAsync(args, stdout, stderr, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            stderr.WriteLine("Cancelled.");
            return 130;
        }
        catch (Exception ex)
        {
            stderr.WriteLine("The probe stopped: " + ex.GetType().Name);
            return 1;
        }
    }
}
