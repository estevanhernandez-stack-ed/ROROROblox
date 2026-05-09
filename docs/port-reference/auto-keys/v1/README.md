# auto-keys

> macOS auto-keys cycler — focus-then-fire serial keystroke driver across multiple windowed apps. Engine + UI + tests + canonical ADR, captured as a port reference for the Windows / WPF sibling.

## What it is

Serial multi-window keystroke cycler that defeats Roblox's ~20-minute AFK timeout across N concurrent accounts. The engine walks each running window in turn — focus the window, fire the configured keystroke sequence, settle, advance — instead of posting events concurrently to backgrounded PIDs. The serial-cycle model trades "always active" for reliability: macOS's deterministic frontmost-window routing means every keystroke lands in the right place.

Captured from RORORO Mac (the SwiftUI / native-Apple-API multi-Roblox launcher) at Slope C wave 3c. The bundle includes 13 domain files (the engine), 5 SwiftUI views (UI reference), 5 XCTest files (behavior spec), and ADR 0004 + implementation checklist (canonical design rationale). **Read the ADR first when porting.**

## When to reach for it

Reach for this when you're building a cross-window auto-input feature — most directly, the RORORO Windows (C# / WPF) sibling — and want a tested, ethics-welded reference design instead of starting from scratch. The engine logic ports as-is in shape (state machine, budget guard, kill-key gesture, engagement-pause); the OS-specific glue rewrites cleanly to Win32 (`SendInput`, `SetForegroundWindow` + `AttachThreadInput`, `SetWindowsHookEx`, `SetThreadExecutionState`).

Don't reach for this if you want a generic macro recorder. This is purpose-built for a multi-window AFK defeater — the budget guard, the focus-then-fire serial cycle, and the kill-key escape hatch are load-bearing for *that* use case.

## Plant

```
/vibe-taker:plant auto-keys
```

To pin a specific version:

```
/vibe-taker:plant auto-keys --version=v1
```

## Reference

The verbatim source-of-truth files live under [`reference/`](./reference/). The contract surface is in [`contract.json`](./contract.json); the why and the gotchas are in [`notes.md`](./notes.md). The canonical design rationale is in [`reference/docs/0004-auto-keys-cycler.md`](./reference/docs/0004-auto-keys-cycler.md).

---

<sub>Captured 2026-05-09T16:17:59Z from `git@github.com:estevanhernandez-stack-ed/rororo-mac.git` (`App/RORORO/Domain/AutoKeys/` + UI + tests + docs) — vibe-taker bundle v1.0.</sub>
