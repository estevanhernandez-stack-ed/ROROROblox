"""Where does RoRoRo rank in Microsoft Store search? Public, unauthenticated GETs. Generic client name."""
import json
import sys
import time
import urllib.parse
import urllib.request

PID = "9NMJCS390KWB"
QUERIES = sys.argv[1:] or ["roblox"]


def search(q, market="US", lang="en-us"):
    url = ("https://apps.microsoft.com/api/products/search?" + urllib.parse.urlencode(
        {"query": q, "mediaType": "all", "age": "all", "price": "all", "category": "all",
         "subscription": "all", "gl": market, "hl": lang}))
    req = urllib.request.Request(url, headers={"User-Agent": "store-rank-check/1.0", "Accept": "application/json"})
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.load(r)


for q in QUERIES:
    market, lang = "US", "en-us"
    if "|" in q:
        q, market, lang = q.split("|")
    try:
        j = search(q, market, lang)
    except Exception as e:  # noqa: BLE001
        print(f"{q!r} [{market}]: ERROR {e}")
        continue
    items = j.get("productsList") or j.get("products") or j.get("highlightedList") or []
    if not items and isinstance(j, dict):
        print(f"{q!r}: keys={list(j.keys())[:12]}")
    pos = next((i + 1 for i, p in enumerate(items) if str(p.get("productId", "")).upper() == PID), None)
    names = [f"{p.get('title', '?')[:34]}" for p in items[:8]]
    print(f"{q!r} [{market}/{lang}] results={len(items)} RoRoRo_pos={pos} :: top8={names}")
    time.sleep(1.5)
