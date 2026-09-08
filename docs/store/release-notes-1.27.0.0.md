# RoRoRo v1.27.0.0 — release notes

v1.26 taught RoRoRo six languages, and it spoke them on every screen that sits
still. The moment the app had something to *tell* you — a launch result, a
warning, a confirmation, the tray menu — it went back to English. v1.27
finishes the job. Everything RoRoRo writes while you use it now speaks your
language too, and switching languages happens the instant you pick one instead
of the next time you open the app.

If you only read one line: **the half of RoRoRo that talks back — status
messages, confirmations, errors, the tray menu, the plugin screens — is
translated now too, and the language picker takes effect immediately.**

## Short list, for the GitHub release and the Discord post

```
• The rest of the app speaks your language. v1.26 translated the screens; the messages RoRoRo writes as you use it — launch status, confirmations, warnings, error messages, the tray menu, the plugin consent screens, Diagnostics — were still English. They aren't now. RoRoRo went from 483 translated strings to 1,017.
• Switching language is instant. Pick one in Settings > Appearance and the app re-renders in place — no restart, no reopening.
• It still follows Windows on its own, and now tells you once where to change it the first time you open this version.
• A security warning got its teeth back. The account-export warning says "don't post the file publicly" — in French, Spanish, German, and Portuguese that had been softened to just "don't publish the file." It's explicit again in every language.
• Nothing else moved. Accounts, themes, plugins, history, and settings all carry over untouched.
```

---

## Longer form

### The half that talks back

v1.26's translation covered the app's furniture: menus, settings pages, labels,
the About box. What it didn't cover was everything RoRoRo *composes* while you
work — "3 accounts launched", "couldn't save that game", the memory warning, the
squad-launch summary, the tray menu, the plugin consent sheet, the Diagnostics
panel. Those were assembled in code, in English, and no amount of translated
furniture reached them.

They're resources now, the same as everything else. The count is the honest
measure: 483 translated strings in v1.26, 1,017 in this one. If you run RoRoRo
in French, you should not see an English sentence anywhere. If you do, that's a
bug worth reporting — there's no longer a category of message that's expected to
stay English.

### Instant, not next launch

v1.26's language picker said your choice applied the next time you opened
RoRoRo, because the app built its screens once at startup. It doesn't work that
way any more. Pick a language and the window re-narrates in place — the main
list, the shell, the settings summaries, the tray. The dropdown no longer warns
you about a restart, because there isn't one.

### One notice, once

RoRoRo follows your Windows display language on its own if you've never chosen
one, which means updating to this version can change the language of an app you
were reading in English. The first time you open v1.27 you get a single tray
notice saying so and pointing at Settings → Appearance. It shows once, then
never again — and it works in both directions, whether you want your own
language or want English back.

### The export warning says what it means

The warning after exporting an account bundle ends, in English, "don't post the
file publicly." In French, Spanish, German, and Brazilian Portuguese it had been
softened to "don't publish the file" — the explicit *publicly* dropped out.
That's a security instruction, and the weaker version is a weaker instruction,
so it's back to explicit in all six languages. A few Polish plural forms were
corrected in the same pass.

### Compatibility

Everything carries over — saved accounts, themes, plugins, history, settings. No
new Windows permissions and no new network behaviour: this is all resources
compiled into the app, and nothing about it phones anywhere. The package still
declares exactly the six languages it ships, each backed by a complete
translation.

**Rolling back?** An older version doesn't know about the language setting and
comes up in English. Harmless — set it again when you're back on current.

### Known issues going into the next update

Two spots still wait for the next open rather than switching in place: the
memory section in Settings, and the Discord presence status line. Both re-read
correctly the next time you open the page — every other surface switches
instantly.

### Other channels

- **Microsoft Store:** [RoRoRo on the Store](https://apps.microsoft.com/detail/9NMJCS390KWB) —
  this is the version going up for certification, and the listing reads in the same six
  languages. The Store updates you automatically once it clears.
- **Sideload MSIX:** attached to this release with `dev-cert.cer` — import the cert to Local
  Machine > Trusted People first, same as before. No cert rotation this release.

### Issues, ideas

Something broken, something missing, or a translation that reads wrong in your language:
[github.com/estevanhernandez-stack-ed/ROROROblox/issues](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues).
Privacy policy:
[estevanhernandez-stack-ed.github.io/ROROROblox/privacy](https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/).
