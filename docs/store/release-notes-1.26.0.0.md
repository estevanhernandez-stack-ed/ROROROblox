# RoRoRo v1.26.0.0 — release notes

The release where RoRoRo stops assuming you read English. The whole interface —
every menu, setting, tooltip, and window — now speaks French, German, Russian,
Portuguese (Brazil), Polish, or Spanish, and it follows your Windows language on
its own. Brazil and the rest of the clan's non-English half get RoRoRo in their
own words for the first time.

If you only read one line: **RoRoRo now speaks six languages beyond English —
fr, de, ru, pt-BR, pl, es — picked up from Windows automatically or chosen in
Settings → Appearance, and the picker only ever offers a language that's fully
translated.**

## Short list, for the GitHub release and the Discord post

```
• Your language, everywhere. The entire interface is translated into French, German, Russian, Portuguese (Brazil), Polish, and Spanish — not just a screen or two, the whole app. If Windows is set to one of them, RoRoRo uses it with no setup.
• A Language picker. Settings > Appearance has a new dropdown. It lists only the languages RoRoRo is fully translated into, so you can't land on a half-English screen. Your pick takes effect next time you open RoRoRo.
• Product names stay put. Squad Launch, Recycle, Friend Follow, RoRoRo — the feature names read the same in every language, so a tip you saw in English still points at the same button.
• Nothing else moved. English is still the default; your accounts, themes, plugins, history, and settings all carry over untouched.
```

---

## Longer form

### Your language, the whole way down

Every user-visible string in RoRoRo — 483 of them, from the big dialogs down to
a tooltip on a single dot — is now a resource the app resolves in your language.
There is no half-measure: menus, the settings pages, the welcome tour, every
modal, the About box. If your Windows display language is French, German,
Russian, Portuguese (Brazil), Polish, or Spanish, RoRoRo comes up in it the
first time you launch, no setting to find.

### The picker only offers what it can deliver

Settings → Appearance grew a **Language** dropdown. What makes it worth calling
out is what it *won't* let you do: it lists only the languages RoRoRo actually
ships a complete translation for. You can't select a language and then discover
half the screen fell back to English — if it's in the list, it's done. English
is always there as the default. A change applies the next time you open RoRoRo
(the app builds its screens once at startup), and the dropdown says so.

### Product names are the same in every language

Feature names — Squad Launch, Recycle, Friend Follow, and RoRoRo itself — stay
in English on purpose. So do Roblox, Discord, Pushover, and ntfy. A walkthrough
that says "click Squad Launch" points at a button that says Squad Launch no
matter which language you're reading the rest of the app in.

### How the translations were made

Each language was drafted string-by-string with product nouns held fixed and
whitespace preserved, then reviewed for voice and accuracy — the same
verification gate the Store listings go through. One rule the review enforced:
a feature name is never translated, so the app and the store listing always
agree on what a thing is called.

### Compatibility

Everything carries over — saved accounts, themes, plugins, history, settings.
No new Windows permissions and no new network behaviour: localization is
resources compiled into the app, nothing that phones anywhere. The app package
now declares the six languages it ships (it declared English only before); each
declaration is backed by a complete translation, never a promise the interface
doesn't keep.

**Rolling back?** If you set a language and later go back to an older version,
the older one doesn't know the setting and comes up in English. Harmless — set
it again when you're back on current, or just don't roll back.

### Other channels

- **Microsoft Store:** [RoRoRo on the Store](https://apps.microsoft.com/detail/9NMJCS390KWB) —
  this version is submitted for certification, and the store listing now reads in the same six
  languages; the Store updates you automatically once it clears.
- **Sideload MSIX:** attached to this release with `dev-cert.cer` — import the cert to Local
  Machine > Trusted People first, same as before. No cert rotation this release.

### Issues, ideas

Something broken, something missing, or a translation that reads wrong in your language:
[github.com/estevanhernandez-stack-ed/ROROROblox/issues](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues).
Privacy policy:
[estevanhernandez-stack-ed.github.io/ROROROblox/privacy](https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/).
