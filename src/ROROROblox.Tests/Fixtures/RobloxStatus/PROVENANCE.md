# RobloxStatus fixtures

Recorded Status.io answers, byte for byte what the server returned. Never edited: the evaluator takes
its watch table as a parameter so no payload is changed to fit. `RobloxStatusFixtureTests` compares each
file with its original under `docs/cycles/2026-11-26-roblox-feeds/probes/`.

| File | Source | Captured |
|---|---|---|
| `status-roblox-operational-2026-11-26.json` | `https://api.status.io/1.0/status/59db90dbcdeb2f04dadcf16d` (Roblox's page), read live | 2026-11-26 17:57 UTC |
| `status-otherpage-incident-2022-03-28.json` | Wayback Machine `20220328131125id_`, page `5980813dd537a2a7050004bd` (another company's page) | 2022-03-28 |
| `status-otherpage-disruption-maintenance-2022-11-29.json` | Wayback Machine `20221129143640id_`, same page | 2022-11-29 |
| `status-otherpage-degraded-2026-11-06.json` | Wayback Machine `20261106064743id_`, page `526712bf10cd4e587b000014` (a third page) | 2026-11-06 |

No recorded incident answer for Roblox's own page exists. A hand-written file is named `synthetic-*` and
exists only for what no real answer can show: a missing `status_code`, an oversized body, a non-hex id.
