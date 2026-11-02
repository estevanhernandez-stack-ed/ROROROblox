# EndpointProbe

Records what Roblox answers to the three requests the v1.36 account-library cycle depends on, so the
measurements at checkpoint C1 are the client's own output and never a hand-typed request with a cookie in a
shell history.

For one saved account it sends, in the client's own shape (same endpoints, the same CSRF dance,
`UseCookies=false`, `RORORO/<version>` user agent):

1. `POST auth.roblox.com/v1/authentication-ticket` (handshake, real answer, and one rotated-token retry)
2. `POST presence.roblox.com/v1/presence/users`
3. `GET premiumfeatures.roblox.com/v1/users/{id}/validate-membership`

and writes a redacted markdown report: status, every `rblx-*` response header and the body for each, under
`docs/cycles/2026-11-01-account-library/probes/`.

```powershell
dotnet run --project tools/EndpointProbe -- --list                          # local names and ids, nothing else
dotnet run --project tools/EndpointProbe -- --account "<name|id>" --live    # the real requests (C1, deliberately)
dotnet run --project tools/EndpointProbe -- --account "<name|id>" --offline # canned responses, no network
dotnet run --project tools/EndpointProbe -- --selftest                      # fake vault + offline run + leak check
```

Other flags: `--data-dir <path>` (folder holding `accounts.dat`; default `%LOCALAPPDATA%\ROROROblox`) and
`--out <dir>`.

## What it will not do

- **Print a cookie, under any flag.** There is no such flag. The cookie goes from the vault into a request
  header and nowhere else; the report is built from records that have no field to hold it.
- **Write the vault.** `--list` and the probe modes only read `accounts.dat`. It is safe to run while RoRoRo is
  open. (`--selftest` builds and deletes its own temp vault.)
- **Touch the network without `--live`.** `--offline` and `--selftest` hand the prober a stub handler instead of a
  socket.
- **Write** the request `Cookie`, any `Set-Cookie`, any `x-csrf-token` value or the `RBX-Authentication-Ticket`
  value. `rblx-challenge-metadata` is base64-decoded and every bare number of five digits or more is replaced.
  The account is named by the first 8 characters of its local id, never its Roblox name (the reports go to a public repo; `--list` maps the two locally). Its Roblox user id is `<uid>` and a server id (`gameId`, `jobId`) is `<gameId>` everywhere.

A successful ticket POST mints a real auth ticket (nothing launches). Run `--live` on purpose.
