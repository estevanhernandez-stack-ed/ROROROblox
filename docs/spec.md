# ROROROblox — Technical Spec (pointer stub)

Spec-first Cart cycle. Canonical spec lives upstream:

→ [docs/superpowers/specs/2026-05-03-rororoblox-design.md](superpowers/specs/2026-05-03-rororoblox-design.md)

## Section index (for checklist references)

- §1 Overview
- §2 Goals and non-goals
- §3 Stack (locked: .NET 10 LTS, WPF, WPF-UI, Hardcodet NotifyIcon, CsWin32, WebView2, DPAPI, MSIX, Velopack, xUnit)
- §4 Architecture
- §5 Components & interfaces
  - §5.1 IMutexHolder
  - §5.2 ITrayService
  - §5.3 MainWindow + MainViewModel
  - §5.4 IAccountStore
  - §5.5 ICookieCapture
  - §5.6 IRobloxLauncher
  - §5.7 IRobloxApi
  - §5.8 App / AppLifecycle
- §6 Data flows
  - §6.1 Add Account
  - §6.2 Launch As
- §7 Error handling (six buckets)
  - §7.1 Roblox shipped a breaking update
  - §7.2 Cookie expired or invalidated
  - §7.3 WebView2 runtime missing or broken
  - §7.4 DPAPI decrypt fails
  - §7.5 Roblox not installed / multiple Roblox installs
  - §7.6 Distribution friction
- §8 Testing
- §9 Distribution
- §10 Open items (mandatory spike, deferred)
- §11 Decisions log
- Appendix A — Reference impl (MultiBloxy provenance)
