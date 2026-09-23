# RoRoRo v1.31.0.0 — release notes

> **Edit log.** Nothing dropped from v1.30's sections. The three known-issue paragraphs are carried
> forward **verified rather than assumed**, per the playbook's standing rule. The localization pair:
> `git log v1.30.0.0..main` touched nothing under `Localization/` and not `DiscordPresenceService.cs`;
> it did touch `SettingsPage.xaml.cs`, so that was read rather than skipped — the change adds
> `RevealMemorySettings` for the new page's button and leaves `OnUiCultureChanged` alone, whose doc
> comment still names the memory section and the Discord status line as deliberate next-open
> refreshes. The cooldown race: `AlertDispatcher.cs` line 53 still names it as known and
> deliberately unfixed, and no alert file is in the diff. The toast note is unchanged for the reason
> it has always been true — no automated test can see a Windows toast. One paragraph is NEW: the
> notice lives in the main window only, which the spec records as a decision, not a discovery.
>
> **The headline is told as a story, and the story blames nobody.** Twice a clan member said "I
> think RoRoRo has a problem" and both times it was Roblox (spec, Why). That was a fair guess each
> time — RoRoRo is the thing they opened — so the notes say so, and name neither person.
>
> **Everyone sees a notice on day one, and the notes say it out loud.** The freeze entry has
> `notify: true` and no `robloxVersions`, so it applies to every PC. `known-issues.json` has been
> attached to the v1.30.0.0 release since 2026-09-23 20:57 UTC and `release.yml` attaches it to the
> v1.31 tag too, so the first check after the update finds both entries. A notice nobody was told
> about reads as a new problem.
>
> **Left out, on purpose:**
>
> - **`103ec78` (the #213 credential-entry strings).** Not a user-visible fix. The six keys shipped
>   translated in v1.30: `82976c9` (#213, in v1.30.0.0) added all six to every `Strings.<culture>.resx`.
>   `103ec78` backfilled them into the `ui-<culture>.json` source files so the lint passes; its only
>   change to the six satellites is a dropped byte-order mark on line 1. Nobody saw an English string.
> - **Ur Score 0.6.2 in the plugin catalog.** `plugins-catalog.json` is served from the latest
>   release, not the binary, and the 0.6.2 catalog was uploaded to the v1.30.0.0 release on
>   2026-09-23 at 19:35 UTC — `releases/latest/download/plugins-catalog.json` serves 0.6.2 today.
>   Tagging 1.31 changes nothing for anyone, so naming it here would announce something already live.
>   (1.30 named Ur Score because 1.30 was when it became installable; Ur Score's own release notes
>   carry its versions.)
> - **CI, test and tooling work:** the compat workflow shipping `roblox-compat.json` before touching
>   `known-issues.json` (`00ea610`), the webhook catcher's mid-read test (`1606bb6`), the page render
>   tests, and the validator inside `tools/CompatSigner`. Real work, invisible to anyone using the app.
> - **The version-drift banner that can never fire** (spec §4). Pre-existing, deliberately not
>   fixed, and not something a user can see or act on.
>
> **Privacy policy updated in the same release**, because it says a material change gets called out
> in the notes: `docs/PRIVACY.md` now names `known-issues.json` on the existing `github.com` Releases
> row, the four-hour refresh, and the two cache files. No new host.

---

# RoRoRo v1.31.0.0

**Download:** [rororo-win-Setup.exe](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest)

## Short list, for the GitHub release and the Discord post

```
• RoRoRo now tells you when the problem is Roblox's. A new page, Known Roblox issues, lists problems in Roblox itself: what happens, what to do, and the RoRoRo feature that helps, with a button that takes you to it. Tools menu, or Ctrl+7 in the tools window.
• A serious one gets a notice, once. It sits at the top of the main window with "See known issues" beside it. Close it and it stays closed; only a new issue brings it back. Expect one the first time you open 1.31, about the freezing window.
• Two are on the list today. The Roblox window that freezes when you drag or resize it (a frame-rate cap of 60 stops it for most people), and Roblox using more memory the longer it runs (the memory watchdog and Recycle exist for exactly this).
• The list updates without an app update. RoRoRo checks when it starts and every four hours, from the same place it already gets its Roblox compatibility settings. The file is signed, and nothing about you is sent.
• Nothing else moved. Accounts, themes, plugins, alerts, history, and settings all carry over untouched.
```

## Longer form

### RoRoRo now tells you when the problem is Roblox's

Twice now, someone in the clan has come to us with "I think RoRoRo has a problem." Both times it
was Roblox. That was a fair guess each time — RoRoRo is the thing you opened — and both times the
answer existed. It just had nowhere to live inside the app.

The first was memory. Roblox clients grow the longer they run, and with enough of them open your PC
runs out and Windows starts closing windows overnight. Working that out is what built the memory
watchdog and Recycle. The second, on 22 September, was a Roblox window that froze when you dragged
or resized it, until you pressed the Windows key. We traced it into Roblox's own drawing code and
matched it to reports on Roblox's developer forum.

So now there is a place for it. **Tools › Known Roblox issues** (Ctrl+7 inside the tools window)
lists problems in Roblox itself. Each one says what happens, what to do about it, and — where
RoRoRo has something for it — a **RoRoRo can help** box with a button that takes you straight to
that feature. Every entry carries a small **Roblox** tag, so you can tell at a glance whose problem
it is. The menu item shows a count when entries apply to your PC.

### What is on the list today

**The Roblox window freezes when you drag or resize it.** Press the Windows key, then Escape, and it
moves again. A lower frame-rate cap stops it for most people: set that account's cap to 60. The
**Show accounts** button brings the main window forward, where the cap sits on each account row.

**Roblox uses more and more memory the longer it runs** — about 300 MB an hour per client, even
sitting idle. Restarting long-running clients about once a day keeps it in check. Keep the memory
watchdog on: it warns you before a client runs your PC out of memory, and a Recycle button appears on
that account's row that restarts the client back into the same server. **Open memory settings** takes
you to the switch.

### A notice for the serious ones, once

An issue marked serious shows one notice at the top of the main window, with **See known issues**
and a close button. Close it and it stays closed. Only a new issue brings it back. If more than one
applies, it counts them instead of naming one.

**Expect one the first time you open 1.31**, about the freezing window. That is the feature working,
not a new problem. And if RoRoRo is already warning you that another tool is holding multi-instance,
that warning comes first and this one waits.

### The list updates without an app update

RoRoRo checks for a new list when it starts and every four hours while it runs, from the same place
it already gets its Roblox compatibility settings — no new address. The file is signed, so RoRoRo
checks it came from us before it reads a word of it, and keeps the last good copy so the page still
works offline. It is a plain download: nothing about you or your PC is sent. The privacy policy now
lists it.

The page itself — headings, buttons, the notice — is in all seven languages. The issue descriptions
are written in English for now, and a line under the page title says so in your language.

### Known issues going into the next update

**The known-issues notice waits for the main window.** If you keep RoRoRo in the tray, you will see
it the next time you open the window. That is deliberate: a notice beats a Windows toast for
something you can read at your own pace, and the page and its count are there either way.

**Two spots still wait for the next open when you change language:** the memory section in Settings
and the Discord presence status line. Both are deliberate rather than missed — the memory section
re-reads your settings to redraw, which risks stomping an edit you are in the middle of, and the
presence line re-narrates on its next update anyway. Every other surface switches instantly.
Re-checked against the code for this release, not copied forward.

**Nobody has looked at a real toast for a big group yet.** The desktop balloon's limits were measured
from the library that draws it, and the tests hold the app to them, but Windows renders that balloon
as a toast and may clip further than those numbers — and no automated test can see a toast. If a
four-account-or-more group looks wrong on your desktop, that is worth an issue.

**Two groups for the same stat but different rules can both alert one account** if they finish within
milliseconds of each other, because the cooldown is stamped after sending rather than before. Rare,
harmless, and on the list.

### Other channels

- **Microsoft Store:** [RoRoRo on the Store](https://apps.microsoft.com/detail/9NMJCS390KWB) — this
  is the version going up for certification. The Store updates you automatically once it clears.
- **Sideload MSIX:** attached to this release with `dev-cert.cer` — import the cert to Local
  Machine → Trusted People first, same as before. No cert rotation this release.

### Issues, ideas

Something broken, something missing, a translation that reads wrong in your language, or a Roblox
problem you think belongs on the list:
[github.com/estevanhernandez-stack-ed/ROROROblox/issues](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues).
Privacy policy:
[estevanhernandez-stack-ed.github.io/ROROROblox/privacy](https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/).
