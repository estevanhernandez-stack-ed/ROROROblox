# Run Brief: 247-server-targeting

## Ask
"do 247. I should be able to do the rest tomorrow"

## Source
- **Kind:** issue
- **Location:** GitHub #247; triage `docs/investigations/2026-10-25-field-report-triage.md` (H6)

## Intent
A plugin can pick one server from Roblox's public server list and send an account straight into it, through the host's existing v1.14 targeting, without the host ever falling back to matchmaking.

## Done when
- `LaunchTargetRequest` carries a place id + game instance id, and `RequestLaunchTarget` launches a `LaunchTarget.GameJob` built from exactly that pair.
- A half pair or a malformed job id is refused with a reason code, never degraded to `Place`.
- The consent sheet's launch-target line covers the new target, in all seven languages.
- `dotnet build` + full `dotnet test ROROROblox.slnx -c Release` green; `docs/decisions.md` mirrors the decisions.

## Out of scope
- Listing servers: a plugin's job (documented public endpoint), per the issue.
- Landing verification for plugin launches (Recycle has it): the plugin can read where the account landed through `GetRunningAccounts`. Named in the report, not built.
- Publishing NuGet 0.14.0, tagging, the translation verifier pass: release-time.

## Inferred
- Rides in contract 0.14.0, which #246 opened and nobody has published (from: `ROROROblox.PluginContract.csproj` at 0.14.0, merge e3c00af).
- Shape: a new `GameInstance game_instance = 4` member of the existing `target` oneof, not two loose fields; a oneof keeps "exactly one target" true on the wire and adding a member is wire-compatible (from: the proto's existing oneof).
- Built through `ServerInstance.TryFrom`, the type's only entrance, because a server-list entry is one reading carrying both halves (from: `ServerInstance.cs`, `ServerInstanceTargeting.cs`).
- No `Upgrade` on this path: the plugin launch path passes the override target through unchanged (`LaunchAccountAsync` → `ResolveLaunchTarget`), and upgrading against the launching account's own presence (offline, it must not be running) would degrade the GameJob to Place, which is silent matchmaking (from: `MainViewModel.cs` 1788-1814, `ServerInstanceTargeting.Upgrade`).
- Job ids validated as GUIDs: Roblox's server list returns GUID `id`s, and the value lands in a URL (escaped already) (from: `RobloxLauncher.cs` GameJob branch).
- Same RPC, same `host.commands.launch-target` capability: no new consent grant, but the sheet's wording ("from a link or friend") goes stale, so it is reworded (from: `Strings.resx` `Plugin_Cap_LaunchTarget`).

## Mode
- **Mode:** Attended
- **Triggered by:** attended by default (20:40 local, 5 items)
