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

## Proposed fix, with blast radius

Recommended: wording-first, no filter.

1. Make the alert say what it covers. Options: a body footer or a title tail such as "N saved accounts" (`WebhookPayload.cs:60`). Blast radius: `WebhookPayload` tests, any golden payload tests, the metric-alert section of `docs/features.md`, and localisation if the strings are ever moved to the catalogue. No contract change.
2. If Este wants running-only scoping, add a per-rule or global option rather than a hard filter. The filter belongs in `MetricReportSinkAdapter.Report`, between `:91` and `:112`, not in the coordinator: the coordinator has no running or saved set (`MetricAlertCoordinator.cs:18` constructor takes only a clock), and the sink is where `resolveNames` already reads `AccountsSnapshot`. It needs a new `Func<Guid, bool>` for "is running" (from `IRunningAccountsProvider`), tests in `MetricReportSinkAdapterTests.cs`, and care that history for a filtered account is still recorded or deliberately not (a Rate rule needs a continuous series, so dropping reports while stopped then resuming gives a gap and a delayed first rate). It also interacts with the rule "Event changed" semantics. This is more than a one-line filter and the semantics are undecided, so it was not made here.

Why no code change on this branch: the brief allows a code fix only when it is a one-line coordinator filter with an obvious test. Neither holds. The coordinator cannot see account state, and the right behaviour (alert on idle saved accounts or not) is a product call.

## Open question for Este

Should a metric alert cover only accounts running on this PC, or every saved account Ur Score can read? If "running only", is a Rate rule on a stopped account supposed to stay silent (the account is expected not to climb)? Answering this picks between the wording-only change (cheap, safe) and the sink-level running filter (new provider dependency, series-gap behaviour). The 2026-10-07 alert did not record the rule kind; if it was a Rate rule ("stopped climbing"), the not-running accounts firing is the rule working as written.

## Verification note

Evidence is from reading code at the worktree base (origin/main c4f2acb) and Ur Score HEAD b383b1a. No live reproduction was run. The 2026-10-07 alert's rule kind and the account set were not available.
