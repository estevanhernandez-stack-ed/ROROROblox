# Join via friend everywhere, and auto-rejoin: implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A `JoinViaFriend` account never joins directly on any launch path, and an opted-in alt whose client drops out of the game is stopped and relaunched automatically.

**Architecture:**
- Two pure rules carry the decisions, so they are unit-tested without the view model:
  - `FlaggedLaunchRule` decides direct, follow, or not-joinable for a flagged account.
  - `AutoRejoinMonitor` decides from presence and process state when to rejoin or pause.
- `MainViewModel.LaunchAccountAsync`, where every launch path already meets, applies the first rule.
- The view model's 30 s `PeriodicTick` drives the second rule.
- The plugin contract gains an additive `reason_code` on `LaunchResult`.

**Tech Stack:** C# / .NET 10, WPF, xUnit 2.9.3, gRPC plugin contract (proto3), `Microsoft.Extensions.TimeProvider.Testing`.

**Spec:** `docs/superpowers/specs/2026-09-27-join-via-friend-everywhere-design.md` (approved 2026-09-29, a686278)

## Global Constraints

- Branch from `origin/main` into `feat/join-via-friend-everywhere`. Never build from the `docs/localization-wave-2` checkout.
- Build: `dotnet build ROROROblox.slnx -c Release`. Always name the `.slnx`; a stray `.sln` gives MSB1011. Build Release, because a running dev build locks `bin\Debug`.
- Test: `dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~<Name>"` per task. Run the full `dotnet test ROROROblox.slnx -c Release --no-build` at the end of each task.
- Nothing clicks, types into or otherwise touches the verification page. It is closed, never answered.
- The main is never rejoined automatically.
- No silent direct join for a flagged account, on any path, including auto-rejoin.
- Auto-rejoin rule: the client is running and not `InGame` for **3 minutes**. The grace before the first `InGame` since launch is **5 minutes**. At most **3 rejoins per account per hour**; the fourth pauses auto-rejoin and alerts.
- Contract stays `"1.0"`; changes are additive only. The host ships first.
- Off the UI thread, read `AccountsSnapshot`, never `Accounts`. Marshal through `IUiDispatcher`.
- A test that builds a `MainViewModel` uses `MainViewModelTests.Build(...)`, which disposes the decorator and calls `StopPeriodicRefresh()`.
- New UI strings go in `src/ROROROblox.App/Properties/Strings.resx` only (English neutral catalog); the translation wave adds the satellites. No emoji in UI copy. Voice: second person, sentence case, periods on microcopy.
- Outbound names use `RenderName`, never `DisplayName`.
- Never log a `LaunchTarget` record with `{Target}`: `PrivateServer` carries a joinable code. Log `target.GetType().Name`.
- A new modal must be linked in `ROROROblox.Tests.csproj` (the `None Include=... Link=Modals\...` pattern) and listed in `ModalDefaultButtonSafetyTests`. A new window also goes in `WindowChromeFenceTests`.
- Conventional commits.

## Review Focus

1. **The main's own launch.** The main is flagged by mistake, or it's the only account. It must launch directly; the rule never makes the main follow itself. Test in Task 1.
2. **Two flagged alts and no joinable main in one batch.** Squad Launch and Launch multiple must ask **once** for the batch, not once per row. Test in Task 4.
3. **The client was closed by hand while presence still said `InGame`.** Auto-rejoin must not relaunch it: once `IsRunning` is false, it's not a candidate. Test in Task 7.
4. **A plugin stops an opted-in alt** (Ur MCP `stop_accounts`). Auto-rejoin must not relaunch it during the stop's grace window. Test in Task 9.
5. **The main isn't joinable when auto-rejoin fires for a flagged alt.** Nothing is stopped, nothing is counted against the 3-per-hour budget, and it retries on the next tick. Test in Task 8.

---

## Part A: join via friend on every path

### Task 1: `FlaggedLaunchRule`, the pure decision

**Files:**
- Create: `src/ROROROblox.Core/FlaggedLaunchRule.cs`
- Test: `src/ROROROblox.Tests/FlaggedLaunchRuleTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public enum FlaggedLaunchOutcome { Direct, Follow, MainNotJoinable, MainNotInThatServer }
  public sealed record FlaggedLaunchDecision(FlaggedLaunchOutcome Outcome, LaunchTarget Target);
  public static class FlaggedLaunchRule
  {
      public static FlaggedLaunchDecision Decide(
          bool joinViaFriend, bool isMain, LaunchTarget resolved,
          long? mainUserId, UserPresence? mainPresence);
  }
  ```
  `Target` is the target to launch: `resolved` for `Direct`, `FollowFriend(mainUserId)` for `Follow`, and `resolved` (unused) for the two refusals.

Rules, in order:
1. Not flagged, or `isMain`, → `Direct`.
2. `resolved` is already a `FollowFriend` → `Direct`. The caller already chose a follow; the Friends window and alt-follow run their own `EvaluateFollow`.
3. `mainUserId` is null, or `mainPresence` isn't joinable (`InGame` with `PlaceId > 0`, the same test as `MainViewModel.EvaluateFollow`) → `MainNotJoinable`.
4. `resolved` is `GameJob(p, j)`: `Follow` only when the main's `PlaceId == p` and `GameJobId == j`, otherwise `MainNotInThatServer`.
5. `resolved` is `PrivateServer(p, …)`: `Follow` only when the main's `PlaceId == p`, otherwise `MainNotInThatServer`. A private link carries a code, not a job id, so the place is the only match possible.
6. Anything else (`DefaultGame`, `Home`, `Place`) → `Follow`.

- [ ] **Step 1: Write the failing tests**

```csharp
using ROROROblox.Core;

namespace ROROROblox.Tests;

public class FlaggedLaunchRuleTests
{
    private const long MainId = 111;
    private static UserPresence InGame(long place, string? job = "job-1") =>
        new(MainId, UserPresenceType.InGame, place, job, null);

    [Fact]
    public void Unflagged_IsDirect()
    {
        var d = FlaggedLaunchRule.Decide(false, false, new LaunchTarget.Place(5), MainId, InGame(5));
        Assert.Equal(FlaggedLaunchOutcome.Direct, d.Outcome);
        Assert.IsType<LaunchTarget.Place>(d.Target);
    }

    [Fact]
    public void FlaggedMain_IsDirect_NeverFollowsItself()
    {
        var d = FlaggedLaunchRule.Decide(true, true, new LaunchTarget.DefaultGame(), MainId, InGame(5));
        Assert.Equal(FlaggedLaunchOutcome.Direct, d.Outcome);
    }

    [Fact]
    public void FlaggedWithExplicitFollow_IsDirect()
    {
        var follow = new LaunchTarget.FollowFriend(999);
        var d = FlaggedLaunchRule.Decide(true, false, follow, MainId, presence: null);
        Assert.Equal(FlaggedLaunchOutcome.Direct, d.Outcome);
        Assert.Same(follow, d.Target);
    }

    [Theory]
    [InlineData(UserPresenceType.Offline)]
    [InlineData(UserPresenceType.OnlineWebsite)]
    [InlineData(UserPresenceType.InStudio)]
    public void FlaggedWithMainNotInGame_IsMainNotJoinable(UserPresenceType type)
    {
        var p = new UserPresence(MainId, type, null, null, null);
        var d = FlaggedLaunchRule.Decide(true, false, new LaunchTarget.DefaultGame(), MainId, p);
        Assert.Equal(FlaggedLaunchOutcome.MainNotJoinable, d.Outcome);
    }

    [Fact]
    public void FlaggedWithMainInGameButPlaceHidden_IsMainNotJoinable()
    {
        var p = new UserPresence(MainId, UserPresenceType.InGame, null, null, null);
        var d = FlaggedLaunchRule.Decide(true, false, new LaunchTarget.DefaultGame(), MainId, p);
        Assert.Equal(FlaggedLaunchOutcome.MainNotJoinable, d.Outcome);
    }

    [Fact]
    public void FlaggedWithNoMain_IsMainNotJoinable()
    {
        var d = FlaggedLaunchRule.Decide(true, false, new LaunchTarget.DefaultGame(), null, null);
        Assert.Equal(FlaggedLaunchOutcome.MainNotJoinable, d.Outcome);
    }

    [Fact]
    public void FlaggedDefaultGame_FollowsTheMain()
    {
        var d = FlaggedLaunchRule.Decide(true, false, new LaunchTarget.DefaultGame(), MainId, InGame(5));
        Assert.Equal(FlaggedLaunchOutcome.Follow, d.Outcome);
        Assert.Equal(new LaunchTarget.FollowFriend(MainId), d.Target);
    }

    [Fact]
    public void FlaggedGameJob_FollowsOnlyWhenTheMainIsInThatServer()
    {
        var same = FlaggedLaunchRule.Decide(true, false, new LaunchTarget.GameJob(5, "job-1"), MainId, InGame(5, "job-1"));
        var other = FlaggedLaunchRule.Decide(true, false, new LaunchTarget.GameJob(5, "job-2"), MainId, InGame(5, "job-1"));
        Assert.Equal(FlaggedLaunchOutcome.Follow, same.Outcome);
        Assert.Equal(FlaggedLaunchOutcome.MainNotInThatServer, other.Outcome);
    }

    [Fact]
    public void FlaggedPrivateServer_FollowsOnlyWhenTheMainIsInThatPlace()
    {
        var ps = new LaunchTarget.PrivateServer(5, "code", PrivateServerCodeKind.LinkCode);
        Assert.Equal(FlaggedLaunchOutcome.Follow,
            FlaggedLaunchRule.Decide(true, false, ps, MainId, InGame(5)).Outcome);
        Assert.Equal(FlaggedLaunchOutcome.MainNotInThatServer,
            FlaggedLaunchRule.Decide(true, false, ps, MainId, InGame(6)).Outcome);
    }
}
```

