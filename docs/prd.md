# RORORO — Product Requirements: the host tells plugins what colour it is

Expands [`docs/scope.md`](scope.md). Technical design lands in `/spec`; nothing here picks a message
field number, a stream capacity or a discovery mechanism.

**Anchor:** a plugin should never need to know where the host keeps its themes.

Every claim about current behaviour below was read against the tree on 2026-08-10. Given that F-091's
register row has now been corrected twice, nothing in this document is carried forward from the row.

## Problem statement

RoRoRo ships four built-in themes. A plugin can only see three of them, and the mechanism explains
why: `626labs.ur-task` reads the host's `settings.json` for an active theme id, matches it against a
hand-copied table of three palettes, and otherwise loads `themes\<id>.json` off disk. **User themes
are files, so that path works. Built-in themes are records in host code and never touch the disk, so
the only way a plugin can have one is to copy it.** Flatline shipped after that copy was written.

The person who feels it is the clan member who chose flatline, which is the accessibility theme — the
one that carries no meaning in colour at all. They now have a host window that is flat grey and a
plugin window that is brand navy. A theme picked because colour was a problem is being applied to
half the screen.

The cost compounds in a second direction. To reach around the contract at all, the plugin has to know
five internal storage details of the host: the settings filename, the camelCase key inside it, the
themes folder layout, the per-file snake_case naming policy, and the reader's tolerance for comments
and trailing commas. Any of those can change in a RoRoRo release without anyone touching the plugin,
and the plugin will simply go the wrong colour. There is no test on either side that would catch it.

## User stories

Epic headings are stable addresses. `/spec` and `/checklist` reference them by name.

### Epic 1 — A plugin can ask what colour the host is right now

**Story 1.1 — Reading the current palette on connect.**
As a plugin, I want to ask the host for its active palette at any moment, so that I can paint myself
correctly the instant I connect rather than waiting for the user to change something.

- [ ] A single call returns the host's currently applied palette. It is answerable at any time after
      handshake, not only when something changes.
- [ ] The palette is returned as **resolved colour values**. No theme id, no theme name, no file path,
      no enum. A plugin that receives this must have no way to look anything up, because looking
      things up is the defect.
- [ ] The response covers **every slot the host theme defines**, not only the seven ur-task currently
      consumes. Truncating to today's only consumer means a second package release the first time any
      plugin wants an expired-row colour.
- [ ] The call succeeds for a built-in theme and for a user-authored theme, and the caller cannot tell
      which it got. That indistinguishability is the requirement, not a side effect.
- [ ] The host always has an answer. There is no "no theme applied" state to represent, because the
      host applies a theme at startup before any plugin can connect.

**Why this story exists and the scope did not name it:** the scope described a push. A push alone
leaves a plugin that connects mid-session painted with its fallback until the user happens to switch
themes. The existing contract already draws this distinction — `GetRunningAccounts` pairs with the
launch and exit streams, and `HostInfo.multi_instance_state` pairs with the mutex stream, while
`SubscribeMemoryPressure` has no paired read at all. **A theme is state; memory pressure is an
occurrence.** State needs a read.

### Epic 2 — A plugin learns when the colour changes, without watching files

**Story 2.1 — Repainting when the user switches themes.**
As a clan member with a plugin window open, I want it to follow the host when I change themes, so that
the app looks like one app.

- [ ] Changing the theme in RoRoRo's Settings causes a connected plugin to receive the new palette
      without the plugin polling, and without it watching any host file or folder.
- [ ] The change arrives for **every** theme the host can apply: all four built-ins including
      flatline, and any theme the user wrote themselves.
- [ ] Editing a user theme file's colours in place, with that theme active, also produces a fresh
      palette. Today the plugin gets this for free from a file watcher; it must not be a regression.
- [ ] A plugin that is slow or briefly stalled catches up to the **current** palette. It must never
      replay a backlog of intermediate themes the user has already moved past — the only palette that
      has ever mattered is the latest one.
- [ ] Disconnecting and reconnecting a plugin re-establishes the feed with no host restart.
- [ ] When the host shuts down, the stream ends cleanly and the plugin does not crash, hang, or
      report an error to the user. A plugin outliving its host is a normal state here, not a fault.

**Story 2.2 — The host emits a theme change at all.**
As a maintainer, I want the host's own theme application to announce itself internally, so that the
plugin feed has something real to forward and is not reading UI state.

- [ ] A theme change raises a signal from the place the theme is actually applied, so that any theme
      change reaches it — not only one triggered by the Settings picker.
- [ ] The signal carries the resolved palette, not an id.
- [ ] The plugin host forwards it. A plugin with no interest in theming is unaffected.

