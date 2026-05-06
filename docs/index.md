---
title: RORORO
description: Multi-launcher for Windows. Run multiple Roblox clients side by side, signed in as different saved accounts. A 626 Labs product.
---

<section class="hero">
  <p class="hero__eyebrow">A 626 Labs product</p>
  <h1 class="hero__wordmark">RORORO</h1>
  <p class="hero__tagline">Multi-launcher for Windows.</p>
  <p class="hero__lede">Run multiple Roblox clients side by side, signed in as different saved accounts. Free. Open source. Brand-spreads-for-free under 626 Labs.</p>
  <div class="cta-row">
    <a href="https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/rororo-win-Setup.exe" class="btn btn-primary">Download Setup.exe</a>
    <a href="https://github.com/estevanhernandez-stack-ed/ROROROblox/releases" class="btn btn-secondary">All releases</a>
  </div>
  <p class="hero__meta">Windows 11 · .NET 10 LTS · WPF</p>
  <figure class="hero__shot">
    <img src="{{ '/screenshots/main.png' | relative_url }}" alt="RORORO main window — saved accounts list with Launch As buttons" />
  </figure>
</section>

## What it does

Holds the Roblox singleton mutex so additional clients open instead of stealing focus. Uses Roblox's documented authentication-ticket flow for per-account launches. Encrypts saved cookies with the Windows Data Protection API tied to your Windows user — `accounts.dat` copied to another PC won't decrypt.

No DevTools. No registry edits. No telemetry. The Roblox client launches unmodified.

## Install it

[**Download `rororo-win-Setup.exe`**](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/rororo-win-Setup.exe) and double-click. The installer is currently unsigned, so Windows SmartScreen will warn on first run — click **More info** → **Run anyway**. One-time per machine. Future updates roll out automatically.

The Microsoft Store listing is in review. Once it goes live, that path bypasses SmartScreen entirely.

## Surfaces

<div class="surfaces" markdown="0">
  <figure class="surface-card">
    <img src="{{ '/screenshots/welcome.png' | relative_url }}" alt="Welcome to RORORO — three-step onboarding overlay shown on first launch" />
    <figcaption class="surface-card__caption">
      <strong>Welcome</strong>
      <span>Three steps and you're set. Shown on first launch.</span>
    </figcaption>
  </figure>
  <figure class="surface-card">
    <img src="{{ '/screenshots/squad-launch.png' | relative_url }}" alt="Squad Launch — pick accounts, fire them at one game" />
    <figcaption class="surface-card__caption">
      <strong>Squad launch</strong>
      <span>Pick a roster, fire them at one game.</span>
    </figcaption>
  </figure>
  <figure class="surface-card">
    <img src="{{ '/screenshots/add-game.png' | relative_url }}" alt="Add Game — paste a roblox.com URL, name it, save it" />
    <figcaption class="surface-card__caption">
      <strong>Saved games</strong>
      <span>Paste a Roblox URL, name it, route Launch As at it.</span>
    </figcaption>
  </figure>
  <figure class="surface-card">
    <img src="{{ '/screenshots/theme.png' | relative_url }}" alt="Theme builder — palette controls for cyan, magenta, navy, accents" />
    <figcaption class="surface-card__caption">
      <strong>Theme builder</strong>
      <span>Cyan + magenta out of the box. Tune to taste.</span>
    </figcaption>
  </figure>
  <figure class="surface-card">
    <img src="{{ '/screenshots/settings.png' | relative_url }}" alt="Settings — startup, tray, multi-instance default" />
    <figcaption class="surface-card__caption">
      <strong>Settings</strong>
      <span>Run on login, default-on multi-instance, tray behavior.</span>
    </figcaption>
  </figure>
  <figure class="surface-card">
    <img src="{{ '/screenshots/diagnostics.png' | relative_url }}" alt="Diagnostics — bundle for bug reports" />
    <figcaption class="surface-card__caption">
      <strong>Diagnostics</strong>
      <span>One-click bundle if something goes sideways.</span>
    </figcaption>
  </figure>
  <figure class="surface-card">
    <img src="{{ '/screenshots/about.png' | relative_url }}" alt="About box — version, build, 626 Labs attribution" />
    <figcaption class="surface-card__caption">
      <strong>About</strong>
      <span>Version, build, attribution. Imagine Something Else.</span>
    </figcaption>
  </figure>
</div>

## Links

- **Source:** [github.com/estevanhernandez-stack-ed/ROROROblox](https://github.com/estevanhernandez-stack-ed/ROROROblox)
- **Privacy policy:** [PRIVACY](./PRIVACY/)
- **Report an issue:** [GitHub Issues](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues/new)

<aside class="trademark" markdown="1">
**Trademark notice.** "Roblox" and the Roblox logo are trademarks of Roblox Corporation. RORORO is an independent third-party tool, **not affiliated with, endorsed by, or sponsored by Roblox Corporation**. The trademarked term is used solely to describe compatibility with the Roblox platform. RORORO launches the official Roblox client unmodified.
</aside>
