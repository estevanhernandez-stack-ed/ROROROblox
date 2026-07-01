using ROROROblox.App.ViewModels;

namespace ROROROblox.Tests;

/// <summary>
/// RelayCommand.Execute deliberately never lets an exception escape (async void + WPF = crash),
/// but "never escape" must not mean "vanish" — the 2026-06-12 review found several command
/// bodies with unguarded awaits whose failures left no trace, contradicting the
/// WireGlobalExceptionHandlers "we just have evidence afterward" philosophy. These tests pin
/// the contract: swallowed exceptions are reported to the OnExceptionSwallowed hook.
/// </summary>
public class RelayCommandTests
{
    [Fact]
    public async Task Execute_SyncExecutorThrows_ReportsToHookAndDoesNotPropagate()
    {
        var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        RelayCommand.OnExceptionSwallowed = ex => reported.TrySetResult(ex);
        try
        {
            var command = new RelayCommand(new Func<Task>(() => throw new InvalidOperationException("boom")));

            command.Execute(null); // must not throw

            var observed = await reported.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsType<InvalidOperationException>(observed);
            Assert.Equal("boom", observed.Message);
        }
        finally
        {
            RelayCommand.OnExceptionSwallowed = null;
        }
    }

    [Fact]
    public async Task Execute_AsyncExecutorFaultsAfterAwait_StillReported()
    {
        // The dangerous case: the executor faults on a continuation, past the synchronous
        // portion of Execute. Without containment inside Execute's own await, this is the
        // path that vanishes entirely.
        var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        RelayCommand.OnExceptionSwallowed = ex => reported.TrySetResult(ex);
        try
        {
            var command = new RelayCommand(new Func<Task>(async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("late boom");
            }));

            command.Execute(null);

            var observed = await reported.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("late boom", observed.Message);
        }
        finally
        {
            RelayCommand.OnExceptionSwallowed = null;
        }
    }

    [Fact]
    public void Execute_NoHookWired_StillSwallowsQuietly()
    {
        // App startup wires the hook, but commands constructed before that (or in tests)
        // must keep the original no-crash behavior.
        RelayCommand.OnExceptionSwallowed = null;
        var command = new RelayCommand(new Func<Task>(() => throw new InvalidOperationException("unreported")));

        command.Execute(null); // must not throw
    }
}
