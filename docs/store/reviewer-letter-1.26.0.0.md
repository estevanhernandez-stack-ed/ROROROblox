# Notes for certification — reviewer letter (v1.26.0.0)

> Paste the block between the `---` markers below into Partner Center → your app → **Submission
> options** → **Notes for certification**.
>
> **The delta this release is a manifest one, and a benign one:** the package now declares six
> additional languages (`<Resource Language>`), each backed by a complete satellite resource
> catalog that actually ships — declared languages match rendered languages. No capability,
> network, or data-handling change. After v1.25's "manifest unchanged" letter, the manifest delta
> is stated up front so it is not a surprise at validation.
>
> Sources: `docs/store/release-notes-1.26.0.0.md`, `docs/store/localization-plan.md`.

---

```
Hello reviewer,

Thank you for your time on v1.26.0.0. Certification last saw
v1.25.0.0; this is a single-version update. This release is
localization only, and the one change worth disclosing is in
the package manifest.

1. MANIFEST: SIX ADDITIONAL DECLARED LANGUAGES. The <Resources>
   section now declares fr, de, ru, pt-BR, pl, and es in
   addition to en-us (still the default). Each declared language
   is backed by a complete .NET satellite resource assembly that
   ships inside this package - the app's user interface is fully
   translated into all six, not partially. The in-app language
   picker (Settings > Appearance) only offers a language whose
   catalog is present, so a declared language is always a
   rendered language.

2. NO CAPABILITY CHANGE. Same capabilities as v1.25
   (runFullTrust only), same protocols, same startup task.

3. NO NETWORK OR DATA CHANGE. Localization is resource data
   compiled into the app. Nothing new is transmitted, collected,
   or stored. Network behaviour remains limited to Roblox-owned
   endpoints and GitHub Releases (update checks and the signed
   compatibility feed), with the app's own User-Agent. The
   optional phone/Discord alert integrations from v1.25 are
   unchanged and still off until the user configures them.

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
