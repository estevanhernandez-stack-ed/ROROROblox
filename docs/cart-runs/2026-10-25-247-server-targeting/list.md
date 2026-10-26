# Run List: 247-server-targeting

Branch `feat/247-server-targeting`, commit per item, in order (2 depends on 1).

- [x] **1. The contract can name one server**
  Spec ref: `#247 > Change (additive)`
  What to build: In `src/ROROROblox.PluginContract/Protos/plugin_contract.proto`, add `message GameInstance { int64 place_id = 1; string game_instance_id = 2; }` and a third member of `LaunchTargetRequest`'s `target` oneof: `GameInstance game_instance = 4;`. Comment in the file's voice: 0.14.0; both halves from ONE server-list entry (the `id` from `games.roblox.com/v1/games/{placeId}/servers/Public` for that same place id); a full server queues and the host never falls back to matchmaking; older hosts refuse it as "no target". No version bump (0.14.0 is already unpublished on main). Add a recipe to `docs/plugins/AUTHOR_GUIDE.md` beside the launch-target docs: list servers yourself (public endpoint, sort ascending for fewest players), pass the pair, read `GetRunningAccounts` afterwards to see where the account landed. Size S.
  Acceptance: Done when the solution builds with the generated type and the AUTHOR_GUIDE recipe exists.
  Verify: `dotnet build ROROROblox.slnx -c Release`.
  Stop class: go

- [x] **2. The host launches exactly that server, or refuses**
  Spec ref: `#247 > routed into the existing v1.14 targeting path. Keep its rule: a full server queues, it never silently matchmakes.`
  What to build: `PluginHostService.RequestLaunchTarget` maps the new case. `IPluginLaunchInvoker.RequestLaunchTargetAsync` gains the instance (e.g. a `ServerInstance? gameInstance` parameter; update every implementation and test fake). `MainViewModelLaunchInvokerAdapter.ValidateLaunchTargetArgs` accepts it; a game instance must produce a non-null `ServerInstance.TryFrom(placeId, jobId)` AND the job id must parse as a GUID, else refuse with a new reason code `invalid-target` (add to `PluginLaunchReasonCodes`, documented). The adapter builds `new LaunchTarget.GameJob(instance.PlaceId, instance.JobId)` from that one pair and hands it to `LaunchAccountForPluginAsync` with no `ServerInstanceTargeting.Upgrade` (comment why: upgrading against the launching account's own, offline presence degrades GameJob to Place, which is matchmaking). The existing flagged-launch handling stays as is (`FlaggedLaunchRule` already treats GameJob; `MainNotInThatServer` maps to `follow-target-not-joinable`). Size M.
  Acceptance: Done when tests show: the RPC passes the pair through (`PluginHostServiceTests`); half pairs (place 0, empty job id) and a non-GUID job id are refused with `invalid-target` and never launch; a valid pair launches a `GameJob` with exactly that place and job id, which fails if an `Upgrade` against the launching account is inserted; share_url and follow paths are unchanged.
  Verify: `dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "MainViewModelLaunchInvokerAdapterTests|PluginHostServiceTests"`; break the no-degrade rule and see a test fail.
  Stop class: go

- [x] **3. The consent sheet says what the grant now covers**
  Spec ref: `Strings.resx > Plugin_Cap_LaunchTarget`
  What to build: Reword the English `Plugin_Cap_LaunchTarget` to cover a link, a friend, or a specific server the plugin picks (same voice, one sentence). Per the repo rule, update `docs/store/translations/ui-<culture>.json` for de, es, fr, pl, pt-BR, ru, then `python scripts/gen-culture-resx.py <culture>` for each; never hand-edit a culture resx. Size S.
  Acceptance: Done when all seven strings are updated, the six culture resx files are regenerated from JSON, and `CultureKeyParityFenceTests` passes.
  Verify: `dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter CultureKeyParityFenceTests` plus `git diff --stat` showing JSON + resx moved together.
  Stop class: go

- [x] **4. Whole suite green, decisions mirrored**
  Spec ref: repo CLAUDE.md > Decisions
  What to build: Append the #247 decision to `docs/decisions.md`. Run the full suite.
  Acceptance: Done when `dotnet build ROROROblox.slnx -c Release` has 0 errors and `dotnet test ROROROblox.slnx -c Release --no-build` passes in full.
  Verify: the two commands above.
  Stop class: go
