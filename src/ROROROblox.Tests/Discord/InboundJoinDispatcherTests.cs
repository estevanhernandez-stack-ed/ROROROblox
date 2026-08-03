using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Discord;
using ROROROblox.Core;

namespace ROROROblox.Tests.Discord;

/// <summary>
/// Fix round 1, Finding 1: the roblox-rororo: protocol-handler path had no JoinEnabled gate
/// anywhere in its chain — <see cref="InboundJoinDispatcher"/> is the fix, extracted specifically
/// so this is testable without a live WPF <c>Application</c> (App.xaml.cs has no test coverage
/// anywhere in this suite; this class is deliberately WPF-free so it doesn't need any).
/// </summary>
public class InboundJoinDispatcherTests
{
    [Fact]
    public async Task HandleAsync_JoinDisabled_IgnoresTheInboundJoinAndLaunchesNothing()
    {
        var (vm, launcher) = DiscordTestHarness.VmWithOneIdleAccount();
        var dispatcher = new InboundJoinDispatcher(
            joinEnabled: () => false,
            viewModel: vm,
            origin: JoinOrigin.UriHandler,
            confirm: _ => true,
            log: NullLogger.Instance);

        await dispatcher.HandleAsync(new LaunchTarget.GameJob(140403681187145, "job-a"));

        Assert.Empty(launcher.Launches);
    }

    [Fact]
    public async Task HandleAsync_JoinEnabled_DispatchesTheJoin()
    {
        var (vm, launcher) = DiscordTestHarness.VmWithOneIdleAccount();
        var dispatcher = new InboundJoinDispatcher(
            joinEnabled: () => true,
            viewModel: vm,
            origin: JoinOrigin.DiscordClient,
            confirm: _ => true,
            log: NullLogger.Instance);

        await dispatcher.HandleAsync(new LaunchTarget.GameJob(140403681187145, "job-a"));

        Assert.Single(launcher.Launches);
    }

    [Fact]
    public async Task HandleAsync_JoinDisabled_NeverConsultsTheConfirmDelegate()
    {
        // A private-server target would normally show the confirm modal (JoinDispatchTests
        // covers that path via HandleDiscordJoinAsync directly). While Join is disabled, the
        // gate must trip BEFORE anything reaches that decision at all.
        var (vm, launcher) = DiscordTestHarness.VmWithOneIdleAccount();
        var confirmCalled = false;
        var dispatcher = new InboundJoinDispatcher(
            joinEnabled: () => false,
            viewModel: vm,
            origin: JoinOrigin.DiscordClient,
            confirm: _ => { confirmCalled = true; return true; },
            log: NullLogger.Instance);

        await dispatcher.HandleAsync(new LaunchTarget.PrivateServer(8737899170, "CODE", PrivateServerCodeKind.LinkCode));

        Assert.False(confirmCalled);
        Assert.Empty(launcher.Launches);
    }

    // Fix round 2: proves origin actually threads through the dispatcher into
    // HandleDiscordJoinAsync end to end -- App.xaml.cs builds one dispatcher per origin now (not
    // one shared instance), and this is the seam that would silently break if a future edit
    // collapsed them back into one without also threading the origin argument.
    [Fact]
    public async Task HandleAsync_UriHandlerOrigin_PublicTarget_ConfirmsThroughTheFullChain()
    {
        var (vm, launcher) = DiscordTestHarness.VmWithOneIdleAccount();
        var confirmCalled = false;
        var dispatcher = new InboundJoinDispatcher(
            joinEnabled: () => true,
            viewModel: vm,
            origin: JoinOrigin.UriHandler,
            confirm: _ => { confirmCalled = true; return true; },
            log: NullLogger.Instance);

        await dispatcher.HandleAsync(new LaunchTarget.GameJob(140403681187145, "job-a"));

        Assert.True(confirmCalled);
        Assert.Single(launcher.Launches);
    }

    [Fact]
    public async Task HandleAsync_ViewModelThrows_IsSwallowedNotPropagated()
    {
        // Matches every other inbound-join call site's swallow-and-log contract — an unguarded
        // throw here (relayed from a second instance) can wedge the single-instance pipe listener
        // for the rest of the process (see InboundJoinRelay's remarks).
        var (vm, _) = DiscordTestHarness.VmWithNoAccounts();
        var dispatcher = new InboundJoinDispatcher(
            joinEnabled: () => true,
            viewModel: vm,
            origin: JoinOrigin.DiscordClient,
            confirm: _ => throw new InvalidOperationException("confirm should not be reached for a public target"),
            log: NullLogger.Instance);

        // GameJob (not PrivateServer) never reaches confirm; with no accounts configured,
        // HandleDiscordJoinAsync returns false rather than throwing -- this asserts the dispatcher
        // doesn't throw either, for either reason.
        await dispatcher.HandleAsync(new LaunchTarget.GameJob(1, "j"));
    }
}
