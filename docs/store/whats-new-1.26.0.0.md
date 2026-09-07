# What's new in this version — v1.26.0.0

> Paste the fenced block into Partner Center → your app → **Store listings** → *What's new in this
> version* — for the English listing AND, translated, for each of the six language listings (the
> per-language blocks live in `whats-new-1.26.0.0-<code>.md`, generated alongside this file).
>
> **Written for someone on the v1.25 Store install.** The headline is the interface in their own
> language. The one mechanism worth teaching is that the picker only lists fully-translated
> languages — the honest-by-construction promise a user actually feels.

---

```
v1.26.0.0

RoRoRo speaks your language now
• The whole interface — menus, settings, tooltips, every
  window — is translated into French, German, Russian,
  Portuguese (Brazil), Polish, and Spanish. If Windows is set
  to one of those, RoRoRo picks it up on its own.

Pick it yourself
• Settings > Appearance has a new Language dropdown. It lists
  only the languages RoRoRo is fully translated into, so you
  never land on a half-translated screen. Your choice takes
  effect the next time you open RoRoRo.

Still English at heart
• English stays the default, and product names — Squad Launch,
  Recycle, RoRoRo itself — read the same in every language.
  Nothing about your accounts, themes, or settings changes.
```

---

## What is deliberately not in this copy

- **Satellite assemblies, ResourceManager, `CurrentUICulture` fallback.** "Picks it up on its
  own" is the same fact in the reader's language; the machinery is not the sell.
- **The six *listing* languages.** This field is read inside the app-language listing the reader
  already chose; the store-listing translations are a separate surface (they went live this
  release too), not something to narrate here.
- **How many strings.** "483 strings translated" is an engineering number, not a customer one —
  "the whole interface" is what they feel.