**Note on current state, because this is the least obvious item in the cycle:** the host has **no**
theme-changed signal today. `IPluginEventBus` carries `AccountLaunched`, `AccountExited`,
`MutexStateChanged` and `MemoryPressure`. A theme change runs from the Settings picker into
`ThemeService.ApplyTo` and is heard by nothing. This story is net-new plumbing, not a forwarding
change, and it is the item most likely to be under-estimated.

### Epic 3 — Nothing that exists today breaks

**Story 3.1 — An existing plugin is untouched.**
As someone running ur-task 0.5.0 today, I want a RoRoRo update that adds this to change nothing about
my plugin, so that an update never costs me a working setup.

- [ ] A plugin built against the current contract connects, handshakes and runs with **no change to
      its binary, its manifest, or its declared capabilities.**
- [ ] The wire contract version string is unchanged. The handshake compares it by exact match, so any
      change there rejects every existing plugin outright — this is the single highest-consequence
      line in the cycle.
- [ ] Existing plugins are not required to consume the feed, and a plugin that ignores it behaves
      exactly as before, including its current file-watching behaviour.

**Story 3.2 — The host still starts.**
As a maintainer, I want the new methods registered in the capability map deliberately, so that the
host does not refuse to boot.

- [ ] Every new host RPC has an explicit entry in the capability map. **A method present in the
      contract but absent from that map crashes the host at startup by design**, and separately would
      be denied at call time. "Ungated" here means *registered and deliberately marked as requiring
      nothing* — the opposite of absent.
- [ ] The decision to require no capability is written down where the entry lives, in the same shape
      as the existing free reads.
- [ ] **Flagged for `/spec` to confirm rather than assume:** every stream that exists today is
      capability-gated, and the ungated entries are all one-shot reads. An ungated stream would be the
      first of its kind. The scope's reasoning is that a colour is not sensitive and that gating it
      would let a plugin be denied the ability to look correct — that reasoning is sound and it is
      still a deliberate break in an established pattern, which is worth saying out loud once.

**Story 3.3 — The change is provable in the suite.**
As a maintainer, I want this proven end-to-end by an automated test, so that it is not one more claim
owed to a human's eyes.

- [ ] The integration harness — which runs a real server over a real named pipe against a real client
      — exercises reading the palette and receiving a change over the wire, not through in-process
      stubs.
- [ ] A test proves an old-shaped plugin still handshakes after the addition.
- [ ] `dotnet test ROROROblox.slnx` is green, unit and harness.

**Why this criterion is written harder than usual:** the v1.18 reflection found that the suite
constructs no `Window` by design, so every on-screen claim ends up owed to a human. This cycle is the
rare one where the interesting behaviour is a wire protocol, and the harness already exists to test
exactly that. Spending it is the point.

### Epic 4 — The next plugin author does not have to reverse-engineer this

**Story 4.1 — The feed is documented.**
As a plugin author, I want theming documented in the author guide, so that I do not have to read
ur-task's source to find out how to match the host.

- [ ] The author guide describes how to read the current palette and how to subscribe to changes,
      with a worked example.
- [ ] It names the slots and what each one is for, so an author can map them to their own UI without
      guessing from names alone.
- [ ] It states plainly that reading the host's `settings.json` or `themes` folder is **not** a
      supported integration and will break. That sentence is the whole point of the cycle written
      down.
- [ ] The contract package is published at a new version, with the wire version explicitly noted as
      unchanged so the distinction is not re-blurred later.

### Epic 5 — ur-task stops keeping a copy

**Story 5.1 — The mirror is deleted.**
As a clan member, I want ur-task to take its colours from the host rather than from a copy, so that
every theme works and the next one works too.

- [ ] ur-task's hardcoded palette table is removed, along with its knowledge of the host's settings
      filename, key name, themes folder and file naming policy.
- [ ] Flatline applies to ur-task's window.
- [ ] A user-authored theme still applies — this works today and must not regress on the way through.
- [ ] ur-task remains usable with RoRoRo not running. Its own comment describes it as *"fully usable
      standalone"* and that is a real property, not an accident.
- [ ] Its manifest's minimum host version reflects the host release that ships the feed.

**Sequencing:** this epic lives in a different repository and does not have to ship at the same time.
The host leg is independently releasable and independently useful — it is what makes every *future*
plugin correct by default.

**Stated plainly so nobody is surprised at close-out:** **F-091 does not close until Epic 5 ships.**
The row's evidence is a plugin window that is the wrong colour, and the host leg alone does not repaint
it. The scope's "register goes to 38" is true at the *end* of both legs, not at the end of the host
leg. Calling the row closed on the host merge would be exactly the register defect this project
already wrote a rule about.

## What we're building

