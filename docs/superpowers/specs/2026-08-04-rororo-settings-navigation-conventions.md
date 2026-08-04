# Conventions brief — Settings + navigation

> **The measuring stick for this campaign.** Scoped campaigns extract, they do not invent: every
> rule below is something RoRoRo already does somewhere, stated so the area can be judged against
> it. Later findings argue **conformance to these conventions** or **progress toward the goal** —
> never taste.
>
> Evidence: 12 captures across `brand` and `flatline`, `docs/ui-evidence/` (gitignored).
> Campaign state: `.vibe-glow/state.json`.

## The goal, verbatim

1. "get our settings section and all the other items that are settings into the settings section" —
   streamer mode, and probably About, History, Diagnostics, currently exposed on the main window.
2. "our settings model was already getting a little tall, so we need to work on how to split that
   up as well."
3. "I don't mind additional buttons on the main page, but I want to make sure that they're leading
   to what they should lead to" — amended on confirmation: "main window buttons are fine to keep or
   change to something if we need a different section for some of our items like tools." **A Tools
   container is in scope.**
4. "we should look at the titles on some of the pages because it looks more like we were getting an
   app ready for a hackathon where we wanted it to kind of explain the page at the top and look
   like an ad more than an app."

**Left open deliberately:** History and Diagnostics are tools, not preferences. "Under Settings"
may be the wrong home. The user's word was "maybe"; this campaign decides it on evidence.

## Invariants (verbatim from campaign state — these outrank any finding)

1. Color belongs to the theme system; identity lives in structure — every surface binds
   `DynamicResource` brushes and users ship their own JSON themes, so a finding that prescribes a
   color is invalid. Findings must argue layout, weight, spacing, or shape.
2. The 626 Labs duo is never split — cyan `#17d4fa` and magenta `#f22f89` appear together or not at
   all. A reviewer proposing one alone is proposing an off-brand surface. **Corrected 2026-08-04:
   two shipped windows already breach this** — `JoinByLink/JoinByLinkWindow.xaml:27-33` and
   `About/WelcomeWindow.xaml:38-43` both render the two-tone header with no magenta.
3. Type roles are fixed — Space Grotesk display, Inter body, JetBrains Mono for small meta labels
   only (uppercase, 0.12em tracking). Findings may argue size, weight, and hierarchy, never a
   different family.
4. WPF-UI (lepoco) owns control chrome — findings target our composition, not the library's control
   internals. The themed `MenuItem` check-glyph behavior is already worked around deliberately in
   two places and is not a defect to re-litigate.
5. No emoji in UI copy — this is a Store-listed product. **Corrected 2026-08-04: the rule is NOT
   currently enforced.** Two emoji ship — `MainWindow.xaml:302` `"🎲 Reroll identity"` and
   `Theming/ThemeBuilderWindow.xaml:52` `"📋 Copy AI prompt"`.

   **AMENDED 2026-08-04 by Este, and this ruling governs.** The die on *Reroll identity* stays. It
   is thematic rather than decorative: streamer mode exists to hand accounts deliberately silly
   fake names, and a die on the action that re-rolls them reads as part of that feature, not as
   ornament. The real defect at that site is **placement, not the glyph** — Reroll identity is a
   settings-shaped action sitting on the main dashboard, and moving it (goal 1) resolves what the
   emoji finding was really reacting to. Do not strip either glyph as part of a copy wave. Revisit
   only if a Store reviewer raises it.
6. Token-contract extensions must be optional-with-fallback — `Theme` has exactly ten required slots
   and user themes on disk supply all of them. A new slot breaks every existing user theme unless it
   defaults.

---

# Observed conventions

## C1 — Window chrome: a title bar plus a two-tone page header plus a subtitle

Every non-modal window opens with the same three-part stack: the OS title bar, a large header
where the first word is cyan and a magenta `/` separates it from a white second word, and a grey
one-line subtitle explaining the page.

Observed: `RoRoRo / Accounts` over "Run multiple Roblox clients side by side. Add a saved account,
click Launch As to open it." · `Preferences / Settings` over "How RoRoRo behaves — startup, idle,
Discord, alerts, and theme."

**This is the pattern goal 4 is about.** Two observations, both from evidence rather than taste:

- **It repeats what the title bar already said.** The window is titled "RoRoRo -- Preferences" and
  the header says "Preferences / Settings." The user reads the product's name three times before
  reading a setting.
- **The subtitle explains the page to someone who already chose it.** A user who clicked Settings
  knows they are in settings.

The device itself is not the problem — a page needs a name. The convention to hold is: **name the
page once, in one place, and let the content start.**

## C2 — Titles: seven competing conventions across 25 windows

| Pattern | Windows |
| --- | --- |
| `RoRoRo -- X` | Diagnostics, History, Preferences, Plugins, Library, Build a theme, Install plugin |
| Prose with the app name | "About RoRoRo", "Welcome to RoRoRo" |
| Bare noun | "Join by link", "Squad Launch", "Rename", "Export accounts", "Import accounts", "Private server" |
| Problem statement | "Roblox is already running", "Roblox needed", "Saved accounts can't be unlocked", "Microsoft WebView2 needed", "Leftover Roblox processes" |
| Imperative | "Pick a title-bar color", "Add Roblox account — log in", "Stop all Roblox instances" |
| **Repo name, three parts** | `FriendFollowWindow` — set at runtime, `Friends/FriendFollowWindow.xaml.cs:133`: `"ROROROblox -- Friends -- {name}"`. The only three-part title, and the only one built on the repo name. |

