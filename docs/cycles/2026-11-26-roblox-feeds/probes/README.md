# Probes, 2026-11-26

Recorded answers and measurements behind `../spec.md`. The JSON files are byte-for-byte what the server
returned. Do not edit them; the status evaluator's tests take their watch table as a parameter so that
no payload has to be changed to fit.

## Recorded status answers

| File | Source | Captured | Why it is here |
|---|---|---|---|
| `status-roblox-operational-2026-11-26.json` | `https://api.status.io/1.0/status/59db90dbcdeb2f04dadcf16d` (Roblox's page), read live | 2026-11-26 17:57 UTC | Every row at 100. Source of the five watched (group id, row id) pairs. |
| `status-otherpage-incident-2022-03-28.json` | Wayback Machine, `web.archive.org/web/20220328131125id_/https://api.status.io/1.0/status/5980813dd537a2a7050004bd` (another company's page) | 2022-03-28 | One row id (`5982d729f6278a3b05000618`) at 400 under one group and 300 under another. Two open incidents. Eleven scheduled maintenances that change no row code. |
| `status-otherpage-disruption-maintenance-2022-11-29.json` | Wayback Machine, `…/20221129143640id_/…/5980813dd537a2a7050004bd` | 2022-11-29 | A 500 and a maintenance under way (200) in one answer. |
| `status-otherpage-degraded-2026-11-06.json` | Wayback Machine, `…/20261106064743id_/https://api.status.io/1.0/status/526712bf10cd4e587b000014` (a third page) | 2026-11-06 | Eight degraded rows; incidents carry a `current_active` field that did not exist in 2022. |

No recorded incident answer for Roblox's own page was found. The Wayback Machine holds two captures of
it (2026-10-28 and 2026-11-15), both Operational, and a GitHub code search for the page id returned
thirty files with no payload among them. A further archived answer (2023-05-29, not saved) has two open
incidents while every row reads 100, which is why the line reads row codes and ignores incidents.

## Measurements

| Question | Answer | How |
|---|---|---|
| Status levels | 100 Operational, 200 maintenance, 300 Degraded Performance, 400 Partial Service Disruption, 500 Service Disruption, 600 Security Event | `kb.status.io/developers/status-codes/` |
| Status.io terms | Nothing forbids reading a public status answer. No rate limit stated for the public status API. Answer served through CloudFront (`Age` 19 s and 120 s on two reads). | `status.io/tos` (effective 2016-03-10), `kb.status.io/developers/public-status-api/`, response headers |
| Games per pulse request | 50. 51 answers 400, code 9, "Too many universe IDs were requested." A missing id is absent from the answer. | `games.roblox.com/v1/games?universeIds=` with 10, 50, 51, 100, 101, 150, 200 ids |
| What "updated" looks like | 5.7 h (Doors) to 17 days (Tower of Hell) across eleven popular games; nine of eleven inside four days. What moves the date (publish, settings, both) is not established. | one batched read |
| Per-version release notes | `create.roblox.com/docs/release-notes/release-notes-748` answers 200; `-749` and `-760` answer 404; the index answers 200. | HEAD-style reads |
| Notes versus builds | First WindowsPlayer build of 741 to 748 on 9/29, 10/5, 10/12, 10/19, 10/26, 11/3, 11/9, 11/16. DevForum "Release Notes for N" posted 9/30, 10/8, 10/15, 10/22, 10/27, 11/5, 11/10, 11/17. Notes trail the build by one to three days, eight of eight. | `setup.rbxcdn.com/DeployHistory.txt` (tail), `devforum.roblox.com/c/updates/release-notes/62.rss` |
| Live client version | `0.748.0.7481526` | `clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer` |
| The hold line on Neb | Not answered. The share refused the read from this machine. | see `../spec.md` §20 for the one command |