| # | Deliverable | Repo | Verified by |
| --- | --- | --- | --- |
| 1 | A palette message carrying every host theme slot as resolved values | RoRoRo | Contract + harness |
| 2 | A read RPC returning the currently applied palette | RoRoRo | Harness, real pipe |
| 3 | A streaming RPC delivering the palette on change | RoRoRo | Harness, real pipe |
| 4 | A theme-changed signal on the internal plugin event bus, raised at apply time | RoRoRo | Unit |
| 5 | Capability-map entries for both new methods, explicitly ungated | RoRoRo | Startup assertion + unit |
| 6 | Contract package version bump, wire version unchanged | RoRoRo | Handshake test |
| 7 | Author-guide section on theming, including the "do not read our files" statement | RoRoRo | Read it |
| 8 | ur-task consumes the feed and deletes its mirror + storage assumptions | ur-task | Run it, all four themes |

## What we'd add with more time

- **A palette for the host's own plugin-rendered UI.** Row badges, tray items and status panels accept
  a `color_hex` from the plugin, which is the same defect in miniature: a plugin choosing a colour to
  paint into the *host's* window. It is not urgent because the UI host is currently a stub that logs
  and renders nothing, so there is no surface mis-coloured today. When those land, they should default
  to the active palette and treat a plugin-supplied hex as the exception.
- **Font and metric tokens.** The palette is colour only. Type scale and spacing are the obvious next
  thing a plugin would want to match, and the obvious next thing to hand-copy.
- **A conformance check for plugin authors** — something an author can run to see whether their window
  actually repaints, rather than eyeballing four themes.
- **Retiring the three hand-synced palette copies inside RoRoRo itself.** Already an open issue; the
  plugin mirror was the fourth copy and this cycle removes only that one.

## Non-goals

- **Adding flatline to ur-task's table.** A one-line fix for the visible symptom that creates a fifth
  copy and leaves the sixth built-in broken the same way.
- **Writing the host's built-in themes to disk.** The strongest rejected alternative, cut in scope
  with its reason: it fixes availability while keeping all five storage couplings, and it turns the
  "a built-in wins an id collision" rule from an in-memory decision into a file-on-disk race.
- **Letting a plugin author, define, or override a theme.** The host owns the theme. Plugins receive
  it. A plugin that could write themes is a plugin that can make the host's own window unreadable.
- **Gating the feed behind a user consent capability.** Capabilities fence things that can cause harm.
  A colour is not one, and gating it would mean a plugin can be denied the ability to look correct.
- **Anything beyond colour.** No layout, no fonts, no per-plugin overrides, no theme-conditional
  behaviour on either side.
- **F-050, F-068, F-046** — the standing exclusions, unchanged.

## Edge cases surfaced

Behaviours that must be decided rather than discovered during the build:

1. **A plugin connects before the host has applied a theme.** Believed impossible — the host applies at
   startup, well before the plugin host accepts connections — but "believed impossible" is how a null
   palette reaches a plugin. `/spec` should either prove the ordering or define the answer.
2. **A theme is switched while a plugin is mid-repaint.** The requirement is convergence on the latest
   palette, never a queue of stale ones.
3. **The user edits the active theme file by hand while a plugin is connected.** Works today via the
   plugin's file watcher. If the plugin stops watching files, the host must be the one to notice.
4. **A malformed user theme.** The host already drops unreadable theme files rather than applying
   them, so the feed can only ever carry something the host itself applied. This is worth an explicit
   test rather than an assumption.
5. **A plugin subscribes twice.** Should be harmless.
6. **The host is closed while a plugin is running.** Clean stream end, plugin keeps working, no error
   surfaced to the user.
7. **A brand-new plugin is installed against an older host that lacks the feed.** This is the open
   question below, and the only edge case with no default.

## Open questions

| Question | Needs answering |
| --- | --- |
| **How does a plugin discover whether the host supports the feed?** Catch the not-implemented error and fall back, or advertise host capabilities in the existing host-info response. The second is field-additive and safe, and it is also a standing commitment about how this contract advertises every future addition. | **Before `/spec` finishes.** Deliberately left undefaulted — it is small now and load-bearing later. |
| **Does ur-task keep its disk reader as the no-host fallback, or collapse to a constant?** Keeping it preserves the user's theme when the plugin runs with RoRoRo closed. Dropping it is the only way the five couplings actually die. | At `/spec`, and it may resolve to "keep the reader, delete the mirror," which is a real third answer. |
| **Does the palette message carry the host's slots one-to-one, or a plugin-facing subset with a derived hover?** ur-task derives its hover by tinting; if every plugin will do that, the derivation may belong in the contract. | At `/spec`. Low consequence either way. |
| **Does the host leg ship as its own release, or wait for ur-task?** The PRD assumes independent release. | Before the release, not before the build. |