- [ ] **Step 2: Run to verify they fail.** Build, then run `--filter "FullyQualifiedName~FlaggedLaunchRuleTests"`. Expected: the build fails with `FlaggedLaunchRule` not found.

- [ ] **Step 3: Implement**

```csharp
namespace ROROROblox.Core;

/// <summary>What a launch of a <c>JoinViaFriend</c> account does. Spec rule 1: a flagged account
/// never joins directly on its own; it follows the main when the main is joinable.</summary>
public enum FlaggedLaunchOutcome { Direct, Follow, MainNotJoinable, MainNotInThatServer }

public sealed record FlaggedLaunchDecision(FlaggedLaunchOutcome Outcome, LaunchTarget Target);

public static class FlaggedLaunchRule
{
    public static FlaggedLaunchDecision Decide(
        bool joinViaFriend, bool isMain, LaunchTarget resolved,
        long? mainUserId, UserPresence? mainPresence)
    {
        if (!joinViaFriend || isMain || resolved is LaunchTarget.FollowFriend)
            return new(FlaggedLaunchOutcome.Direct, resolved);

        // Same joinable test as MainViewModel.EvaluateFollow: InGame AND a visible place.
        if (mainUserId is not { } mainId || mainPresence is not { PresenceType: UserPresenceType.InGame, PlaceId: > 0 } p)
            return new(FlaggedLaunchOutcome.MainNotJoinable, resolved);

        var follow = new LaunchTarget.FollowFriend(mainId);
        return resolved switch
        {
            LaunchTarget.GameJob gj when gj.PlaceId == p.PlaceId && gj.JobId == p.GameJobId
                => new(FlaggedLaunchOutcome.Follow, follow),
            LaunchTarget.GameJob => new(FlaggedLaunchOutcome.MainNotInThatServer, resolved),
            LaunchTarget.PrivateServer ps when ps.PlaceId == p.PlaceId
                => new(FlaggedLaunchOutcome.Follow, follow),
            LaunchTarget.PrivateServer => new(FlaggedLaunchOutcome.MainNotInThatServer, resolved),
            _ => new(FlaggedLaunchOutcome.Follow, follow),
        };
    }
}
```

- [ ] **Step 4: Run to verify they pass.** Same filter. Expected: all pass.
- [ ] **Step 5: Commit.** `git add src/ROROROblox.Core/FlaggedLaunchRule.cs src/ROROROblox.Tests/FlaggedLaunchRuleTests.cs` then `git commit -m "feat(launch): flagged-launch rule decides follow, direct or refuse"`

---

### Task 2: apply the rule in `LaunchAccountAsync`, with the ask