> **Corrected 2026-08-04 by the audit's skeptic pass.** This section originally said six conventions and listed `FriendFollowWindow` as having no title at all — a XAML-only sweep missed that it sets one in code-behind. The count is seven.

The defensible split already present in the app: **destinations take a noun, interruptions state
the problem.** "Squad Launch" is a place you went; "Roblox is already running" is something that
happened to you. That distinction is worth keeping; the six-way spread is not.

## C3 — The main window mixes three kinds of control in one band

One row, one visual weight, six buttons: `Settings · About · History · Diagnostics · Games ·
Plugins`. Immediately beside them, in the same band: the default-game picker, `Launch multiple`,
`Squad Launch`. Immediately below: a `Streamer mode` toggle with its own helper sentence, and
`Reroll all identities`.

That is **navigation**, **actions**, and **a setting**, rendered identically and adjacent. Nothing
in the visual language tells a user that Settings and Squad Launch are different kinds of thing.

## C4 — Naming already drifted once, in the code

`Settings/SettingsWindow.xaml` is titled **"RoRoRo -- Library"** and is the game library. A class
named `SettingsWindow`, in a folder named `Settings`, that is not settings — opened from
`MainViewModel:3187` behind a button labeled `Games`. Any settings reorganization that does not fix
this inherits it.

## C5 — Grouping is done with a filled card, and only with a filled card

The Preferences page groups settings into `RowBgBrush` cards with `CornerRadius="8"`,
`Padding="14"`, `Margin="0,0,0,10"`. Account rows on the main window use the same device. It is the
app's only grouping primitive.

**Under `flatline` this fails.** With `row_bg == bg`, the Preferences cards vanish entirely and the
page becomes an undifferentiated column of checkboxes and paragraphs — no rule, no indent, no
spacing rhythm distinct enough to separate one setting from the next. The main window survives
better, because avatars, spacing, and the filter bar carry the rows even when their fill is gone.

Stated as a rule: **grouping must survive a theme that flattens fills.** Today, in Settings, it does
not. This is the strongest evidence for goal 2 — "splitting it up" cannot mean *more cards*, because
cards are the thing that is not load-bearing.

## C6 — Secondary buttons lose their affordance when fills collapse

Under `brand`, `Settings · About · History · Diagnostics · Games · Plugins`, `Reroll all
identities`, and per-row `Remove` read as buttons. Under `flatline` they read as plain text —
nothing but color distinguished them from labels. Primary actions survive (`Squad Launch`,
`Launch As`, `+ Add Account` carry accent fills; `Launch multiple` carries a border).

Stated as a rule: **a control's interactivity must be legible from shape, not fill alone.** The
app already demonstrates the fix — `Launch multiple`'s border survives Flatline intact.

## C7 — Helper prose is long, and sits at the same level as the thing it explains

Every Preferences checkbox carries a two-to-three-line explanation in `MutedTextBrush` at 11px.
The main window carries a five-line FPS warning banner. Under `flatline`, where `muted_text ==
white`, that prose competes with the labels at equal weight.

The prose is good and users need it — the alerts work this week proved that. The rule is about
**hierarchy**, not length: explanation must be subordinate by size or position, not by color alone.

## C8 — Established component vocabulary (keep, do not reinvent)

- Cards: `CornerRadius="8"`, `Padding="14"`, `Margin="0,0,0,10"`, `RowBgBrush`.
- Section heading: 13px SemiBold `WhiteBrush`, with 11px `MutedTextBrush` body beneath.
- Primary CTA: accent fill, `BorderThickness="0"`, 11px SemiBold.
- Secondary: `NavyBrush` fill with a `DividerBrush` border.
- Small meta labels: uppercase, letter-spaced — the `DEFAULT` chip on the game picker.
- Dialog close: a `Close` button, bottom-right. Consistent across every window checked.
- Row context menus carry the check-state in the header text, not a glyph (a deliberate WPF-UI
  workaround, invariant 4).

## C9 — Copy voice, already consistent and worth protecting

Second person, sentence case, no emoji, plain words. "Recycle closes a client and puts it straight
back into the server it was in." The voice is not the problem anywhere in this area — the
*placement and repetition* of copy is.

---

## What this brief does not decide

Deliberately left to the audit and the waves, so findings are argued from evidence rather than
settled here by assertion:

- Whether History and Diagnostics belong in Settings, in a Tools container, or where they are.
- Whether the six main-window buttons become a Tools group, a menu, a sidebar, or stay as-is.
- How Settings splits — nav rail, tabs, or grouped sections with real structure.
- What replaces the two-tone header, if anything.

The brief's job is to make sure whatever is proposed can be checked against something.

## C10 — The repo name leaks into user-facing copy (added by the audit, 2026-08-04)

The product is **RoRoRo**; `ROROROblox` is for code identifiers only. It reaches users in four
places, one of which is the surface a tray-resident app shows most often:

- `Friends/FriendFollowWindow.xaml.cs:133` — window title, so also the taskbar and Alt-Tab
- `Tray/TrayService.cs:221` — "Open ROROROblox" in the tray menu
- `Tray/TrayService.cs:98-100` — the tray tooltip, all three states
- `Diagnostics/DiagnosticsWindow.xaml.cs:220` — "ROROROblox support snapshot", first line of a
  file users forward to other people

The v1.15 naming pass fixed the exe metadata, the installer, and the package. It did not reach
these, because they are runtime strings rather than build-time metadata.
