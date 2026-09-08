# Notes for certification — reviewer letter (v1.27.0.0)

> Paste the block between the `---` markers below into Partner Center → your app → **Submission
> options** → **Notes for certification**.
>
> **This is a two-version delta, and that is the first thing the letter says.** Certification last
> saw v1.27's predecessor-but-one: **v1.25.0.0**. v1.26.0.0 was built and shipped to our
> direct-download channel but was deliberately never submitted to the Store — we held it because
> its localization was incomplete — so the manifest change it introduced (six additional declared
> languages) reaches certification for the first time here. Stating that up front avoids a
> surprise at validation, the same way v1.26's draft letter front-loaded the manifest delta.
>
> The disclosure surface is otherwise unchanged: no capability, network, or data-handling change
> since v1.25.
>
> Sources: `docs/store/release-notes-1.27.0.0.md`, `docs/store/localization-plan.md`.

---

```
Hello reviewer,

Thank you for your time on v1.27.0.0. Certification last saw
v1.25.0.0. Version 1.26.0.0 was released only to our
direct-download channel and was never submitted to the Store -
we held it back because its localization was incomplete - so
this submission carries the changes of two versions. The one
change worth disclosing is in the package manifest, and it
arrived in that unsubmitted version.

1. MANIFEST: SIX ADDITIONAL DECLARED LANGUAGES. The <Resources>
   section now declares fr, de, ru, pt-BR, pl, and es in
   addition to en-us (still the default). Each declared language
   is backed by a complete .NET satellite resource assembly that
   ships inside this package - the application's user interface
   is fully translated into all six, including the messages the
   app composes at runtime, not only its static screens. The
   in-app language picker (Settings > Appearance) only offers a
   language whose catalog is present, so a declared language is
   always a rendered language.

2. NO CAPABILITY CHANGE. Same capabilities as v1.25
   (runFullTrust only), same protocol declarations, same
   startup task.

3. NO NETWORK OR DATA CHANGE. Localization is resource data
   compiled into the app. Nothing new is transmitted, collected,
   or stored. Network behaviour remains limited to Roblox-owned
   endpoints and GitHub Releases (update checks and the signed
   compatibility feed), with the app's own User-Agent. The
   optional phone and Discord alert integrations from v1.25 are
   unchanged and remain off until a user configures them.

Credential handling is unchanged: session cookies remain
DPAPI-encrypted, local-only, and never exposed to plugins.

The trademark position is unchanged from prior certifications:
RoRoRo is an independent tool, not affiliated with Roblox
Corporation, and the disclaimer appears on the Store
description, the About box, and the privacy policy. Product and
service names (Roblox, Squad Launch, Recycle, Discord, Pushover,
ntfy) are deliberately left untranslated across all languages.

Thank you,
626 Labs LLC
```