**Files:**
- Modify: `src/ROROROblox.App/ViewModels/MainViewModel.cs`: `LaunchAccountAsync` (about line 1673; the target line is about 1745), the seam properties (about line 719-760), and the seam defaults in the constructor (about line 211).
- Create: `src/ROROROblox.App/Modals/FlaggedLaunchWindow.xaml` and `.xaml.cs` (copy `StopAllConfirmWindow`'s shape and its owner rule).
- Create: `src/ROROROblox.App/ViewModels/FlaggedLaunchAsk.cs`
- Modify: `src/ROROROblox.App/Properties/Strings.resx`, `src/ROROROblox.Tests/ROROROblox.Tests.csproj` (the link), `src/ROROROblox.Tests/ModalDefaultButtonSafetyTests.cs` (default is Cancel), and `WindowChromeFenceTests` if it lists modals.
- Test: `src/ROROROblox.Tests/FlaggedLaunchTests.cs`

**Interfaces:**
- Consumes: `FlaggedLaunchRule.Decide`, `FlaggedLaunchOutcome` (Task 1).
- Produces:
  ```csharp
  // FlaggedLaunchAsk.cs
  internal sealed record FlaggedLaunchAsk(IReadOnlyList<string> AccountNames, string? MainName,
      IReadOnlyList<(string Name, long UserId)> JoinableOthers);
  internal abstract record FlaggedLaunchChoice
  {
      internal sealed record FollowAccount(long UserId) : FlaggedLaunchChoice;
      internal sealed record JoinDirectly : FlaggedLaunchChoice;
      internal sealed record Cancel : FlaggedLaunchChoice;
  }
  // MainViewModel
  internal Func<FlaggedLaunchAsk, FlaggedLaunchChoice> FlaggedLaunchPrompt { get; set; }
  internal FlaggedLaunchDecision DecideFlaggedLaunch(AccountSummary summary, LaunchTarget resolved); // thread-safe (AccountsSnapshot)
  internal IReadOnlyList<(string Name, long UserId)> JoinableFollowTargets(Guid excludingAccountId);
  internal enum FlaggedLaunchMode { Ask, Refuse }
  private Task<int> LaunchAccountAsync(AccountSummary? summary, LaunchTarget? overrideTarget = null,
      FlaggedLaunchMode flaggedMode = FlaggedLaunchMode.Ask);
  ```

Behaviour:
- `DecideFlaggedLaunch` reads the main from `AccountsSnapshot.FirstOrDefault(a => a.IsMain)` and projects its presence the way `FollowAltAsync` does: `new UserPresence(main.RobloxUserId ?? 0, main.PresenceState, main.CurrentPlaceId, main.CurrentServer?.JobId, null)`.
- `JoinableFollowTargets` lists saved rows (not the excluded one, not flagged-and-not-in-game) whose projected presence passes `EvaluateFollow`. It returns `RenderName` and `RobloxUserId`.
- In `LaunchAccountAsync`, directly after `LaunchTarget target = ResolveLaunchTarget(...)`:
  ```csharp
  var decision = DecideFlaggedLaunch(summary, target);
  switch (decision.Outcome)
  {
      case FlaggedLaunchOutcome.Direct: break;
      case FlaggedLaunchOutcome.Follow: target = decision.Target; break;
      default:
          if (flaggedMode == FlaggedLaunchMode.Refuse) { summary.StatusText = string.Empty; return 0; }
          var choice = FlaggedLaunchPrompt(new FlaggedLaunchAsk(
              [summary.RenderName], MainAccount?.RenderName, JoinableFollowTargets(summary.Id)));
          switch (choice)
          {
              case FlaggedLaunchChoice.FollowAccount f: target = new LaunchTarget.FollowFriend(f.UserId); break;
              case FlaggedLaunchChoice.JoinDirectly:
                  await ToggleJoinViaFriendAsync(summary); // clears the flag, persists, reverts on failure
                  if (summary.JoinViaFriend) return 0;     // the save failed; don't join directly
                  break;
              default: summary.StatusText = string.Empty; return 0;
          }
          break;
  }
  _log.LogInformation("Flagged-launch decision for {AccountId}: {Outcome}", summary.Id, decision.Outcome);
  ```
  `FlaggedLaunchPrompt` runs on the UI thread. Every UI caller of `LaunchAccountAsync` is already there.
- The window (`FlaggedLaunchWindow`):
  - Title: "Join through your main".
  - Body: `Shell_FlaggedLaunch_Body`, "{0} joins through your main, and your main isn't in a game yet." For a batch, `Shell_FlaggedLaunch_BodyMany`, "{0} join through your main, and your main isn't in a game yet." {0} is the names joined with ", ".
  - A `ComboBox` of `JoinableOthers`, with a "Follow" button that is disabled when the list is empty.
  - An "It's fixed, join directly" button, and a Cancel button that is the default (`IsDefault`/`IsCancel`).
  - Static `FlaggedLaunchChoice Ask(FlaggedLaunchAsk ask)` returns `Cancel` when closed.

- [ ] **Step 1: Write the failing tests** (in `FlaggedLaunchTests.cs`, using `MainViewModelTests.Build` and `MainViewModelTests.RecordingSuccessLauncher`)

```csharp
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

public class FlaggedLaunchTests
{
    internal static async Task<(AccountSummary Main, AccountSummary Alt)> SeedAsync(MainViewModel vm, IAccountStore store, bool mainInGame)
    {
        var m = new AccountSummary(await store.AddAsync("Main", "", "c1")) { IsMain = true, RobloxUserId = 111 };
        var a = new AccountSummary(await store.AddAsync("Alt", "", "c2")) { JoinViaFriend = true, RobloxUserId = 222 };
        vm.Accounts.Add(m); vm.Accounts.Add(a);
        if (mainInGame)
            vm.ApplyPresence(new AccountPresenceEventArgs(m.Id, UserPresenceType.InGame, 5, "Game",
                DateTimeOffset.UtcNow, new ServerInstance(5, "job-1")));
        return (m, a);
    }

    [Fact]
    public async Task FlaggedAlt_SingleLaunch_FollowsTheMain()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var (_, alt) = await SeedAsync(vm, store, mainInGame: true);
            vm.FlaggedLaunchPrompt = _ => throw new InvalidOperationException("must not ask");
            vm.LaunchAccountCommand.Execute(alt);
            await Task.Delay(50);
            Assert.Equal(new LaunchTarget.FollowFriend(111), Assert.Single(launcher.Launches));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task FlaggedAlt_MainNotInGame_AsksAndCancelLaunchesNothing()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var (_, alt) = await SeedAsync(vm, store, mainInGame: false);
            FlaggedLaunchAsk? asked = null;
            vm.FlaggedLaunchPrompt = ask => { asked = ask; return new FlaggedLaunchChoice.Cancel(); };
            vm.LaunchAccountCommand.Execute(alt);
            await Task.Delay(50);
            Assert.Equal(["Alt"], asked!.AccountNames);
            Assert.Empty(launcher.Launches);
            Assert.False(alt.IsLaunching);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task FlaggedAlt_ItsFixed_ClearsTheFlagAndJoinsDirectly()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var (_, alt) = await SeedAsync(vm, store, mainInGame: false);
            vm.FlaggedLaunchPrompt = _ => new FlaggedLaunchChoice.JoinDirectly();
            vm.LaunchAccountCommand.Execute(alt);
            await Task.Delay(50);
            Assert.False(alt.JoinViaFriend);
            Assert.False((await store.ListAsync()).Single(a => a.Id == alt.Id).JoinViaFriend);
            Assert.IsType<LaunchTarget.DefaultGame>(Assert.Single(launcher.Launches));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task FlaggedAlt_FollowAnotherAccount_FollowsThePick()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var (_, alt) = await SeedAsync(vm, store, mainInGame: false);
            vm.FlaggedLaunchPrompt = _ => new FlaggedLaunchChoice.FollowAccount(333);
            vm.LaunchAccountCommand.Execute(alt);
            await Task.Delay(50);
            Assert.Equal(new LaunchTarget.FollowFriend(333), Assert.Single(launcher.Launches));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task FlaggedAlt_JoinByLinkToAServerTheMainIsNotIn_Asks()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var (_, alt) = await SeedAsync(vm, store, mainInGame: true);
            var asked = false;
            vm.FlaggedLaunchPrompt = _ => { asked = true; return new FlaggedLaunchChoice.Cancel(); };
            await vm.LaunchAccountForPluginAsync(alt, new LaunchTarget.GameJob(5, "job-OTHER"));
            // LaunchAccountForPluginAsync passes Ask mode by default in this task; Task 3 switches it to Refuse.
            Assert.True(asked);
            Assert.Empty(launcher.Launches);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task UnflaggedAlt_IsUnchanged()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var (_, alt) = await SeedAsync(vm, store, mainInGame: true);
            alt.JoinViaFriend = false;
            vm.LaunchAccountCommand.Execute(alt);
            await Task.Delay(50);
            Assert.IsType<LaunchTarget.DefaultGame>(Assert.Single(launcher.Launches));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
```

`RecordingSuccessLauncher` is already `internal`. If `Build` returns `IAccountStore` without `ListAsync` on it, use the store's existing list method. Check `IAccountStore` and adjust that one line. The last test's comment is corrected in Task 3, which changes `LaunchAccountForPluginAsync` to refuse.

- [ ] **Step 2: Run to verify they fail.** `--filter "FullyQualifiedName~FlaggedLaunchTests"`. Expected: build errors (`FlaggedLaunchPrompt` missing).
- [ ] **Step 3: Implement** the records, the seam (default `Modals.FlaggedLaunchWindow.Ask`), `DecideFlaggedLaunch`, `JoinableFollowTargets`, the `LaunchAccountAsync` block above, the window, and the strings:
  - `FlaggedLaunchWindow_Title`: "Join through your main"
  - `Shell_FlaggedLaunch_Body`: "{0} joins through your main, and your main isn't in a game yet."
  - `Shell_FlaggedLaunch_BodyMany`: "{0} join through your main, and your main isn't in a game yet."
  - `FlaggedLaunchWindow_FollowAnother`: "Follow another account"
  - `FlaggedLaunchWindow_Follow`: "Follow"
  - `FlaggedLaunchWindow_NobodyJoinable`: "None of your accounts is in a game you can join right now."
  - `FlaggedLaunchWindow_ItsFixed`: "It's fixed, join directly"
  - `FlaggedLaunchWindow_Cancel`: "Cancel"

  Register the window: add the csproj `None Include` link, add `{ "FlaggedLaunchWindow.xaml", "It's fixed, join directly" }` to the destructive map in `ModalDefaultButtonSafetyTests` plus its "defaults to Cancel" fact, and add it to `WindowChromeFenceTests` if that fence enumerates windows.
- [ ] **Step 4: Run to verify they pass**, then the full suite. Expected: all green, and every fence green.
- [ ] **Step 5: Commit.** `feat(launch): flagged accounts follow the main on every launch, and ask when it can't`

---

### Task 3: plugin launches refuse with a reason code

**Files:**
- Modify: `src/ROROROblox.PluginContract/Protos/plugin_contract.proto:246` (`LaunchResult`); `src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj:9` (`0.10.0` → `0.11.0`)
- Modify: `src/ROROROblox.App/Plugins/IPluginLaunchInvoker.cs`, `src/ROROROblox.App/Plugins/PluginHostService.cs:565-587`, `src/ROROROblox.App/Plugins/Adapters/MainViewModelLaunchInvokerAdapter.cs`, `src/ROROROblox.App/ViewModels/MainViewModel.cs` (`LaunchAccountForPluginAsync`)
- Modify, fakes: `src/ROROROblox.Tests/PluginHostServiceTests.cs:662`, `src/ROROROblox.Tests/Metrics/ReportMetricHandlerTests.cs:96`, `src/ROROROblox.PluginTestHarness/EndToEndContractTests.cs:1855`
- Test: `src/ROROROblox.Tests/MainViewModelLaunchInvokerAdapterTests.cs`, `src/ROROROblox.Tests/PluginHostServiceTests.cs`

**Interfaces:**
- Consumes: `MainViewModel.DecideFlaggedLaunch`, `FlaggedLaunchOutcome`, `MainViewModel.EvaluateFollow` (Tasks 1-2).
- Produces:
  - proto: `message LaunchResult { bool ok = 1; string failure_reason = 2; int32 process_id = 3; string reason_code = 4; }`
  - `public static class PluginLaunchReasonCodes { public const string FollowTargetNotJoinable = "follow-target-not-joinable"; }` in `src/ROROROblox.App/Plugins/PluginLaunchReasonCodes.cs`
  - The `IPluginLaunchInvoker` methods return `(bool ok, string? failureReason, int processId, string? reasonCode)`.
  - `internal Task LaunchAccountForPluginAsync(AccountSummary summary, LaunchTarget target)` now passes `FlaggedLaunchMode.Refuse`.

Adapter behaviour:
- `RequestLaunchAsync`: after the existing guards, `var d = _vm.DecideFlaggedLaunch(summary, MainViewModel.ResolveLaunchTarget(summary.SelectedGame, null));`
  - `MainNotJoinable`/`MainNotInThatServer` → `(false, "This account joins through your main, and your main isn't in a joinable game.", 0, FollowTargetNotJoinable)`.
  - `Follow` → dispatch `_vm.LaunchAccountForPluginAsync(summary, d.Target)`, not the command, so no prompt can open.
  - `Direct` → the existing command path.
- `RequestLaunchTargetAsync` with `followUserId`:
  - Find the saved row with that `RobloxUserId` in `AccountsSnapshot`.
  - If one exists, run `MainViewModel.EvaluateFollow` on its projected presence. When that's blocked, refuse with `FollowTargetNotJoinable` and the text "That account isn't in a joinable game."
  - A user id that isn't a saved account passes through unchanged, because its presence can't be read here.
- `RequestLaunchTargetAsync` with `shareUrl`: after resolving, run `DecideFlaggedLaunch(summary, target)`.
  - Refusal outcomes → `(false, …, FollowTargetNotJoinable)`.
  - `Follow` → launch `d.Target`.
  - `Direct` → launch as before.
- `PluginHostService` copies `reasonCode ?? string.Empty` into `ReasonCode`.

- [ ] **Step 1: Write the failing tests** in `MainViewModelLaunchInvokerAdapterTests.cs`, following its existing construction (read the file's `Build`/setup first). Seed with `FlaggedLaunchTests.SeedAsync`.

```csharp
[Fact]
public async Task RequestLaunch_FlaggedAltWithMainOffline_RefusesWithReasonCode()
{
    var launcher = new MainViewModelTests.RecordingSuccessLauncher();
    var (vm, store, _, path) = MainViewModelTests.Build(launcher);
    try
    {
        var (_, alt) = await FlaggedLaunchTests.SeedAsync(vm, store, mainInGame: false);
        vm.FlaggedLaunchPrompt = _ => throw new InvalidOperationException("plugins never ask");
        var adapter = new MainViewModelLaunchInvokerAdapter(vm);
        var (ok, _, _, code) = await adapter.RequestLaunchAsync(alt.Id.ToString());
        Assert.False(ok);
        Assert.Equal(PluginLaunchReasonCodes.FollowTargetNotJoinable, code);
        Assert.Empty(launcher.Launches);
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}

[Fact]
public async Task RequestLaunch_FlaggedAltWithMainInGame_FollowsTheMain()
{
    var launcher = new MainViewModelTests.RecordingSuccessLauncher();
    var (vm, store, _, path) = MainViewModelTests.Build(launcher);
    try
    {
        var (_, alt) = await FlaggedLaunchTests.SeedAsync(vm, store, mainInGame: true);
        var (ok, _, _, _) = await new MainViewModelLaunchInvokerAdapter(vm).RequestLaunchAsync(alt.Id.ToString());
        await Task.Delay(50);
        Assert.True(ok);
        Assert.Equal(new LaunchTarget.FollowFriend(111), Assert.Single(launcher.Launches));
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}

[Fact]
public async Task RequestLaunchTarget_FollowOfASavedAccountNotInGame_Refuses()
{
    var launcher = new MainViewModelTests.RecordingSuccessLauncher();
    var (vm, store, _, path) = MainViewModelTests.Build(launcher);
    try
    {
        var (_, alt) = await FlaggedLaunchTests.SeedAsync(vm, store, mainInGame: false);
        alt.JoinViaFriend = false;
        var (ok, _, _, code) = await new MainViewModelLaunchInvokerAdapter(vm)
            .RequestLaunchTargetAsync(alt.Id.ToString(), null, followUserId: 111);
        Assert.False(ok);
        Assert.Equal(PluginLaunchReasonCodes.FollowTargetNotJoinable, code);
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}
```

In `PluginHostServiceTests`, add a fake invoker that returns `(false, "x", 0, "follow-target-not-joinable")`. Assert that `RequestLaunch`'s `LaunchResult.ReasonCode` equals `"follow-target-not-joinable"`.

In `FlaggedLaunchTests.FlaggedAlt_JoinByLinkToAServerTheMainIsNotIn_Asks`, change the call to go through the UI path instead: `vm.JoinByLinkPicker = _ => (new LaunchTarget.GameJob(5, "job-OTHER"), false); await vm.OpenJoinByLinkAsync(alt);`. Keep the assertion that it asked. Add a sibling test asserting that `LaunchAccountForPluginAsync` with the same target launches nothing **and does not ask**.

- [ ] **Step 2: Run to verify they fail.** Expected: compile errors on the 4-tuple.
- [ ] **Step 3: Implement.** Proto field, package version, reason-code constants, the interface change, the adapter logic above, the host mapping, the `LaunchAccountForPluginAsync` Refuse mode, and the three fakes' return tuples.
- [ ] **Step 4: Run** the filtered tests, then the full solution including `src/ROROROblox.PluginTestHarness/`. Expected: green.
- [ ] **Step 5: Commit.** `feat(contract): launch refusals carry reason_code; flagged plugin launches follow or refuse`

---

### Task 4: batches treat flagged accounts as followers and ask once

**Files:**
- Modify: `src/ROROROblox.App/ViewModels/MainViewModel.cs`: `SquadLaunchAsync` (about 2402; the 90 s fallback is at 2485-2521), `LaunchAllAsync` (about 2018), and `ReleaseBatchAsync` (about 2330, which gains a `FlaggedLaunchMode` parameter).
- Test: `src/ROROROblox.Tests/FlaggedBatchLaunchTests.cs`

**Interfaces:**
- Consumes: `FlaggedLaunchPrompt`, `FlaggedLaunchAsk`, `FlaggedLaunchChoice`, `FlaggedLaunchMode`, `JoinableFollowTargets` (Task 2); `SquadLaunchPlan.Build`, `AnchorGate.PickAnchor` (existing).
- Produces: `internal TimeSpan AnchorWait { get; set; } = AnchorGate.MaxWait;` on the view model. The anchor loop in `SquadLaunchAsync` reads it instead of the static, so tests can shorten it.

Behaviour:
- **Squad Launch:** the 90 s fallback no longer joins the flagged accounts directly. When no anchor landed, it builds one `FlaggedLaunchAsk(plan.Flagged.Select(r => r.RenderName).ToList(), MainAccount?.RenderName, JoinableFollowTargets(Guid.Empty))`.
  - `FollowAccount(uid)` → `ReleaseBatchAsync(plan.Flagged, new LaunchTarget.FollowFriend(uid), FlaggedLaunchMode.Refuse)`.
  - `JoinDirectly` → clear the flag on each flagged row via `ToggleJoinViaFriendAsync`, then take the old direct path.
  - `Cancel` → leave them stopped and set `StatusBanner` to `Shell_Msg_FlaggedLeftStopped` ("{0} left stopped: nobody they can follow is in a game.").
- **Launch multiple:** split the eligible rows with `SquadLaunchPlan.Build`.
  - Dispatch `Direct` first, exactly as today.
  - If any `Flagged` rows exist, wait for a joinable anchor with the same `AnchorGate.PickAnchor` loop and `AnchorWait` that Squad Launch uses.
  - Then release the flagged rows following the anchor, or ask once as above.
  - Extract the shared "wait for anchor, then follow or ask once" block into `private async Task ReleaseFlaggedAfterAnchorAsync(IReadOnlyList<AccountSummary> direct, IReadOnlyList<AccountSummary> flagged, CancellationToken ct)`. Both batches call it.
- Inside a batch, `ReleaseBatchAsync` passes `FlaggedLaunchMode.Refuse` so no per-row prompt opens.

- [ ] **Step 1: Write the failing tests**

```csharp
using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.Tests;

public class FlaggedBatchLaunchTests
{
    [Fact]
    public async Task SquadLaunch_NoAnchor_AsksOnceForAllFlaggedAccounts()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var a = new AccountSummary(await store.AddAsync("AltA", "", "c1")) { JoinViaFriend = true, RobloxUserId = 1 };
            var b = new AccountSummary(await store.AddAsync("AltB", "", "c2")) { JoinViaFriend = true, RobloxUserId = 2 };
            vm.Accounts.Add(a); vm.Accounts.Add(b);
            vm.AnchorWait = TimeSpan.FromMilliseconds(50);
            var asks = new List<FlaggedLaunchAsk>();
            vm.FlaggedLaunchPrompt = ask => { asks.Add(ask); return new FlaggedLaunchChoice.Cancel(); };

            await vm.SquadLaunchAsync(new LaunchTarget.Place(5));

            var ask = Assert.Single(asks);
            Assert.Equal(["AltA", "AltB"], ask.AccountNames);
            Assert.Empty(launcher.Launches); // no silent direct join
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task LaunchAll_FlaggedRowsFollowTheMainOnceItLands()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var (main, alt) = await FlaggedLaunchTests.SeedAsync(vm, store, mainInGame: true);
            vm.FlaggedLaunchPrompt = _ => throw new InvalidOperationException("must not ask");
            vm.AnchorWait = TimeSpan.FromMilliseconds(200);

            await vm.LaunchAllForTestAsync(); // see Step 3

            Assert.Contains(new LaunchTarget.FollowFriend(111), launcher.Launches);
            Assert.DoesNotContain(launcher.Launches, t => t is LaunchTarget.DefaultGame && launcher.Launches.IndexOf(t) > 0);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
```

`SquadLaunchAsync` must be `internal` to be called from tests. Check its current visibility; the Squad tests at `MainViewModelTests.cs:1324` show how they reach it. Mirror them.

- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement.** `AnchorWait`, `ReleaseFlaggedAfterAnchorAsync`, the Squad fallback change, the Launch-multiple split, and the banner string. If `LaunchAllAsync` shows the headroom modal or reads presence through the throwing `FakePresenceService`, expose `internal Task LaunchAllForTestAsync()`. It runs the post-modal body (`DispatchBatchAsync` plus the new flagged release) so the test skips the modal. Name it exactly that, and document it as a test seam. If the existing structure already lets a test bypass the modal (look at how the Squad tests do it), use that instead and drop the seam.
- [ ] **Step 4: Run** the filter and the full suite, including the existing `SquadLaunchPlanTests`, `AnchorGateTests` and the Squad tests in `MainViewModelTests`. An existing test that asserted the old direct fallback must be updated to the new ask. Say so in the commit body.
- [ ] **Step 5: Commit.** `feat(launch): batches hold flagged accounts for an anchor and ask once instead of joining directly`

---

## Part B: auto-rejoin

### Task 5: persist the per-account "Rejoin if it drops out" setting

**Files:**
- Modify: `src/ROROROblox.Core/Account.cs` (add `bool AutoRejoin = false` last), `src/ROROROblox.Core/IAccountStore.cs` (add `Task SetAutoRejoinAsync(Guid id, bool autoRejoin);`), `src/ROROROblox.Core/AccountStore.cs` (the `StoredAccount` record at about 743, the `ListAsync` projection at about 58, the `AddAsync` return at about 101, and the setter modelled exactly on `SetJoinViaFriendAsync` at 188 with no-op-write avoidance)
- Modify: `src/ROROROblox.App/ViewModels/AccountSummary.cs` (an `AutoRejoin` property seeded from the account, like `JoinViaFriend` at 573), `MainViewModel.cs` (a `ToggleAutoRejoinCommand` in the ctor near 286, and `internal async Task ToggleAutoRejoinAsync(AccountSummary?)` modelled on `ToggleJoinViaFriendAsync` at 3690, which refuses and does nothing for the main), `MainWindow.xaml` (a menu item after Join via friend, hidden when `IsMain` via a `DataTrigger` on `Visibility`)
- Modify, fakes: `src/ROROROblox.Tests/AccountUserIdBackfillServiceTests.cs:195`, `src/ROROROblox.Tests/PresenceServiceTests.cs:645` (implement as `Task.CompletedTask`)
- Strings: `MainWindow_RejoinIfItDropsOut` "Rejoin if it drops out", `MainWindow_RejoinIfItDropsOut_2` "Rejoin if it drops out (on)", `MainWindow_RejoinIfItDropsOutTip` "When this account's client is open but out of the game for 3 minutes, RoRoRo closes it and joins again. Never used for your main.", `Shell_Msg_CouldntSaveAutoRejoin` "Couldn't save the rejoin setting: {0}"
- Export and import: **do not** carry `AutoRejoin`. Like mutes, it's a per-machine preference.
- Test: `src/ROROROblox.Tests/AccountStoreAutoRejoinTests.cs` (copy `AccountStoreJoinViaFriendTests.cs`'s structure), plus two VM tests in `src/ROROROblox.Tests/AutoRejoinToggleTests.cs`

**Interfaces:**
- Produces: `Account.AutoRejoin`, `IAccountStore.SetAutoRejoinAsync(Guid, bool)`, `AccountSummary.AutoRejoin`, `MainViewModel.ToggleAutoRejoinAsync(AccountSummary?)`.

- [ ] **Step 1: Write the failing tests.** In `AccountStoreAutoRejoinTests`, copy each `AccountStoreJoinViaFriendTests` fact and rename `JoinViaFriend` → `AutoRejoin`: defaults false, set true then round-trips through a fresh store, and setting the same value doesn't write. In `AutoRejoinToggleTests`:

```csharp
[Fact]
public async Task Toggle_OnAnAlt_FlipsAndPersists()
{
    var (vm, store, _, path) = MainViewModelTests.Build();
    try
    {
        var alt = new AccountSummary(await store.AddAsync("Alt", "", "c"));
        vm.Accounts.Add(alt);
        await vm.ToggleAutoRejoinAsync(alt);
        Assert.True(alt.AutoRejoin);
        Assert.True((await store.ListAsync()).Single(a => a.Id == alt.Id).AutoRejoin);
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}

[Fact]
public async Task Toggle_OnTheMain_DoesNothing()
{
    var (vm, store, _, path) = MainViewModelTests.Build();
    try
    {
        var main = new AccountSummary(await store.AddAsync("Main", "", "c")) { IsMain = true };
        vm.Accounts.Add(main);
        await vm.ToggleAutoRejoinAsync(main);
        Assert.False(main.AutoRejoin);
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}
```

- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement** everything in Files.
- [ ] **Step 4: Run** the filter and the full suite (fences: loc keys, accessible naming, menu naming).
- [ ] **Step 5: Commit.** `feat(accounts): per-account "Rejoin if it drops out" setting, never for the main`

---

### Task 6: `AutoRejoinMonitor`, the pure decision

**Files:**
- Create: `src/ROROROblox.Core/Diagnostics/AutoRejoinMonitor.cs`
- Test: `src/ROROROblox.Tests/AutoRejoinMonitorTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record AutoRejoinCandidate(Guid AccountId, bool Enabled, bool IsMain, bool IsRunning,
      bool InGame, ServerInstance? CurrentServer, bool StopInProgress);
  public abstract record AutoRejoinAction(Guid AccountId)
  {
      public sealed record Rejoin(Guid AccountId, ServerInstance? LastServer, bool FailedJoin) : AutoRejoinAction(AccountId);
      public sealed record Pause(Guid AccountId) : AutoRejoinAction(AccountId);
  }
  public sealed class AutoRejoinMonitor
  {
      public static readonly TimeSpan OutOfGameLimit = TimeSpan.FromMinutes(3);
      public static readonly TimeSpan FirstJoinGrace = TimeSpan.FromMinutes(5);
      public static readonly TimeSpan BudgetWindow = TimeSpan.FromHours(1);
      public const int BudgetPerWindow = 3;
      public IReadOnlyList<AutoRejoinAction> Tick(DateTimeOffset now, IReadOnlyList<AutoRejoinCandidate> accounts);
      public void NotifyLaunched(Guid accountId, DateTimeOffset now);   // a launch of ours started
      public void NotifyRejoinSkipped(Guid accountId);                   // the VM couldn't act; refund the budget, clear in-flight
      public void Resume(Guid accountId);                                // user turned it back on
      public bool IsPaused(Guid accountId);
  }
  ```

Per-account state: `LastInGameAt`, `WatchSince` (first time seen running, or `NotifyLaunched` time), `EverInGameSinceLaunch`, `LastServer`, `RejoinTimes` (a queue), `InFlight`, `Paused`.

`Tick` rules for each candidate:
1. Not `Enabled`, `IsMain`, `Paused`, or `InFlight` → skip. Also forget `WatchSince` when not enabled, so turning it on later starts a fresh clock.
2. Not `IsRunning`, or `StopInProgress` → reset `WatchSince`/`EverInGameSinceLaunch`/`LastInGameAt` and skip. A closed client is never relaunched.
3. `InGame` → `LastInGameAt = now`, `EverInGameSinceLaunch = true`, `LastServer = CurrentServer ?? LastServer`, and skip.
4. Otherwise: `WatchSince ??= now`.
   - `due = EverInGameSinceLaunch ? now - LastInGameAt >= OutOfGameLimit : now - WatchSince >= FirstJoinGrace`.
   - Not due → skip.
5. Due: drop `RejoinTimes` older than `now - BudgetWindow`.
   - Count is `>= BudgetPerWindow` → `Paused = true` and emit `Pause`.
   - Otherwise enqueue `now`, set `InFlight = true`, and emit `Rejoin(id, LastServer, FailedJoin: !EverInGameSinceLaunch)`.

`NotifyLaunched` clears `InFlight` and sets `WatchSince = now`, `EverInGameSinceLaunch = false`. `NotifyRejoinSkipped` clears `InFlight` and removes the newest `RejoinTimes` entry. `Resume` clears `Paused` and `RejoinTimes`.

- [ ] **Step 1: Write the failing tests**

```csharp
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

public class AutoRejoinMonitorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Id = Guid.NewGuid();
    private static readonly ServerInstance Srv = new(5, "job-1");

    private static AutoRejoinCandidate C(bool inGame, bool running = true, bool enabled = true,
        bool isMain = false, bool stopping = false) =>
        new(Id, enabled, isMain, running, inGame, inGame ? Srv : null, stopping);

    private static IReadOnlyList<AutoRejoinAction> At(AutoRejoinMonitor m, double minutes, AutoRejoinCandidate c) =>
        m.Tick(T0.AddMinutes(minutes), [c]);

    [Fact]
    public void OutOfGameForThreeMinutes_WithTheClientRunning_Rejoins()
    {
        var m = new AutoRejoinMonitor();
        At(m, 0, C(inGame: true));
        Assert.Empty(At(m, 2.9, C(inGame: false)));
        var a = Assert.IsType<AutoRejoinAction.Rejoin>(Assert.Single(At(m, 3.0, C(inGame: false))));
        Assert.Equal(Srv, a.LastServer);
        Assert.False(a.FailedJoin);
    }

    [Fact]
    public void ClosedClient_IsNeverRejoined()
    {
        var m = new AutoRejoinMonitor();
        At(m, 0, C(inGame: true));
        Assert.Empty(At(m, 10, C(inGame: false, running: false)));
    }

    [Fact]
    public void ClosedByHandWhilePresenceStillSaidInGame_IsNeverRejoined()
    {
        var m = new AutoRejoinMonitor();
        At(m, 0, C(inGame: true));
        At(m, 1, C(inGame: true, running: false));
        Assert.Empty(At(m, 10, C(inGame: false, running: false)));
    }

    [Theory]
    [InlineData(false, false)] // not opted in
    [InlineData(true, true)]   // the main
    public void NotOptedInOrMain_IsNeverRejoined(bool enabled, bool isMain)
    {
        var m = new AutoRejoinMonitor();
        At(m, 0, C(inGame: true, enabled: enabled, isMain: isMain));
        Assert.Empty(At(m, 10, C(inGame: false, enabled: enabled, isMain: isMain)));
    }

    [Fact]
    public void StopInProgress_IsNotADrop()
    {
        var m = new AutoRejoinMonitor();
        At(m, 0, C(inGame: true));
        Assert.Empty(At(m, 5, C(inGame: false, stopping: true)));
    }

    [Fact]
    public void FirstJoin_GetsFiveMinutes_ThenCountsAsAFailedJoin()
    {
        var m = new AutoRejoinMonitor();
        m.NotifyLaunched(Id, T0);
        Assert.Empty(At(m, 4.9, C(inGame: false)));
        var a = Assert.IsType<AutoRejoinAction.Rejoin>(Assert.Single(At(m, 5.0, C(inGame: false))));
        Assert.True(a.FailedJoin);
    }

    [Fact]
    public void InFlight_BlocksASecondRejoinUntilTheLaunchIsNotified()
    {
        var m = new AutoRejoinMonitor();
        At(m, 0, C(inGame: true));
        Assert.Single(At(m, 3, C(inGame: false)));
        Assert.Empty(At(m, 7, C(inGame: false)));
        m.NotifyLaunched(Id, T0.AddMinutes(7));
        Assert.Empty(At(m, 11.9, C(inGame: false)));
        Assert.Single(At(m, 12, C(inGame: false)));
    }

    [Fact]
    public void FourthDropInAnHour_Pauses_AndResumeClearsIt()
    {
        var m = new AutoRejoinMonitor();
        double t = 0;
        for (var i = 0; i < 3; i++)
        {
            At(m, t, C(inGame: true));
            Assert.IsType<AutoRejoinAction.Rejoin>(Assert.Single(At(m, t + 3, C(inGame: false))));
            m.NotifyLaunched(Id, T0.AddMinutes(t + 3));
            t += 4;
        }
        At(m, t, C(inGame: true));
        Assert.IsType<AutoRejoinAction.Pause>(Assert.Single(At(m, t + 3, C(inGame: false))));
        Assert.True(m.IsPaused(Id));
        Assert.Empty(At(m, t + 10, C(inGame: false)));

        m.Resume(Id);
        Assert.False(m.IsPaused(Id));
    }

    [Fact]
    public void BudgetRefills_AfterAnHour()
    {
        var m = new AutoRejoinMonitor();
        for (var i = 0; i < 3; i++)
        {
            At(m, i * 4, C(inGame: true));
            At(m, i * 4 + 3, C(inGame: false));
            m.NotifyLaunched(Id, T0.AddMinutes(i * 4 + 3));
        }
        At(m, 70, C(inGame: true));
        Assert.IsType<AutoRejoinAction.Rejoin>(Assert.Single(At(m, 73, C(inGame: false))));
    }

    [Fact]
    public void SkippedRejoin_RefundsTheBudget()
    {
        var m = new AutoRejoinMonitor();
        At(m, 0, C(inGame: true));
        for (var i = 0; i < 5; i++)
        {
            Assert.IsType<AutoRejoinAction.Rejoin>(Assert.Single(At(m, 3 + i, C(inGame: false))));
            m.NotifyRejoinSkipped(Id);
        }
        Assert.False(m.IsPaused(Id));
    }
}
```

- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement** the class to the rules above. It's single-threaded: the view model calls it on the UI thread. Document that on the class.
- [ ] **Step 4: Run to verify they pass.**
- [ ] **Step 5: Commit.** `feat(rejoin): auto-rejoin monitor decides rejoin or pause from presence and process state`

---

### Task 7: wire the monitor into the view model

**Files:**
- Modify: `src/ROROROblox.App/ViewModels/MainViewModel.cs`: the ctor (build `_autoRejoin = new AutoRejoinMonitor()`), `PeriodicTick` handling (subscribe a private `OnAutoRejoinTick`), `LaunchAccountAsync` (the `Started` case calls `_autoRejoin.NotifyLaunched(summary.Id, DateTimeOffset.UtcNow)`), `ToggleAutoRejoinAsync` (turning it on calls `_autoRejoin.Resume(id)`), and a new `internal async Task RunAutoRejoinAsync(DateTimeOffset now)`.
- Modify: `src/ROROROblox.Core/Diagnostics/AccountRecycler.cs`: no change. Reuse `_instanceStopper.StopAccount` directly (the recycler also resets the memory baseline, which rejoin doesn't need).
- Test: `src/ROROROblox.Tests/AutoRejoinWiringTests.cs`

**Interfaces:**
- Consumes: `AutoRejoinMonitor`, `AutoRejoinCandidate`, `AutoRejoinAction` (Task 6); `AccountSummary.AutoRejoin` (Task 5); `DecideFlaggedLaunch`, `FlaggedLaunchMode.Refuse` (Tasks 2-3); `ServerInstanceTargeting.Upgrade` (existing); `ExpectClose`, `WasCloseExpected` (existing).
- Produces:
  - `internal Task RunAutoRejoinAsync(DateTimeOffset now)`, the tick body; tests call it directly because `Build` stops the ticker.
  - `internal Func<Guid, Task> WaitForClientExitAsync { get; set; }`, a seam. The default polls `summary.IsRunning` every 500 ms for up to 15 s.
  - `private readonly Dictionary<Guid, LaunchTarget> _lastRejoinTargets`: the last launch target per account, captured in the `Started` case **before** `ApplyPresence` can clear `LastLaunchTarget`.

`RunAutoRejoinAsync(now)`:
1. Build candidates from `Accounts`: `new AutoRejoinCandidate(r.Id, r.AutoRejoin, r.IsMain, r.IsRunning, r.InGame, r.CurrentServer, StopInProgress: WasCloseExpected(r.Id, now))`.
2. For each `Pause`: raise the paused alert (Task 10 adds `AlertKind.AutoRejoinPaused`; until then, call `_tray.ShowToast(title, body)` with `Shell_AutoRejoin_PausedTitle` "Auto-rejoin paused" and `Shell_AutoRejoin_PausedBody` "{0} dropped out 4 times in an hour. Auto-rejoin is paused for it."). Log it.
3. For each `Rejoin`:
   - Build the target:
     - `var baseTarget = _lastRejoinTargets.GetValueOrDefault(id) ?? ResolveLaunchTarget(row.SelectedGame, null);`
     - `var target = ServerInstanceTargeting.Upgrade(baseTarget, action.LastServer);`
   - Pre-check: `var d = DecideFlaggedLaunch(row, target);`. If the outcome is `MainNotJoinable` or `MainNotInThatServer`, and the row is flagged, retry with `d = DecideFlaggedLaunch(row, new LaunchTarget.DefaultGame())`. A flagged account only needs the main joinable; it doesn't need the old server. If it's still a refusal: log "main not joinable, retrying next tick", call `_autoRejoin.NotifyRejoinSkipped(id)`, and **stop nothing**.
   - `ExpectClose(id)`, `_instanceStopper.StopAccount(id)`, `await WaitForClientExitAsync(id)`.
   - `await LaunchAccountAsync(row, d.Outcome == FlaggedLaunchOutcome.Follow ? d.Target : target, FlaggedLaunchMode.Refuse)`.
   - If that returns 0, call `NotifyRejoinSkipped(id)`. The `Started` case already called `NotifyLaunched`.
   - Log `"Auto-rejoin {AccountId}: {Reason} -> {TargetKind}"`, with `FailedJoin ? "failed join" : "dropped out"` and `target.GetType().Name`.
4. Never call the ask prompt. Never touch the client's window.

- [ ] **Step 1: Write the failing tests**

```csharp
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

public class AutoRejoinWiringTests
{
    private static AccountPresenceEventArgs P(Guid id, bool inGame, DateTimeOffset at) =>
        inGame ? new(id, UserPresenceType.InGame, 5, "Game", at, new ServerInstance(5, "job-1"))
               : new(id, UserPresenceType.Offline, null, null, at);

    [Fact]
    public async Task OptedInAlt_OutOfGameThreeMinutes_IsStoppedAndRelaunchedIntoItsServer()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper);
        try
        {
            var alt = new AccountSummary(await store.AddAsync("Alt", "", "c")) { AutoRejoin = true, RobloxUserId = 2 };
            vm.Accounts.Add(alt);
            vm.WaitForClientExitAsync = _ => Task.CompletedTask;
            tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
            var t0 = DateTimeOffset.UtcNow;
            vm.ApplyPresence(P(alt.Id, true, t0));
            await vm.RunAutoRejoinAsync(t0);
            vm.ApplyPresence(P(alt.Id, false, t0.AddMinutes(1)));
            await vm.RunAutoRejoinAsync(t0.AddMinutes(1));
            Assert.Empty(stopper.StoppedAccountIds);

            await vm.RunAutoRejoinAsync(t0.AddMinutes(4));

            Assert.Equal(alt.Id, Assert.Single(stopper.StoppedAccountIds));
            Assert.Equal(new LaunchTarget.GameJob(5, "job-1"), Assert.Single(launcher.Launches));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ClosedClient_IsNotRelaunched()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper);
        try
        {
            var alt = new AccountSummary(await store.AddAsync("Alt", "", "c")) { AutoRejoin = true, RobloxUserId = 2 };
            vm.Accounts.Add(alt);
            tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
            var t0 = DateTimeOffset.UtcNow;
            vm.ApplyPresence(P(alt.Id, true, t0));
            await vm.RunAutoRejoinAsync(t0);
            tracker.RaiseExited(new RobloxProcessEventArgs(alt.Id, 4242));
            vm.ApplyPresence(P(alt.Id, false, t0.AddMinutes(1)));
            await vm.RunAutoRejoinAsync(t0.AddMinutes(10));
            Assert.Empty(stopper.StoppedAccountIds);
            Assert.Empty(launcher.Launches);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task NoPromptEverOpens()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher);
        try
        {
            var alt = new AccountSummary(await store.AddAsync("Alt", "", "c")) { AutoRejoin = true, JoinViaFriend = true, RobloxUserId = 2 };
            vm.Accounts.Add(alt);
            vm.FlaggedLaunchPrompt = _ => throw new InvalidOperationException("auto-rejoin never asks");
            vm.WaitForClientExitAsync = _ => Task.CompletedTask;
            tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
            var t0 = DateTimeOffset.UtcNow;
            vm.ApplyPresence(P(alt.Id, true, t0));
            await vm.RunAutoRejoinAsync(t0);
            vm.ApplyPresence(P(alt.Id, false, t0.AddMinutes(1)));
            await vm.RunAutoRejoinAsync(t0.AddMinutes(4)); // no main at all: must not throw
            Assert.Empty(launcher.Launches);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
```

`FakeRobloxInstanceStopper` is `private sealed` in `MainViewModelTests`. Change it to `internal sealed` and keep the name. That's the only change to it.

- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement** everything in Files and the tick body above. `OnAutoRejoinTick` calls `_ = RunAutoRejoinAsync(DateTimeOffset.UtcNow)` and wraps it in a try/catch that logs; a throw must never kill the ticker.
- [ ] **Step 4: Run** the filter and the full suite.
- [ ] **Step 5: Commit.** `feat(rejoin): stop and relaunch opted-in alts that drop out, on the 30 s tick`

---

### Task 8: flagged alts rejoin by following, and wait when the main isn't joinable

**Files:**
- Test: `src/ROROROblox.Tests/AutoRejoinWiringTests.cs` (add)
- Modify: `MainViewModel.RunAutoRejoinAsync` only if the tests expose a gap.

**Interfaces:**
- Consumes: everything from Task 7.

- [ ] **Step 1: Write the failing tests** (add to `AutoRejoinWiringTests`)

```csharp
[Fact]
public async Task FlaggedAlt_Rejoins_ByFollowingTheMain()
{
    var launcher = new MainViewModelTests.RecordingSuccessLauncher();
    var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
    var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper);
    try
    {
        var (main, alt) = await FlaggedLaunchTests.SeedAsync(vm, store, mainInGame: true);
        alt.AutoRejoin = true;
        vm.WaitForClientExitAsync = _ => Task.CompletedTask;
        tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
        var t0 = DateTimeOffset.UtcNow;
        vm.ApplyPresence(new AccountPresenceEventArgs(alt.Id, UserPresenceType.InGame, 5, "Game", t0, new ServerInstance(5, "job-9")));
        await vm.RunAutoRejoinAsync(t0);
        vm.ApplyPresence(new AccountPresenceEventArgs(alt.Id, UserPresenceType.Offline, null, null, t0.AddMinutes(1)));
        await vm.RunAutoRejoinAsync(t0.AddMinutes(4));

        Assert.Equal(new LaunchTarget.FollowFriend(111), Assert.Single(launcher.Launches));
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}

[Fact]
public async Task FlaggedAlt_MainNotJoinable_StopsNothing_CostsNoBudget_RetriesNextTick()
{
    var launcher = new MainViewModelTests.RecordingSuccessLauncher();
    var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
    var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper);
    try
    {
        var (main, alt) = await FlaggedLaunchTests.SeedAsync(vm, store, mainInGame: false);
        alt.AutoRejoin = true;
        vm.WaitForClientExitAsync = _ => Task.CompletedTask;
        tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
        var t0 = DateTimeOffset.UtcNow;
        vm.ApplyPresence(new AccountPresenceEventArgs(alt.Id, UserPresenceType.InGame, 5, "Game", t0, new ServerInstance(5, "job-9")));
        await vm.RunAutoRejoinAsync(t0);
        vm.ApplyPresence(new AccountPresenceEventArgs(alt.Id, UserPresenceType.Offline, null, null, t0.AddMinutes(1)));

        for (var i = 0; i < 6; i++) await vm.RunAutoRejoinAsync(t0.AddMinutes(4 + i));
        Assert.Empty(stopper.StoppedAccountIds);
        Assert.Empty(launcher.Launches);

        vm.ApplyPresence(new AccountPresenceEventArgs(main.Id, UserPresenceType.InGame, 5, "Game", t0.AddMinutes(10), new ServerInstance(5, "job-1")));
        await vm.RunAutoRejoinAsync(t0.AddMinutes(10));
        Assert.Equal(new LaunchTarget.FollowFriend(111), Assert.Single(launcher.Launches)); // six skips cost no budget
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}
```

- [ ] **Step 2: Run.** If both pass on Task 7's code, that's the expected result. Commit the tests alone. If either fails, fix `RunAutoRejoinAsync` so the flagged pre-check happens before `ExpectClose`/`StopAccount`.
- [ ] **Step 3: Run the full suite.**
- [ ] **Step 4: Commit.** `test(rejoin): flagged alts follow the main, and wait without stopping when it can't be followed`

---

### Task 9: a plugin stop is never mistaken for a drop

**Files:**
- Modify: `src/ROROROblox.App/Plugins/Adapters/ProcessTrackerAccountStopper.cs`. The class is container-resolved and keeps ONE constructor (its remarks say why), so don't add a ctor parameter. Add a settable hook, `public Action<Guid>? OnStopping { get; set; }`, invoked in `StopAccount` after the `IsTracking` check and before `_inFlight.GetOrAdd`.
- Modify: `src/ROROROblox.App/App.xaml.cs`. After both the view model and the stopper are resolved, set `stopper.OnStopping = id => <marshal to the UI thread>(() => vm.ExpectClose(id));`. Use the same `IUiDispatcher` call the app already uses for other gRPC-thread callbacks; grep `IUiDispatcher` in `App.xaml.cs` for the exact method. `_expectedCloses` is a UI-thread `Dictionary`.
- Test: `src/ROROROblox.Tests/ProcessTrackerAccountStopperTests.cs` (existing file; add the fact there, using its own tracker fake if it has one instead of `MainViewModelTests.FakeRobloxProcessTracker`), plus one wiring test.

**Interfaces:**
- Consumes: `MainViewModel.ExpectClose(Guid)` (existing, internal).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void StopAccount_ReportsTheStopBeforeIssuingIt()
{
    var id = Guid.NewGuid();
    var tracker = new MainViewModelTests.FakeRobloxProcessTracker();
    tracker.AttachedMap[id] = new TrackedProcess(4242, DateTimeOffset.UtcNow);
    Guid? reported = null;
    var stopper = new ProcessTrackerAccountStopper(tracker, delay: (_, _) => Task.CompletedTask)
    {
        OnStopping = g => reported = g,
    };
    stopper.StopAccount(id.ToString());
    Assert.Equal(id, reported);
}
```

`FakeRobloxProcessTracker.IsTracking` throws today. Make it answer `AttachedMap.ContainsKey(accountId)`, and make `RequestClose`/`Kill` return true. That's a fake-only change.

Add to `AutoRejoinWiringTests`:

```csharp
[Fact]
public async Task PluginStop_OfAnOptedInAlt_IsNotRelaunched()
{
    var launcher = new MainViewModelTests.RecordingSuccessLauncher();
    var (vm, store, tracker, path) = MainViewModelTests.Build(launcher);
    try
    {
        var alt = new AccountSummary(await store.AddAsync("Alt", "", "c")) { AutoRejoin = true, RobloxUserId = 2 };
        vm.Accounts.Add(alt);
        tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
        var t0 = DateTimeOffset.UtcNow;
        vm.ApplyPresence(new AccountPresenceEventArgs(alt.Id, UserPresenceType.InGame, 5, "Game", t0, new ServerInstance(5, "job-1")));
        await vm.RunAutoRejoinAsync(t0);
        vm.ApplyPresence(new AccountPresenceEventArgs(alt.Id, UserPresenceType.Offline, null, null, t0.AddMinutes(1)));
        await vm.RunAutoRejoinAsync(t0.AddMinutes(3.9));

        vm.ExpectClose(alt.Id); // what the plugin stopper's callback does
        await vm.RunAutoRejoinAsync(DateTimeOffset.UtcNow.AddSeconds(5)); // inside the 60 s window
        Assert.Empty(launcher.Launches);
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}
```

`ExpectClose` stamps `DateTimeOffset.UtcNow`, so the tick must use a real-clock `now` inside `ExpectedCloseWindow` (60 s). The test does that.

- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement** the callback, the App wiring and the fake changes.
- [ ] **Step 4: Run** the filter and the full suite.
- [ ] **Step 5: Commit.** `fix(plugins): a plugin stop marks the close as expected, so auto-rejoin leaves it alone`

---

### Task 10: `AlertKind.AutoRejoinPaused`

**Files** (every switch site from the survey; copy the `Recycled` entry at each one):
- `src/ROROROblox.Core/Discord/AlertTrigger.cs`: append `AutoRejoinPaused` to `AlertKind`, last, so the existing numeric values don't shift.
- `src/ROROROblox.Core/Discord/DiscordConfig.cs:80-93`: `AutoRejoinPausedDestinations`. The default is the same as `AccountDroppedOut`'s default list, because it's the same kind of news.
- `src/ROROROblox.Core/Discord/WebhookPayload.cs:66-97`: title "Auto-rejoin paused", body "{name} dropped out 4 times in an hour. Auto-rejoin is paused for it until you turn it back on."
- `src/ROROROblox.App/Discord/AlertStatusLine.cs:67-76`
- `src/ROROROblox.App/Preferences/SettingsPage.xaml` (a routing row next to Recycled at about 384-390) and `.xaml.cs` (at about 902-924, 962-990 and 1540-1544)
- `src/ROROROblox.App/Notify/NtfySender.cs:42`, `PushoverSender.cs:106`: the same priority as `AccountDroppedOut`.
- `MainViewModel.RunAutoRejoinAsync`: replace Task 7's temporary `ShowToast` with `RaiseAlerts([new AlertTrigger(AlertKind.AutoRejoinPaused, row.Id, row.RenderName, row.DisplayName, row.CurrentGameName, null, now)])`. The Local destination makes the toast.
- Tests: `src/ROROROblox.Tests/Discord/AlertRouterTests.cs`, `WebhookPayloadTests.cs`, `AlertDispatcherTests.cs`, and `SettingsReachabilityTests` if it enumerates alert kinds.

- [ ] **Step 1: Write the failing tests.**
  - In `WebhookPayloadTests`, copy the `Recycled` payload test and assert the new title and body with `RenderName`.
  - In `AlertRouterTests`, copy the `Recycled` routing test: a default config routes `AutoRejoinPaused` to the same destinations as `AccountDroppedOut`.
  - In `AutoRejoinWiringTests`, drive four drops for one alt (Task 6's loop shape, calling `RunAutoRejoinAsync` and simulating each relaunch with `tracker.RaiseAttached` and `ApplyPresence`). Subscribe to `vm.AlertsRaised` and assert that exactly one `AutoRejoinPaused` trigger was raised.
- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement** every site above. Grep for `AlertKind.Recycled` and confirm that each hit now has an `AutoRejoinPaused` sibling: `grep -rn "AlertKind.Recycled" src --include=*.cs`.
- [ ] **Step 4: Run** the filter and the full suite.
- [ ] **Step 5: Commit.** `feat(alerts): auto-rejoin paused alert, routed like dropped-out`

---

### Task 11: live check on the rig (manual, with Este)

No code. Run on Dunder-MiffLan, with the host built Release and installed the way the repo's release notes describe. Record the results in the PR body.

- [ ] **Step 1:** With the main in the private server, launch the flagged alt from the single Launch button. Expected: it follows the main.
- [ ] **Step 2:** Stop the main and launch the flagged alt again. Expected: the "Join through your main" dialog, defaulting to Cancel. Cancel, and confirm nothing launched.
- [ ] **Step 3:** Turn on "Rejoin if it drops out" for one unflagged alt and one flagged alt. Let both idle out (20 minutes, Error 278). Expected:
  - each is back in the server within about 26 s plus 3 minutes of the kick;
  - the flagged one followed the main;
  - RoRoRo's log shows `Auto-rejoin … dropped out`.
- [ ] **Step 4:** From Ur MCP, `stop_accounts` the unflagged alt. Expected: it stays stopped.
- [ ] **Step 5:** Commit nothing. Paste the timings into the PR.

---

## Self-review notes

- **Spec coverage:**
  - Rule 1 → Tasks 1-2. Rule 2 → Task 2. Rule 3 → Task 4. Rule 4 → Task 3.
  - Rule 5 (Join by link) → Task 2, through `LaunchAccountAsync`, with a test in Task 3.
  - Rule 6 (auto-rejoin) → Tasks 5-10.
  - "Never": nothing touches the page (the only client action is `StopAccount`); the main is never rejoined (Tasks 5-6); no silent direct join (Tasks 2-4, 7-8); auto-rejoin never fights a person (Tasks 6, 7, 9).
  - Contract → Task 3. Live testing → Task 11.
- **Recycle and the Discord inbound join** pass an override through `LaunchAccountAsync`, so rule 1 covers them with no extra task. For a flagged account, Recycle's `LastLaunchTarget` is already a `FollowFriend`, so it follows again.
- **Types:** `FlaggedLaunchMode`, `FlaggedLaunchDecision`, `AutoRejoinCandidate` and `AutoRejoinAction` names match across Tasks 1-10.
