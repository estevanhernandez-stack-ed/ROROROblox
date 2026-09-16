# Metric alerts you can read, one per read

Approved by the owner on 2026-09-15. Builds on `2026-09-09-external-metric-alerts-design.md` (rules come from the local, unsigned `metric-rules.json`, permanently) and the v1.28 metric alerts feature.

---

> **APPROVED DEVIATIONS (2026-09-15) — C1, C2, C3, added by the controller before execution and built as written.**
>
> Three rulings were added before any task was dispatched. Each knowingly changes a line stated
> below; all three are approved, and all three shipped in `feat/metric-alert-wording`.
>
> - **C1** (contradicts §2 "the same … per-(account, kind) cooldown" below): a metric breach's
>   cooldown is keyed by account and metric id, not account and kind, so two different stats
>   breaching for one account in the same read both alert. Every other alert kind keeps the
>   per-(account, kind) cooldown §2 describes.
> - **C2** (new; §2's "existing alert kinds keep their current behaviour" is unaffected): a grouped
>   alert caps its account lines to one body that fits Discord, Pushover and ntfy, then ends with
>   "and N more." The cap applies to all five alert kinds, not only metric groups, because a large
>   drop-out group overflows Discord the same way a large metric group would.
> - **C3** (new): every webhook POST — all five alert kinds — now sends `allowed_mentions` with an
>   empty `parse` array, so a label, metric id or account name can never ping the shared clan
>   channel.
>
> Do not rewrite §2 below to match; this banner is the correction. Full account:
> [docs/superpowers/plans/2026-09-15-metric-alert-wording.md](../plans/2026-09-15-metric-alert-wording.md#controller-rulings-added-before-execution-2026-09-15)
> and the 2026-09-15 entry in `docs/decisions.md`.

---

## Why

A live test on 2026-09-15 used a temporary Level rule ("Diamonds above 0") against 8 accounts, with metric alerts going to Local, Mine and Phone. It produced 24 notifications, one per account per destination. Each Discord post read:

```
CElCPapa — ps99.diamonds
• CElCPapa — ps99.diamonds at 2974993
```

- **Raw text:** the metric id and a bare number, with no friendly name, no unit and no rule.
- **No grouping:** a plugin reports one `ReportMetric` per account, and the router only groups triggers that arrive in the same call. So every account became its own alert, and four alts stalling in the same read would send four pushes.

## What changes

### 1. Wording from the rule

- **The rule row gains an optional `label`** in `metric-rules.json` (e.g. "Points"). Ur Score 0.3.2 writes it; hand-written rules may add it. A rule without a label falls back to the metric id, exactly as today.
- **The breach carries what fired:** the rule's label, kind, threshold, window and direction, and the observed value (for Rate, the measured rate per minute). `WebhookPayload` then composes the text from them, in English as today; one payload still feeds the toast, both webhooks and the phone.

| Kind | Title | Line |
|---|---|---|
| Rate | `{account} — {label} stopped climbing` | `• {account} — {rate} a minute over {window} min (alert under {threshold})` |
| Level, below | `{account} — {label} fell below {threshold}` | `• {account} — now {value}` |
| Level, above | `{account} — {label} went above {threshold}` | `• {account} — now {value}` |
| Event | `{account} — {label} changed` | `• {account} — now {value}` |

- **Formatting:**
  - numbers of 1,000 and up use thousands separators;
  - small values keep today's `0.##`;
  - a threshold prints the way the rule wrote it.
- **Streamer masking** applies to account names exactly as it does for every other alert kind.

### 2. One alert per read

- **Grouping window:** metric breaches for the same metric id and the same rule, arriving within a short window (a few seconds after the first), are grouped into one alert. It's titled `{n} accounts — {label} stopped climbing` (or the kind's wording), with one line per account.
- **Destinations and cooldown:** the grouped alert goes through the same `AlertRouter`, with the same destinations, per-(account, kind) cooldown, mute and desktop fallback. An account in cooldown drops out of the group, and the others still alert.
- **Different stats or rules** in the same window stay separate alerts, one per (metric, rule).
- **Scope:** only metric breaches are grouped. Existing alert kinds keep their current behaviour.

## What doesn't change

- **Rules and toggles:** the rules file stays local and unsigned. The `MetricAlertsEnabled` setting and destination checkboxes in Settings › Alerts stay as they are.
- **The plugin contract:** `ReportMetric` is untouched, with no proto change and no new capability.
- **Old rules files:** a rules file without labels behaves as in 1.28, apart from the new grouping.

## Release

The usual RoRoRo cycle:
1. Tag `vX.Y.Z.0` on main after `ci.yml` is green.
2. `release.yml` drafts the release.
3. Build the Store MSIX for x64 and arm64.
4. Listing audit.

Merging, tagging and the Store submission each wait for the owner's OK. Log the decision to the 626 Labs dashboard and `docs/decisions.md`.
