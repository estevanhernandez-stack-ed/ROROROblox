# Hosted privacy page: RoRoRo 1.41.0.0

For the hub repo (`626Labs-LLC/626Labs-LLC.github.io`, `privacy.html`, the section under
`<h3 id="privacy-rororo">`). Apply the text unchanged. Source: `docs/PRIVACY.md` on `main` after PR #304
(`git diff v1.40.0.0..v1.41.0.0 -- docs/PRIVACY.md`).

**Why 1.41 needs one.** 1.41 adds a third party, Status.io, and two new reads: `games.roblox.com` for
game activity, and the Roblox client-version check moved from batch launches only to a timer while the
main window is open. Nothing new is stored, so only two of the five paragraphs change.

Checked against the live hosted section on 2026-11-27. It carries everything through 1.40 and nothing
from 1.41.

## 1. What touches the network: add one passage

In the **What touches the network** paragraph, insert the passage below immediately after the sentence
that ends `a file that fails the check is not used.` and before `On Microsoft Store installs, since 1.40`:

```html
Since 1.41, three more reads run while the main window is open, and never while RoRoRo is in the tray. RoRoRo checks Roblox's current client version at <code>clientsettingscdn.roblox.com</code> about every 30 minutes, so the main window can say when Roblox has an update waiting. While the Library or an account panel is open, it asks <code>games.roblox.com</code> how many people are in the saved games on screen and when each last changed, with game ids only and no cookie or account. And it asks Status.io, a third-party service that hosts Roblox's public status page, whether Roblox reports a problem: first 30 seconds after the window is shown, then about every ten minutes. The Status.io read sends no cookie, no account and no identifier beyond the address any web request carries, and nothing it returns is shown, saved or logged; it only chooses which of RoRoRo's own sentences appears. One switch in Settings, <strong>Show Roblox's status and game activity</strong>, turns off the Status.io and game-activity reads.
```

## 2. Third parties: add one sentence

In the **Third parties** paragraph, insert after the sentence that ends `for the update question Windows
asks on the app's behalf.`:

```html
Status.io, since 1.41, for the public Roblox status read described above, under its own policy; it is not Roblox, GitHub or Microsoft.
```

## Unchanged

**What is collected**, **Where data lives** and **Removal**: 1.41 writes no new file and collects nothing.

## Deploy check

After the hub merges and Pages deploys:

```sh
curl -s https://626labs.dev/privacy.html | grep -o 'Status\.io' | wc -l
```

Expect **3** or more (two in the network passage, one in Third parties). Before the change it is 0.
Use `grep -o ... | wc -l`, not `grep -c`, which counts lines.

## Which URL the Store reads

The Store listing's privacy URL is the repo page,
`https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/` (`listing-copy.md`). It renders
`docs/PRIVACY.md` from `main`, so it carries 1.41's text as soon as #304 merges. The hub page is the
626 Labs-wide policy, and this handoff keeps it in step. It does not gate the Store submission.
