# Worker brief: v1.41 "Roblox at a glance" build

Read this whole file before you start. It applies to every checklist item.

## Where you are

- Repo: the `ROROROblox` checkout under `%USERPROFILE%\Projects`, branch `cycle/v1.41-roblox-feeds`. Work in this tree on this
  branch. Do not switch branches, create worktrees, push, or open PRs.
- The plan: `docs/cycles/2026-11-26-roblox-feeds/checklist.md`. Your item is named in your prompt; build
  that item and nothing else.
- The design: `docs/cycles/2026-11-26-roblox-feeds/spec.md`. Read it in full before writing code; the
  item's "Spec ref" names the sections that matter most. Acceptance context: `prd.md` beside it.
- House rules: the repo's `CLAUDE.md`. Read its "Build, test, run" and "Hard rules" sections. They are
  load-bearing (one ctor + `ILogger<T>` on typed clients, `UseCookies=false`, `DynamicResource` only,
  `AppDataRoot`, generated culture resx, fence ceilings that move in the same commit, etc.).
- The builder is a senior engineer. Write code at that level; no tutorial comments.

## Standing gates (every item)

- Build: `dotnet build ROROROblox.slnx -c Release` (always name the `.slnx`). 0 errors; ~43 warnings are
  known noise.
- **The commit gate is the full suite:** `dotnet test ROROROblox.slnx -c Release --no-build`, 0 failed,
  1 harness skip by design. A filtered run is not the gate. Run it after your last change and before you
  commit.
- No launch path awaits a feed. No new RPC, capability, or contract package bump. No new
  `PackageReference`.
- No new call carries a cookie or an account. Nothing new runs at startup.
- Every new colour is a `DynamicResource` of an existing slot. No new theme slot.
- No text from Status.io reaches the window, a tooltip, a screen reader or the log.
- `src/ROROROblox.Core/RobloxCompatChecker.cs`, row 4's border and the drift string are not edited.
- No listing image changes.
- **Live runs:** right now a Store-installed RoRoRo and a Roblox client are running on this machine. Do
  not start a dev build of the app, and never close, kill or send a close to any RoRoRo or Roblox
  process. If your item has a live step, record in the smoke doc that it could not be made because
  those processes were running, and move on. (Check first with
  `Get-Process ROROROblox.App, RobloxPlayerBeta -ErrorAction SilentlyContinue`; if both are gone you may
  run it, with `RORORO_DATA_DIR` set to an absolute temp folder.)
- If the tree disagrees with the plan or the spec (a file is not where it says, a type has a different
  shape, a measurement contradicts it), **stop and report the disagreement**. Do not build to the plan
  over the tree.
- Mutation checks ("fails when X is changed", mutation-checked): actually make the break, run the test,
  see it red, revert, and put the red line in the commit message.

## Git

- One commit per item (an item may say "its own commit"), conventional commits, e.g.
  `feat(feeds): ...`, `test(render): ...`, `fix(update-probe): ...`.
- The pre-commit hooks run a secret scan and a local-path guard; they must pass. Never `--no-verify`.
- End every commit message with this trailer line exactly:
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`
- Do not stage `.claude/worktrees/` or anything outside your item.
- Do not check boxes in `checklist.md`; the orchestrator does that.

## Carry it through, hold the scope

Keep working until everything in the item is built and checked. Stop only when you cannot go on without
the builder, or before a step that cannot be undone. When the item is built and checked, stop and report.
Do not add features, tests, files, docs or refactors the item did not ask for. If one would help, name it
in the report instead.

## Report shape

1. Each acceptance line of your item, restated, with **met / not met / not run** and the evidence (test
   name and result, file:line, command output).
2. The Verify command(s) from the item, run, with their output summarised.
3. The full-suite result (passed / failed / skipped counts).
4. Files changed, and the commit SHA.
5. Anything left undone, deliberately left out, or any place the tree disagreed with the plan.
