# RORORO — account groups (coalescing launch surfaces) design

---
**Date:** 2026-07-09
**Status:** Approved-shape (queue spec — build whenever) — mini-spec, ready for a plan
**Author:** The Architect + Este
**Scope:** Named account **groups** as the unit of "launch these together." Coalesce Launch-multiple (ephemeral `IsSelected` checkboxes) and Squad Launch (eligibility-computed set) into one mental model: **pick a group → pick a target → go.** "Users should be able to easily set their groups."
**Origin:** Este — "the squad launch and batches are going to have a moment to coalesce; users should be able to easily set their groups."
**Builds on:** trust-aware squad launch (#55 — `JoinViaFriend`, careful mode, three-phase dispatch all apply per-group) + the launch pipeline (`DispatchBatchAsync`/`ReleaseBatchAsync` already take "a set of accounts + a target").
---

## 1. Problem

Two "launch a set of accounts" surfaces exist with different selection models: **Launch-multiple** uses per-row `IsSelected` toggle dots (ephemeral, no memory) → default game; **Squad Launch** computes eligibility over ALL accounts → a picked private server. A clan runs the same crews repeatedly (the main squad, the farm alts, the trader accounts) and today re-selects them by hand every session. There's no saved concept of "these accounts, together."

## 2. Design decisions

1. **A Group is a named, ordered set of account ids.** Persisted plaintext (`%LOCALAPPDATA%\ROROROblox\groups.json` — not secret, same class as `favorites.json`). An account may belong to many groups. Order within a group is the launch order (respects the trust-aware phasing on top).
2. **Groups feed the existing pipeline; they don't replace it.** A group launch resolves to `{ set of member accounts (eligible ones) } + { chosen target }` and runs through the SAME `DispatchBatchAsync` path — so `JoinViaFriend` routing, careful mode, pre-warm, never-strand all apply unchanged. No new launch machinery.
3. **Launch surface — a group bar.** The main-window toolbar gains a **Group** picker (dropdown of saved groups + "All selected" + "Manage groups…"). Choosing a group:
   - Sets the batch to that group's members (visually reflects by checking their `IsSelected` dots so the current per-row model stays truthful, OR by a distinct group-highlight — decided at plan time; the cleaner path is "selecting a group sets IsSelected across rows," reusing the existing selection concept as the runtime batch).
   - `Launch multiple` / `Private server` then act on that set exactly as today.
   - "All selected" = today's manual-checkbox behavior, unchanged (groups are additive, never mandatory).
4. **Setting groups — a Manage Groups surface** ("easily set"): create/rename/delete groups; a build-mode that toggles membership per row (checkbox-per-account against the active group), or drag/multi-select. The Library-window pattern (list + rows + inline actions) is the model; theme tokens + define-before-use resource discipline apply.
5. **Per-account group membership is also reachable from the row context menu** (a "Groups ▸" submenu with a checkable item per group), so a user can tag an account into a group without opening the manager — mirrors how JoinViaFriend landed in the context menu.
6. **Groups compose with the default target, not a per-group target (v1).** A group is *who*, the target picker is *where*. (A future "this group defaults to this private server" is a natural v2 — noted, not built.)

## 3. Non-goals (v1)

- No per-group saved target (who, not where — v2).
- No auto-grouping (by tag, by presence). Tags stay the freeform filter concept; groups are explicit launch sets. (A "make a group from the current filter" convenience is a cheap future add.)
- No group-level settings (per-group FPS cap, per-group careful mode) — those stay per-account / global.
- No change to eligibility rules, the trust-aware phases, or careful mode — groups only choose the input set.
- No plugin-contract exposure of groups in v1 (additive proto later if a plugin wants group awareness).

## 4. Architecture

- **Core:** `SavedGroup(Guid Id, string Name, IReadOnlyList<Guid> MemberIds, DateTimeOffset AddedAt, string? LocalName = null)` + `IGroupStore` (`ListAsync`, `AddAsync`, `RenameAsync`, `RemoveAsync`, `SetMembersAsync`/`AddMemberAsync`/`RemoveMemberAsync`, `DefaultChanged`-style event if a picker needs live refresh) — mirror `FavoriteGameStore`/`PrivateServerStore` shape (SemaphoreSlim gate, atomic temp-write, tolerant JSON load, additive-defaulted fields per the #54/#55 lesson). Removing an account must prune it from every group (hook `AccountStore.RemoveAsync` → `IGroupStore.PruneMemberAsync(id)`).
- **App — pure planner:** `GroupBatch.Resolve(group, allSummaries) → ordered eligible member summaries` (pure, testable — reuses `LaunchEligibility`).
- **App — VM:** `Groups` observable collection; `SelectedGroup`; `ApplyGroupCommand` (sets `IsSelected` across rows to the group's members); `ManageGroupsCommand`; `ToggleGroupMembershipCommand(account, group)`. Group launch reuses `LaunchAllAsync`/`SquadLaunchAsync` — they already read the selected set.
- **App — UI:** the toolbar Group picker; a `GroupsWindow` (manage/build); the row context-menu Groups submenu.

## 5. Edge cases

- **Member account deleted** → pruned from all groups (store hook); a group can go empty (still valid, launches nothing + banner).
- **Member ineligible at launch** (expired/limited/running) → excluded by existing eligibility, same as today's selected-but-ineligible accounts.
- **Overlapping groups** → fine; selecting a group replaces the current selection set (not additive) so "launch group A" is unambiguous. A "add group to selection" modifier is a future nicety.
- **Legacy / no groups** → the Group picker shows only "All selected" + "Manage groups…"; every existing flow works untouched (groups are purely additive).
- **Rename/reorder** → survives; membership keyed by account Guid (stable), not name.

## 6. Testing

- **Unit (store):** add/rename/remove group; set/add/remove members; prune-on-account-delete removes from all groups; JSON round-trip incl. legacy-load default; no-op-write discipline.
- **Unit (pure):** `GroupBatch.Resolve` — member order preserved, ineligible excluded, empty group → empty, non-member accounts absent.
- **Manual smoke:** create a group, add accounts via manager + via row context menu; pick the group → correct rows select; Launch-multiple + Private-server both act on exactly the group (with a `JoinViaFriend` member routing via anchor, careful mode honored); delete an account → gone from the group; theming recolors the new surfaces.

## 7. What ships

`SavedGroup` + `IGroupStore` + prune hook; `GroupBatch.Resolve`; VM group state + commands; toolbar Group picker; `GroupsWindow` manager/build; row context-menu Groups submenu; tests per §6; smoke doc. Own subagent-driven cycle (~5-6 tasks). Sequence after #55 merges (reuses its pipeline + patterns).

**The coalesced mental model, stated:** *who* (group) × *where* (target) × *how* (careful / trust-aware) — three independent dials, one launch.
