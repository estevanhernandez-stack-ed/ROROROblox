# RoRoRo v1.19.0.0 — release notes

> **Written retrospectively on 2026-08-12.** v1.19 merged to `main` on 2026-08-11 and was never
> tagged, because the Store submission was parked. These notes were reconstructed from the archived
> spec (`docs/superpowers/specs/2026-08-11-rororo-plugin-theme-feed-design.md`) and the feature
> ledger row, not written at merge time. Nothing here is guessed — but if a detail reads thinner
> than the v1.18 notes, that is why.

Mostly a release for plugin authors, and if you do not use plugins there is genuinely nothing here
for you. Skip to v1.20.

The one-line version: **RoRoRo now tells a plugin what colour it is**, so plugins stop guessing.

## Short list, for the GitHub release and the Discord post

```
• Plugins can now ask RoRoRo for the current theme and get the actual colours back. Before this, a plugin that wanted to match the app had to keep its own copy of RoRoRo's built-in themes and hope nobody edited one.
• Plugins can subscribe to theme changes. Switch RoRoRo to flatline and a plugin window can follow, live, without being restarted.
• Ur Task drops its hand-copied mirror of the four built-in themes, and five assumptions it was making about where RoRoRo keeps its files.
• Neither new call needs a permission grant. A plugin asking "what colour are you" is not being told anything about you, your accounts, or your machine, so there is nothing to consent to.
• No existing plugin breaks. The wire contract version is unchanged, so a plugin built before this release is still accepted exactly as it was.
```

---

## Longer form

### The problem this fixes

A plugin runs as its own process with its own window. If it wants to look like part of RoRoRo, it
needs RoRoRo's colours. Until v1.19 there was no way to ask, so the only option was to hard-code
them: keep a copy of the built-in palettes, match on a theme name, and read RoRoRo's settings file
to find out which one was active.

That works right up until any of it moves. A renamed theme, a re-tuned colour, a changed storage
path, a user-authored theme the plugin has never heard of — each of those silently breaks the copy,
and the plugin has no way to know it is now wrong.

**Ur Task was doing all of it.** A mirrored copy of the four built-in themes, plus five separate
assumptions about RoRoRo's storage layout. All of that is deleted in this release.

### What replaced it

Two calls on the plugin contract:

- **`GetTheme`** returns the palette RoRoRo is currently rendering with.
- **`SubscribeThemeChanged`** pushes a new palette whenever the user switches theme.

Both are **ungated** — no capability, no consent prompt. That is deliberate and worth being explicit
about: the palette describes *the app*, not the user. It carries no account, no cookie, no path, no
machine detail. There is nothing for a user to be asked to approve, and adding a prompt would train
people to click through prompts that do not matter.

### The eleventh slot

A theme record has ten colours. The palette a plugin receives has **eleven**.

The extra one is the derived interactive edge — the outline that separates a control from what it
sits on. RoRoRo computes it from the theme rather than storing it, because it has to clear a
contrast floor against whatever ground it lands on. A plugin could not reproduce that without
carrying a second copy of the host's contrast logic, which is the exact class of duplication this
release exists to delete. So it ships in the palette.

### Compatibility

Wire `contractVersion` stays `"1.0"`. A plugin built against any earlier contract is accepted
unchanged and is never asked about theme. The contract NuGet package moves **0.7.0 → 0.8.0**.

### Known gap, carried into v1.20

`ROROROblox.PluginContract 0.8.0` is not on nuget.org, and neither are 0.5.0 through 0.7.0. The
RoRoRo half of this release is complete and proven over the wire; the `rororo-ur-task` half is
written but its PR stays red on `NU1102` until the package lands. This is tracked on the feature
ledger and is not a defect in the app.
