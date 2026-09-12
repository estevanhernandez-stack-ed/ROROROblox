# Notes for certification — reviewer letter (v1.28.0.0)

> Paste the block between the `---` markers below into Partner Center → your app → **Submission
> options** → **Notes for certification**.
>
> **A single-version delta, unlike v1.27's.** Certification last saw v1.27.0.0 and it published, so
> this letter covers one version rather than two.
>
> **The one thing worth disclosing is the new plugin RPC**, and the letter leads with it because a
> reviewer reading "the app accepts reported metrics" could reasonably wonder what is being
> collected and by whom. The answer is favourable and specific: the shipped binary gathers nothing,
> contacts no new endpoint, and the capability is declined by default until a user grants it on the
> consent sheet.
>
> **Every claim below was verified against the tree before writing, not assumed:**
>
> - `Package.appxmanifest` is **byte-identical** to v1.27.0.0 — `git diff v1.27.0.0..main` on that
>   file returns nothing. So: no capability change, no new declared language, no new protocol.
> - Declared capabilities are `runFullTrust` and nothing else.
> - The only hosts appearing in code changed since v1.27.0.0 are `ntfy.sh`, `pushover.net` and
>   `github.com` — all three pre-existing, disclosed at v1.25 and v1.4 respectively.
> - `NoVendorNameFenceTests` passes on this tree: no game-vendor hostname or company name appears
>   in Core, App or PluginContract.
>
> Sources: `docs/store/release-notes-1.28.0.0.md`,
> `docs/superpowers/specs/2026-09-09-external-metric-alerts-design.md`.

---

```
Hello reviewer,

Thank you for your time on v1.28.0.0. Certification last saw
v1.27.0.0, which published, so this submission is a single
version's delta. One change is worth disclosing in detail.

1. A NEW PLUGIN CALL THAT ACCEPTS A NUMBER. RoRoRo has had a
   consent-gated plugin system since v1.4: plugins run as
   separate processes and talk to the app over a local named
   pipe, never in-process. This version adds one call to that
   interface. A plugin may report a single number it gathered
   itself - a value, an identifier for what it refers to, and
   when it was observed.

   What the application does with it is the whole point: it
   keeps a short local history, compares it against thresholds
   the USER configured in a local file, and may raise a
   notification. It never forwards the number anywhere.

   The shipped application gathers no such number and contacts
   no service to obtain one. It has no endpoint, no field path
   and no vendor name for any third-party data source compiled
   into it, and an automated test in the build fails if one
   ever appears. Whether any number is ever reported depends
   entirely on whether the user chooses to install a plugin
   that does so.

   The capability is declined unless granted. A plugin must
   declare it, and the user sees it on the consent sheet at
   install time and can refuse it there. Absence from a
   plugin's manifest is treated exactly as a refusal.

2. NO CAPABILITY OR MANIFEST CHANGE. The package manifest is
   unchanged from v1.27.0.0 - same runFullTrust capability,
   same declared languages, same protocol declarations, same
   startup task.

3. NO NEW NETWORK DESTINATION. Nothing in this version
   introduces a new host. Network behaviour remains limited to
   Roblox-owned endpoints, GitHub Releases for update checks
   and the signed compatibility feed, and - only when a user
   has configured them - the Discord webhook and phone push
   services disclosed at v1.25. A metric notification travels
   the same configured destinations as every other alert; it
   adds no new one.

4. QR CODE FOR PHONE SETUP, GENERATED LOCALLY. Connecting the
   ntfy push service required typing a 33-character topic on a
   phone keyboard. The app now renders that same topic as a QR
   code so a camera can read it. The image is generated on the
   device from a string the app already holds; nothing is
   uploaded to produce it and no image service is contacted.
   Because the topic is a credential, the code is hidden by
   default and removed from the screen entirely while streamer
   mode is on.

5. NO CHANGE TO INPUT HANDLING. The application continues to
   synthesize no keyboard or mouse input and to inject nothing
   into any other process. That boundary is unchanged and is
   enforced in the shipped binary.

Credential handling is unchanged: session cookies remain
DPAPI-encrypted, local-only, and never exposed to plugins.
This version strengthens the handling of the user's own
notification credentials - a revealed webhook or push key is
now re-hidden when streamer mode is switched on, and revealing
one while it is on asks first.

The trademark position is unchanged from prior
certifications: RoRoRo is an independent tool, not affiliated
with Roblox Corporation, and the disclaimer appears on the
Store description, the About box, and the privacy policy.
Product and service names (Roblox, Squad Launch, Recycle,
Discord, Pushover, ntfy) are deliberately left untranslated
across all languages.

Thank you,
626 Labs LLC
```
