# Metric alerts naming accounts that are not running on this PC

Date: 2026-10-19. Dashboard task o2g8mchv8. Investigation only; no code changed.

## Finding

A metric alert names every saved RoRoRo account that Ur Score reports a number for, because Ur Score's allow list is the saved-accounts list (`GetAccounts`), RoRoRo applies no running-or-saved filter of its own, and the alert text never says "running". Accounts that are saved but not running on this PC are in scope by design on both sides. This is neither a clean scoping bug nor a wording bug in the host; it is a missing product decision (should a metric alert cover only accounts running here?) plus alert wording that does not say what population it covers.

## What a report carries

- Contract: `MetricReport` has `subject_id`, `metric_id`, `value`, `observed_at_unix_ms` (`src/ROROROblox.PluginContract/Protos/plugin_contract.proto:290-318`). One number for one subject. `subject_id` is a RoRoRo account id "from GetAccounts" or empty for a metric about no account (`:291-299`).
- `GetAccounts` returns saved accounts, not running ones: "GetRunningAccounts lists running only" (`plugin_contract.proto:61-62`, `:337-338`).
- So values are keyed per account (one RPC per account per stat) or globally (empty subject, Ur Score's clan-and-field numbers). Never per clan roster.
- The plugin does not report the whole clan roster. Ur Score reads the clan rows, then keeps only rows that map to a RoRoRo account (`Ur-Score/src/Core/RecipeWatch.cs:498-499`, `mine = reading.Rows.Where(r => owned.ContainsKey(r.UserId))`), then `ReportPolicy.Evaluate` drops any subject not on the allow list (`Ur-Score/src/Core/ReportPolicy.cs:96-101`).
- The allow list is every known (saved) account minus ones the user excluded, no running check: `Ur-Score/src/Core/ReportPolicies.cs:25-31`, fed by `GetAccounts` via `Ur-Score/src/Composition/AppServices.cs:831-836` and `:823`. Ur Score never calls `GetRunningAccounts` for this.
- The Ur Score copy read: `Projects/Ur-Score`, HEAD b383b1a (merge of PR #44, release v0.6.3, 2026-09-27). Read-only, not modified.

Host receiver: `PluginHostService.ReportMetric` is a pass-through (`src/ROROROblox.App/Plugins/PluginHostService.cs:129-133`) to `MetricReportSinkAdapter.Report` (`src/ROROROblox.App/Plugins/Adapters/MetricReportSinkAdapter.cs:62-126`).

## What the host does with it

- Sink: opt-in gate (`:66`), skew check (`:72-82`), rules present (`:84-85`), subject parsed with a fall-back to `Guid.Empty` (`:90`), names resolved (`:91`), then `_coordinator.Observe` (`:112-113`) and the batcher (`:116`).
- Name resolution reads the saved-accounts snapshot, not running accounts: `App.xaml.cs:2435-2454` (`MainViewModel.AccountsSnapshot.FirstOrDefault(a => a.Id == accountId)`, unmatched gives empty strings). Wired at `App.xaml.cs:1504`.
- `MetricAlertCoordinator.Observe` (`src/ROROROblox.Core/Metrics/MetricAlertCoordinator.cs:45-95`) takes whatever it is handed: it records history, matches rules by metric id (`:54`), evaluates (`:56`), and returns at most one trigger per account per call (`:82-91`). There is no filter on running state, saved state, or subject at all. It has no access to either set.
- Grouping: `MetricBreachBatcher` groups by (kind, metric id, rule) in a fixed 5 s window and lists each account once (`src/ROROROblox.Core/Metrics/MetricBreachBatcher.cs:81-92`, `Window` at `:55`). One alert, N accounts, which is the "three accounts" Este saw.
- Tests (`src/ROROROblox.Tests/Metrics/MetricAlertCoordinatorTests.cs`, `MetricAlertsGateTests.cs`, `MetricReportSinkAdapterTests.cs:168-189`) pin grouping, the gate, and the Guid.Empty fall-back. None pins any running or saved filter, so there is no intended filter being violated.

## Wording

All metric alert text is built in `src/ROROROblox.Core/Discord/WebhookPayload.cs`, not in a string catalogue:

- Title: `"{name | N accounts} — {rule label}"` plus a tail such as " stopped climbing", " went above X", " fell below X", " changed" (`:60`, `:134-155`); recovery tails at `:162-174`.
- Body line per account: `• {name} — ...` (`:102-105`).

It does not say "your accounts", "running", or anything clan-wide. "N accounts" is true of the alert but silent on which population. A reader sees RoRoRo (a launcher for running accounts) naming accounts and infers they are the running ones.

## Why the unrunning accounts appear, and whether it is wrong

A saved account that is not running still has a real clan score; points can be earned elsewhere (another PC, phone, the account played before RoRoRo closed). Ur Score reads those points from the clan page, so they report. For a Rate rule ("stopped climbing") a non-running account is exactly the case that will trip it: it is not earning on this PC. That is arguably useful (the user finds out an alt is idle) and arguably noise (the user did not expect alts they are not running to alert). The code cannot tell which Este wants.

## Classification

- Scoping bug: only if the intent is "alert about accounts running on this PC". Nothing in the contract, docs, or tests states that intent. The contract says the opposite by sourcing ids from `GetAccounts`.
- Wording bug: partly. The alert does not state its population, and the default "N accounts" invites the wrong reading.
- Not a plugin or contract defect: the plugin behaves as documented.

## Decided and built (2026-10-19)

Este's ruling: a metric alert names only accounts running on this PC at observation time; the alert still fires on the metric, and when no running account is left to name, the text says so instead of naming idle saved alts. No plugin or contract change.

What changed:

- `src/ROROROblox.App/Plugins/Adapters/MetricReportSinkAdapter.cs`: a new `Func<Guid, bool> isRunningHere` dependency, wired in `App.xaml.cs` (`IsRunningHere`, reading `MainViewModel.AccountsSnapshot` and `IsRunning`). Every report still reaches `MetricAlertCoordinator.Observe` unfiltered, so history, Rate series and the rule's verdict run on the same numbers as before and no gap or synthetic zero is introduced. A trigger for an account that is not running here is marked `NotRunningHere` after the verdict, not dropped. `Guid.Empty` (the global carrier) is never marked.
- `src/ROROROblox.Core/Discord/AlertTrigger.cs`: trailing optional `NotRunningHere` flag.
- `src/ROROROblox.Core/Discord/WebhookPayload.cs`: metric alerts name, count and list only triggers not marked; the count reads "3 accounts running here"; with none left the title carries the label alone and the body is "None of your running accounts; other players moved the clan total." Alert text in this type is English by design (the Core string boundary bans a `Message` member, not wording), so no resx or translation catalogue changed.
- Tests: `MetricReportSinkAdapterTests` (running named; not-running unnamed with its value still fed to the coordinator; all-not-running still fires with the empty-case text; mixed group; global carrier), `WebhookPayloadTests` (the two phrasings), and four existing title assertions moved to "running here".

The coordinator is untouched, as the finding recommended.

## Verification note

Evidence is from reading code at the worktree base (origin/main c4f2acb) and Ur Score HEAD b383b1a. No live reproduction was run. The 2026-10-07 alert's rule kind and the account set were not available.
