# Smoke checklist — the entire startup gate

**Branch:** `fix/startup-start-anyway` (PR #59) · **Design:** [`../specs/2026-07-09-startup-start-anyway-design.md`](../specs/2026-07-09-startup-start-anyway-design.md) + the #32 tray-residence gate

Covers every path of `StartupGate` + the BLOCKED modal + the runtime contested banner. The decision invariant is unit-tested; this is the live human pass. **Scenario D (Start anyway) is the load-bearing new check** — the rest is regression coverage that #59 must not have broken.

## The dev build under test

Run from the repo root (relative path):

```
.\src\ROROROblox.App\bin\Debug\net10.0-windows\ROROROblox.App.exe
```

## Helper commands (PowerShell)

```powershell
# Roblox clients running (safe to keep — the gate never needs to touch these unless you ask)
(Get-Process RobloxPlayerBeta -ErrorAction SilentlyContinue | Measure-Object).Count
# RoRoRo instances running
Get-Process ROROROblox.App -ErrorAction SilentlyContinue | Select-Object Id,Path
# Reset: stop ALL RoRoRo (releases the mutex; already-launched Roblox clients survive)
Stop-Process -Name ROROROblox.App -Force
```

Reset to a clean slate between scenarios: `Stop-Process -Name ROROROblox.App -Force`, and quit Roblox from its tray if a scenario left it running.

---

## A. Clean start (mutex free, no leftovers)

- [ ] No Roblox running, no other RoRoRo. Launch the dev build.
- [ ] **Expected:** MainWindow opens directly (no modal). Tray tooltip reads **Multi-Instance: ON ✓**, icon = tray-on.

## B. Leftover Roblox processes (informational, not blocking)

- [ ] From a clean RoRoRo, Launch As 1–2 accounts (Roblox clients come up).
- [ ] Quit RoRoRo (`Stop-Process -Name ROROROblox.App -Force`). The Roblox clients stay running — they're now "leftovers."
- [ ] Relaunch the dev build.
- [ ] **Expected:** the **Leftover processes** window appears (RoRoRo *did* acquire the mutex — this is informational, not the BLOCKED modal). It reports the count of windowless + windowed Roblox processes. Choosing clean-up stops them; declining proceeds anyway. Either way RoRoRo starts and tray reads **ON**.

## C. BLOCKED — the holder is Roblox (the #32 tray-residence case)

Setup: quit all RoRoRo. Start **Roblox directly** (desktop/Store, not via RoRoRo) so it owns its own singleton. Confirm no RoRoRo is running.

- [ ] **C1 — Close Roblox for me.** Launch the dev build → the **"Roblox is already running"** modal appears. Click **Close Roblox for me**. **Expected:** Roblox is closed, RoRoRo re-acquires, the modal closes, MainWindow opens, tray **ON**.
- [ ] **C2 — Retry after manual quit.** Repeat setup. Launch dev build → modal. Quit Roblox yourself (tray → Quit), then click **Retry**. **Expected:** re-acquires, proceeds, tray **ON**.
- [ ] **C3 — Retry while still held.** Repeat setup. Launch dev build → modal. Click **Retry** WITHOUT closing Roblox. **Expected:** the amber **"Still locked — Roblox is still running."** tick appears; the modal stays open (no crash, no proceed).

## D. BLOCKED — the holder is another RoRoRo (the new Start anyway path) ← load-bearing

Setup: launch your **installed** RoRoRo first (it clean-starts and holds the mutex, tray ON). Leave it running. Then launch the **dev build** — it comes up as a second instance and can't acquire.

- [ ] **D1 — Start anyway.** On the dev build's BLOCKED modal, click **Start anyway**. **Expected:** the dev build starts (MainWindow opens) WITHOUT owning the lock. Tray reads **Multi-Instance: OFF** (honest — it doesn't hold it, and it's *not* an error). Within ~5s the contested **banner** appears: *"Roblox has the multi-instance lock — it's probably running in your system tray."*
- [ ] **D2 — Multi-instance still works borrowed.** From that borrowed dev build, Launch As an account. **Expected:** the account launches into its own client (multi-instance works because the installed instance is squatting the mutex).
- [ ] **D3 — Quit.** Repeat setup. On the BLOCKED modal click **Quit RoRoRo**. **Expected:** the dev build exits cleanly (no MainWindow, no partial startup).
- [ ] **D4 — Fail-closed close.** Repeat setup. Close the BLOCKED modal via the title-bar **X** (and, separately, via **Esc**). **Expected:** both are treated as Quit — the dev build exits, never starts half-initialized.

## E. Runtime contested banner + its actions (rides on D1)

With the borrowed dev build from D1 still up and showing the banner:

- [ ] **E1 — Banner recovery: Retry.** Quit the installed RoRoRo (`Stop-Process`), then use the banner's **Retry** action. **Expected:** the dev build now acquires the freed mutex, the banner clears, tray flips to **ON**.
- [ ] **E2 — Banner recovery: Close Roblox for me** (optional, only if a real tray-Roblox is the holder) behaves as in C1 from the running app.

## Result

- [ ] A–E all pass, especially **D1/D2** (Start anyway borrowed start + multi-instance still works). Anything off → note the scenario letter and what you saw.

**Not covered here (out of scope / hard to force manually):** the `MutexLost` watchdog path (tray flips to ERROR when an external force invalidates a held handle) — it has unit coverage; forcing it live is unreliable.
