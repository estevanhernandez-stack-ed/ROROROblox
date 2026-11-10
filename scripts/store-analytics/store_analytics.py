"""Read-only Microsoft Store analytics readout for RoRoRo.

GETs only, against the Store analytics API. Nothing here can change a listing or a submission.

    python scripts/store-analytics/store_analytics.py            # pull, then print the readout
    python scripts/store-analytics/store_analytics.py --target 10000
    python scripts/store-analytics/store_analytics.py --no-pull  # re-print from the last pull

Credentials are the same three environment variables the listing console uses:
STORE_TENANT_ID, STORE_CLIENT_ID, STORE_CLIENT_SECRET. Never put them in a file.

Raw responses are written OUTSIDE the repo (default: %TEMP%/rororo-store-analytics). The reviews
response carries reviewer names, so the raw JSON must never be committed. The readout prints
review text without names.

What "downloads" means here: the Store's successful installs (the number the hub snapshot and
Partner Center's Installs chart show). Acquisitions count only signed-in customers and run at
roughly half of installs; GitHub release-asset counts are update polling and are not downloads.
"""
from __future__ import annotations

import argparse
import collections
import datetime as dt
import json
import os
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request

APP = "9NMJCS390KWB"
BASE = "https://manage.devcenter.microsoft.com/v1.0/my/analytics/"
FIRST_DAY = "2026-05-01"  # the listing went live 2026-05-06
ENV = ("STORE_TENANT_ID", "STORE_CLIENT_ID", "STORE_CLIENT_SECRET")


def token() -> str:
    missing = [k for k in ENV if not os.environ.get(k)]
    if missing:
        raise SystemExit("missing environment variable(s): " + ", ".join(missing))
    body = urllib.parse.urlencode({
        "grant_type": "client_credentials",
        "client_id": os.environ["STORE_CLIENT_ID"],
        "client_secret": os.environ["STORE_CLIENT_SECRET"],
        "resource": "https://manage.devcenter.microsoft.com",
    }).encode()
    url = f"https://login.microsoftonline.com/{os.environ['STORE_TENANT_ID']}/oauth2/token"
    with urllib.request.urlopen(urllib.request.Request(url, data=body), timeout=60) as r:
        return json.load(r)["access_token"]


def get(tok: str, path: str, **params) -> dict:
    """One GET, throttled. The API allows only a few calls per second and answers 429 freely."""
    url = BASE + path + "?" + urllib.parse.urlencode({"applicationId": APP, **params})
    req = urllib.request.Request(url, headers={"Authorization": "Bearer " + tok})
    for _ in range(8):
        try:
            with urllib.request.urlopen(req, timeout=120) as r:
                time.sleep(3)
                return json.load(r)
        except urllib.error.HTTPError as e:
            if e.code == 429:
                time.sleep(8)
                continue
            return {"__error__": e.code, "body": e.read().decode("utf-8", "replace")[:400]}
    return {"__error__": 429, "body": "rate limited on every attempt"}


def pull(out: str, end: str) -> None:
    tok = token()
    d90 = {"startDate": (dt.date.fromisoformat(end) - dt.timedelta(days=91)).isoformat(), "endDate": end}
    d30 = {"startDate": (dt.date.fromisoformat(end) - dt.timedelta(days=29)).isoformat(), "endDate": end}
    life = {"startDate": FIRST_DAY, "endDate": end}
    pulls = {
        "installs_by_day": ("installs", {**life, "aggregationLevel": "day", "groupby": "date", "top": 10000}),
        "acq_by_day": ("appacquisitions", {**d90, "aggregationLevel": "day", "groupby": "date", "top": 10000}),
        "acq_by_market": ("appacquisitions", {**d90, "groupby": "market", "top": 10000}),
        "funnel_30d": ("funnel", d30),
        "channels": ("appchannelconversions", {**d90, "groupby": "channelType,referrerUriDomain,customCampaignId", "top": 10000}),
        "usage_by_day": ("usagedaily", {**d30, "groupby": "date", "top": 10000}),
        "usage_by_market": ("usagedaily", {**d30, "groupby": "market", "top": 10000}),
        "ratings": ("ratings", {**life, "top": 10000}),
        "reviews": ("reviews", {**life, "top": 10000}),
        "failures": ("failurehits", {**d30, "groupby": "failureName,eventType,packageVersion", "top": 10000}),
        "failures_by_day": ("failurehits", {**d30, "aggregationLevel": "day", "groupby": "date", "top": 10000}),
    }
    os.makedirs(out, exist_ok=True)
    for name, (path, params) in pulls.items():
        res = get(tok, path, **params)
        with open(os.path.join(out, name + ".json"), "w", encoding="utf-8") as f:
            json.dump(res, f)
        state = f"ERROR {res['__error__']} {res['body'][:120]}" if "__error__" in res else f"{len(res.get('Value', []))} rows"
        print(f"  pulled {name}: {state}", file=sys.stderr)


def load(out: str, name: str) -> list:
    p = os.path.join(out, name + ".json")
    if not os.path.exists(p):
        return []
    with open(p, encoding="utf-8") as f:
        j = json.load(f)
    return [] if "__error__" in j else j.get("Value", [])


