using ROROROblox.App.Discord;
using ROROROblox.Core;

namespace ROROROblox.Tests.Discord;

public class InboundJoinRelayTests
{
    private const string ValidJoinUri = "roblox-rororo://join/g%7C140403681187145%7Cjob-a";

    /// <summary>
    /// Fix round 1, Critical finding: SingleInstanceGuard's pipe-listener thread calls the
    /// relay path through a Dispatcher.Invoke that only catches OperationCanceledException and
    /// IOException. Before this fix, an unguarded throw from a JoinRequested subscriber would
    /// propagate out of that Invoke, kill the listener task for the rest of the process, and
    /// wedge single-instance silently — every later launch would time out in SignalExisting and
    /// the second instance would exit believing it had signalled the primary.
    /// <para>
    /// SingleInstanceGuard's own pipe machinery is awkward to drive in a headless test (no STA/
    /// WPF Application test infrastructure exists in this repo — see RosterSnapshotProjectionTests'
    /// note on why Application.Current is deliberately avoided in tests), so this tests at the
    /// level the exception boundary actually lives: InboundJoinRelay.Handle, the guarded
    /// implementation App.xaml.cs's cold-start and relay call sites both delegate to.
    /// </para>
    /// </summary>
    [Fact]
    public void Handle_SubscriberThrows_ExceptionDoesNotEscape_AndRelayStaysUsableForTheNextJoin()
    {
        var relay = new InboundJoinRelay(log: null);
        EventHandler<LaunchTarget> throwingSubscriber = (_, _) => throw new InvalidOperationException("boom");
        relay.JoinRequested += throwingSubscriber;

        var escaped = Record.Exception(() => relay.Handle(ValidJoinUri, "test"));
        Assert.Null(escaped);

        // Prove the relay — and by extension the pipe-listener thread it runs on in production —
        // is still alive and able to process a SUBSEQUENT join, not just that this one call
        // didn't throw. Swap in a well-behaved subscriber for the next join, the way a real
        // second Join click would arrive later in the same process.
        relay.JoinRequested -= throwingSubscriber;
        LaunchTarget? received = null;
        relay.JoinRequested += (_, target) => received = target;

        relay.Handle(ValidJoinUri, "test");

        Assert.NotNull(received);
        Assert.Equal("job-a", Assert.IsType<LaunchTarget.GameJob>(received).JobId);
    }

    [Fact]
    public void Handle_GarbagePayload_DoesNotRaiseJoinRequested()
    {
        var relay = new InboundJoinRelay(log: null);
        var raised = false;
        relay.JoinRequested += (_, _) => raised = true;

        relay.Handle("roblox-rororo://join/not-a-secret", "test");

        Assert.False(raised);
    }
}
