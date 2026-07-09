# Smoke checklist — launch-to-home + optional default game

**Branch:** `feat/launch-to-home` · **Spec:** [`../specs/2026-07-09-launch-to-home-base-design.md`](../specs/2026-07-09-launch-to-home-base-design.md)

Store/resolution/URI logic is unit-tested; the live `launchmode:app` launch and the WPF surfaces are the human pass. **`launchmode:app` is a Roblox-contract surface RoRoRo hasn't shipped — the live launch is the load-bearing check.**

## Setup
- [ ] Quit the installed RoRoRo (single-instance); run the dev build; 1+ saved account, 1+ saved game.

## The load-bearing check — launchmode:app
- [ ] **1. Clear the default game** (Library → a game's **Clear default**) → the widget reads **"Roblox home"** (tooltip explains). Then **Launch As** an account → **the client opens signed in, at Roblox home, joining no game.** (This is the Roblox-contract check — if the app opens to home authenticated, `launchmode:app` works.)
- [ ] **2. Fresh state** (no games saved at all) → Launch As → same: Roblox home, signed in, no hard-fail, no prompt.

## Default game still works
- [ ] **3. Set a default game** → widget shows its name → Launch As lands straight in that game (unchanged).
- [ ] **4. Clear default toggling:** Set → Clear → the game row's Set/Clear buttons flip correctly (exactly one visible); the DEFAULT badge tracks.

## No-promotion + honesty
- [ ] **5. Remove the default game** with other games saved → default becomes **none** (no other game silently promoted); widget flips to "Roblox home".
- [ ] **6. Game default vs server default independent** (with #54): a default private server still pre-selects in Squad Launch; the game default/home change doesn't touch it.
- [ ] **7. Theming:** the Clear-default button + widget copy recolor under a custom theme.

## Result
- [ ] All pass (esp. #1 live launchmode:app) → merge-ready. Anything off → note the check + what you saw.