def readout(out: str, target: int) -> None:
    installs = sorted((r["date"][:10], r["successfulInstallCount"]) for r in load(out, "installs_by_day"))
    total = sum(n for _, n in installs)
    # The newest day is partial when the pull runs mid-day, so the pace uses the 14 days before it.
    full = installs[:-1][-14:]
    pace = sum(n for _, n in full) / max(1, len(full))
    print("== Installs (the download number) ==")
    print(f"  lifetime through {installs[-1][0] if installs else '?'}: {total}")
    print(f"  pace, last {len(full)} full days: {pace:.0f}/day  ({full[0][0]} to {full[-1][0]})" if full else "  no data")
    if full and total < target:
        days = (target - total) / pace
        eta = dt.date.fromisoformat(installs[-1][0]) + dt.timedelta(days=round(days))
        print(f"  to {target}: {target - total} more, about {days:.0f} days at this pace (around {eta})")
    for i in range(max(0, len(installs) - 57), len(installs) - 1, 7):
        chunk = installs[i:i + 7]
        print(f"    week of {chunk[0][0]}: {sum(n for _, n in chunk):5d}  ({sum(n for _, n in chunk) / len(chunk):.0f}/day)")

    f = {r["MetricType"]: r["UserCount"] for r in load(out, "funnel_30d")}
    if f:
        pv, ac, ins, us = (f.get(k, 0) for k in ("PageView", "Acqusition", "Install", "Usage"))
        print("\n== Funnel, last 30 days (signed-in users only) ==")
        print(f"  listing views {pv} -> Get {ac} ({100 * ac / max(1, pv):.0f}%) -> installed {ins} ({100 * ins / max(1, ac):.0f}%) -> opened {us}")

    print("\n== Where acquisitions come from, last 90 days ==")
    for r in load(out, "channels"):
        print(f"  {r.get('channelType')}: clicks={r.get('clickCount')} conversions={r.get('conversionCount')}"
              f" campaign={r.get('customCampaignId') or '-'} referrer={r.get('referrerUriDomain') or '-'}")

    acq = collections.Counter()
    for r in load(out, "acq_by_market"):
        acq[r["market"]] += r["acquisitionQuantity"]
    use = {r["market"]: r["dailyActiveUsers"] for r in load(out, "usage_by_market")}
    print("\n== Markets: acquisitions (90 d), share, active user-days (30 d) ==")
    for m, n in acq.most_common(16):
        print(f"  {m}: {n:4d}  {100 * n / max(1, sum(acq.values())):4.1f}%   active user-days {use.get(m, 0)}")

    days = sorted(load(out, "usage_by_day"), key=lambda r: r["date"])
    if days:
        last7 = days[-8:-1]
        print("\n== Daily users ==")
        print(f"  last 7 full days: {sum(r['dailyActiveUsers'] for r in last7) / max(1, len(last7)):.0f}/day,"
              f" new {sum(r['dailyNewUsers'] for r in last7) / max(1, len(last7)):.0f}/day")

    stars = collections.Counter()
    for r in load(out, "ratings"):
        for k, s in (("oneStar", 1), ("twoStars", 2), ("threeStars", 3), ("fourStars", 4), ("fiveStars", 5)):
            stars[s] += r.get(k, 0) or 0
    n = sum(stars.values())
    print("\n== Ratings, lifetime ==")
    print(f"  {n} ratings, average {sum(s * c for s, c in stars.items()) / max(1, n):.2f}:"
          + "".join(f"  {s}*={stars[s]}" for s in (5, 4, 3, 2, 1)))
    print("  reviews with text (no names), newest first; * marks an unanswered one:")
    def when(r):
        try:
            return dt.datetime.strptime(r.get("date", "")[:10].split(" ")[0], "%m/%d/%Y")
        except ValueError:
            return dt.datetime.min
    for r in sorted(load(out, "reviews"), key=when, reverse=True):
        if r.get("reviewText") or r.get("reviewTitle"):
            mark = " " if r.get("responseText") else "*"
            print(f"   {mark} {when(r):%Y-%m-%d} {r.get('rating')}* {r.get('market')} v{r.get('packageVersion') or '?'}:"
                  f" {(r.get('reviewTitle') or '').strip()} | {(r.get('reviewText') or '').strip()[:260]}")

    fails = collections.defaultdict(lambda: [0, 0])
    for r in load(out, "failures"):
        k = (r.get("eventType"), r.get("failureName") or "(unclassified, no stack)")
        fails[k][0] += r.get("deviceCount") or 0
        fails[k][1] += r.get("eventCount") or 0
    print("\n== Failures, last 30 days (devices, events) ==")
    for (kind, name), (dev, ev) in sorted(fails.items(), key=lambda kv: -kv[1][0]):
        print(f"  {kind}: {dev:.0f} devices, {ev:.0f} events :: {name[:110]}")
    byday = [r.get("deviceCount") or 0 for r in load(out, "failures_by_day")]
    if byday:
        print(f"  devices reporting a failure per day: avg {sum(byday) / len(byday):.0f}, max {max(byday):.0f}")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--out", default=os.path.join(tempfile.gettempdir(), "rororo-store-analytics"))
    ap.add_argument("--end", default=dt.date.today().isoformat(), help="last day to include (YYYY-MM-DD)")
    ap.add_argument("--target", type=int, default=10000)
    ap.add_argument("--no-pull", action="store_true")
    a = ap.parse_args()
    repo = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
    if os.path.abspath(a.out).startswith(repo + os.sep):
        raise SystemExit("--out must be outside the repo: the raw reviews response carries reviewer names")
    if not a.no_pull:
        pull(a.out, a.end)
    readout(a.out, a.target)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
