# RORORO — themed window chrome design

---
**Date:** 2026-07-09
**Status:** Approved-shape (queue spec — build whenever) — mini-spec, ready for a plan
**Author:** The Architect + Este
**Scope:** Replace the stock Windows title bar with WPF-UI's themed chrome so the app's top bar matches the rest of the UI (and the Ur Task plugin, which already does this). First-pixel polish.
**Origin:** Este, during the 2026-07-09 smoke — "the main app's top bar isn't themed as well as Ur Task's."
---

## 1. Problem

`MainWindow` (and the modal windows) ride the OS default title bar — grey/white system chrome that ignores the app theme and the 626 palette. Ur Task uses `WindowStyle="None"` on every window with fully custom, themed chrome, so its top bar reads first-party; RoRoRo's does not. The gap is the very first thing a user sees.

## 2. Design decisions

1. **Use WPF-UI's `FluentWindow` + `TitleBar`, not hand-rolled `WindowStyle="None"`.** The app already ships **WPF-UI 4.3.0** (`ui:ControlsDictionary` merged in `App.xaml`). `FluentWindow` + the `TitleBar` control give themed chrome, drag regions, snap-layout/Aero-snap support, and working min/max/close caption buttons **without** re-implementing hit-testing, DWM, or window-command plumbing (the classic footguns of raw `WindowStyle="None"`). Ur Task hand-rolled because it predates this; RoRoRo should use the library it already depends on.
2. **Scope: the main window first, then the modals.** `MainWindow` is the payoff. The ~10 modal/tool windows (Library/Settings, Squad Launch, Friends, Preferences, About, the gate modals, Groups when it lands) convert for consistency in the same cycle — but the plan sequences MainWindow as task 1 so the visible win lands early and each modal is an independent, low-risk conversion.
3. **Preserve every existing behavior:** custom tray-icon integration (`Hardcodet.NotifyIcon`), the "X minimizes to tray / quit from tray" semantic (App-level, already in the status bar copy), `WindowStartupLocation`, per-window sizing/min-size, the theme-brush backgrounds, and the existing in-window content layouts (only the chrome changes, not the client area).
4. **TitleBar carries the brand:** app icon (the teal mark from #53 once merged) + "RORORO" wordmark in the title bar, using theme tokens. Caption buttons themed. No custom buttons in v1 beyond what's there (tray behavior unchanged).
5. **Theme-reactivity:** the chrome recolors with the app's theme like everything else (that's the whole point) — verified against a custom theme in smoke.

## 3. Non-goals

- No raw `WindowStyle="None"` re-implementation (the library handles it).
- No new window behaviors (no custom caption menus, no tabs, no acrylic/mica experiments) — pure chrome parity, not a redesign.
- No change to the tray-minimize model, the single-instance guard, or startup flow.
- No plugin-window changes (plugins own their own chrome).

## 4. Architecture

- Convert `MainWindow` (and each modal) from `<Window>` to `<ui:FluentWindow>` (WPF-UI namespace already available via the merged dictionary; add the `xmlns:ui` if a window lacks it). Add a `<ui:TitleBar>` as the first child with the app icon + title + themed caption buttons; move the existing root content beneath it.
- Code-behind: `FluentWindow` derives from `Window`, so `x:ClassModifier`, event handlers, `DialogResult`, `Owner`, and `ShowDialog` all carry over. Verify each window's code-behind base type reference (if any explicitly names `Window`, it still works — `FluentWindow : Window`).
- Tray interop: confirm `Hardcodet.NotifyIcon`'s window-handle hooks and the minimize-to-tray override still fire under `FluentWindow` (they should — same HWND lifecycle; verify the min/close intercept).
- Per-window: keep `Background="{DynamicResource BgBrush}"`, existing `Height/Width/MinHeight/MinWidth`, `WindowStartupLocation`.

## 5. Edge cases

- **Modal `ShowDialog` + `DialogResult`** — unchanged under `FluentWindow` (still a `Window`). Verify the gate modals (which set `DialogResult` from callbacks) still close correctly.
- **The always-on-top / owner relationships** (tray-painted windows, the floating pieces) — verify z-order + owner behavior post-conversion.
- **DPI / snap layouts** — WPF-UI TitleBar handles snap; smoke on a multi-monitor / high-DPI setup.
- **Theme switch while open** — chrome must recolor live (DynamicResource).
- **The tray "X minimizes" intercept** — must still fire from the TitleBar close button, not just the OS one.

## 6. Testing

- No unit tests (pure XAML chrome). Verified by build + manual smoke.
- **Manual smoke:** main window opens with themed chrome + brand; min/max/close work; **X minimizes to tray, quit-from-tray still exits**; snap layouts work (hover maximize); theme switch recolors the bar live; each converted modal opens/closes/returns DialogResult correctly; multi-monitor + high-DPI sanity.

## 7. What ships

`MainWindow` + each modal converted to `FluentWindow` + `TitleBar` (brand + themed captions); tray-minimize intercept verified; smoke checklist. Own cycle (~1 task for MainWindow + 1 per modal batch, ~4-5 tasks total). Sequence anytime — independent of the launch-pipeline work. Best paired AFTER #53 (teal icons) merges so the title-bar icon is the new mark.
