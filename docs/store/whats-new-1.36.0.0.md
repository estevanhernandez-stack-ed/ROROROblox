# What's new in this version — v1.36.0.0

> Paste the matching fenced block into Partner Center → your app → **Store listings** → *What's new
> in this version*: the English block for the en listing, and each translated block for its own
> language listing. (Since v1.28 the rows go in through the listing console instead; the blocks are the
> same either way.)
>
> **Written for someone on the v1.35 Store install.** Single-version delta. The account manager leads
> because it changes what the main window shows the moment the update lands; the Library follows
> because "Games" is gone from the toolbar and the rail, and a reader has to be told where it went.
>
> **The quit line is the spec's (§7.7) and the README's.** An update that lands while RoRoRo is running
> can fail to install. It goes in every language.
>
> **Left out on purpose:** the input-credit fix for the idle clock (#221) and the cleanup of background
> Roblox clients (#222) are described in customer words, not mechanics; the new automated checks and
> fences are not mentioned at all. Nothing about plugin contracts.
>
> **UI names are the app's own translations**, read from the per-culture catalogs rather than
> re-translated: the Library (`ShellWindow_Games`), "Play as" (`Library_PlayAs`), and the three health
> words (`Shell_Health_Expired`, `Shell_Health_NeedsVerification`, `Shell_Health_Limited`). Each block
> quotes its own language's labels exactly as the app renders them.
>
> Register follows each existing sheet: **vous** in French, **вы** in Russian, **du** in German,
> informal elsewhere. Product nouns stay English: RoRoRo, Roblox, Robux, Plus, Microsoft Store.
>
> Sources: `docs/cycles/2026-11-01-account-library/` (spec, checklist), `docs/decisions.md`.

---

## English

```
v1.36.0.0

Your accounts, at a glance

• Each account shows its Robux, Plus and friends online, with a
  note you write yourself.
• Open an account to see its recent games, favourites and
  private servers, and play any of them in one click.
• When an account needs you, a badge says why: Session expired,
  Needs verification, or Limited by Roblox. One click starts the
  fix.
• Games is now the Library: every game you've played,
  favourited or saved, in a grid or a list. Search it, filter it,
  paste a game link, and choose who plays with "Play as".
• The welcome tour starts with one account; the tips for several
  accounts come after.
• Idle time now counts for the account you actually played on,
  even when you switch windows quickly.
• Background Roblox processes that never opened a window close
  on their own after two minutes.

Quit RoRoRo before updating from the Store: right-click the tray
icon and choose Quit.

Nothing else moved. Accounts, themes, plugins, history and every
other setting carry over untouched.
```
