# RORORO — plugin-reported activity design (GetAccountActivity crediting fix)

---
**Date:** 2026-07-09
**Status:** Draft — ready for Este review, then plan
**Scope:** Fix the v1.8 `GetAccountActivity` crediting blind spot: input synthesized by a plugin (ur-AFK's keep-alive Space) is invisible to the host's 1s foreground-at-tick sampling, so the host keeps reporting the account idle and the plugin re-fires. Add a consent-tied host RPC for plugins to report the activity they themselves create.
**Origin:** 2026-07-07 plugin host-support audit finding #2 (live-observed by ur-AFK, which ships a client-side workaround + regression suite). **Code-verified 2026-07-09:** `ActivityMonitor.Sample()` credits `advanced` input to the account foreground AT SAMPLE TIME (`src/ROROROblox.Core/Diagnostics/ActivityMonitor.cs:96-106`); a sub-second focus→input→focus-back window between ticks credits the wrong account by construction.
---

## 1. Problem

`ActivityMonitor` polls every ~1s: if the system input tick advanced since last sample AND account X is foreground now, stamp X active. ur-AFK's grab (focus flagged window → Space → restore focus) completes between ticks — the input advance is then attributed to whatever regained foreground, not the account that received the Space. The flagged account keeps aging → threshold re-crossing → ur-AFK re-fires → repeat. Poll-resolution can't fix this: no sampling rate reliably catches a deliberately-brief focus window.

## 2. Design

1. **New host RPC: `ReportAccountActivity(AccountActivityReport) → Empty`** — a plugin tells the host "I just generated input for account X at time T." Host stamps that account's `LastActivityAt` (clamped: `T` must be within the last few seconds; otherwise use host-now — no back-dating, no future-dating).
2. **Capability gating: reuse `system.synthesize-keyboard-input`.** A plugin the user trusted to INJECT input is trusted to report that injection; a separate consent line would be noise. The interceptor requires that capability for this RPC. (No new consent-sheet entry.)
3. **Contract:** additive — new message + RPC in the proto; `ROROROblox.PluginContract` bumps 0.3.x → 0.4.0; Handshake `contract_version` stays "1.0" (same posture as the v1.8 additions). Older hosts: plugin degrades gracefully (call fails PERMISSION_DENIED/unimplemented → ur-AFK keeps its existing workaround as fallback).
4. **Host plumbing:** `PluginHostService` handler → resolves the report's account (by the same account-id/userId convention the existing plugin surface uses — match `GetAccountActivity`'s identifier) → `ActivityMonitor.ReportExternalActivity(accountId, at)` (new method: lock-free single write to the record's `LastActivityAt` + re-arm the warn latch, mirroring what a foreground stamp does — reuse the record mutation `Sample()` performs so the two