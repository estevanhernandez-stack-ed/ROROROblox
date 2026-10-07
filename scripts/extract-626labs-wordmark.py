#!/usr/bin/env python3
"""Extract the official 626Labs wordmark geometry for the WPF About page (v1.33 item 10).

The brand rules are explicit: "Use the files, don't retype it. The SVGs in assets/Logos/ are
outlined, so they render the same everywhere with no font loaded. Typing 626Labs in --font-hand is
fine for a one-off mock, never for shipped work." So this copies the official outlined SVG into the
repo and lifts its path data; it does not redraw or re-set the lockup.

The source is the `-dark` ink (#ffffff, ink-0), which the Logos README assigns to dark grounds —
and all four of RoRoRo's built-in themes are dark (#0F1F31, #0A1320, #1A0F1F, #101010). In WPF the
geometry is filled from the theme's own text brush rather than a baked colour, so a user-defined
light theme gets a legible mark too, which a single-ink PNG could not manage.

The c2pa provenance metadata is stripped: it is 7.7 KB of signature describing the SVG file, it is
not geometry, and WPF has no use for it. The upstream file in the design skill keeps it.

    python scripts/extract-626labs-wordmark.py

Writes:
    src/ROROROblox.App/About/Marks/sources/626labs-wordmark.svg   the outlined source, metadata out
    src/ROROROblox.App/About/Marks/626labs-wordmark.path.txt      one line per path, WPF Path.Data
"""
from __future__ import annotations

import pathlib
import re
import sys

SRC = (pathlib.Path.home() / ".claude-personal" / "skills" / "626labs-design"
       / "assets" / "Logos" / "626labs-wordmark-dark.svg")
ROOT = pathlib.Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "src" / "ROROROblox.App" / "About" / "Marks"
SRC_DIR = OUT_DIR / "sources"


def main() -> None:
    if not SRC.is_file():
        raise SystemExit(f"official wordmark not found at {SRC}")

    svg = SRC.read_text(encoding="utf-8")

    view = re.search(r'viewBox="([^"]+)"', svg)
    if not view:
        raise SystemExit("no viewBox on the official wordmark")
    vb = [float(v) for v in view.group(1).replace(",", " ").split()]
    if len(vb) != 4:
        raise SystemExit(f"unexpected viewBox: {view.group(1)!r}")

    paths = re.findall(r'<path[^>]*\bd="([^"]+)"', svg)
    if len(paths) != 2:
        raise SystemExit(
            f"expected 2 paths (the hand, then LLC), found {len(paths)}. The official asset "
            "changed shape — re-read the Logos README before trusting this extraction.")

    fills = set(re.findall(r'fill="([^"]+)"', svg))
    if fills != {"#ffffff"}:
        raise SystemExit(f"expected a single #ffffff ink, found {sorted(fills)}")

    body = re.sub(r"<metadata>.*?</metadata>", "", svg, flags=re.S)

    SRC_DIR.mkdir(parents=True, exist_ok=True)
    (SRC_DIR / "626labs-wordmark.svg").write_text(body, encoding="utf-8")
    (OUT_DIR / "626labs-wordmark.path.txt").write_text("\n".join(paths) + "\n", encoding="utf-8")

    print(f"source        : {SRC.name}")
    print(f"viewBox       : {vb[0]} {vb[1]} {vb[2]} {vb[3]}")
    print(f"aspect (w/h)  : {vb[2] / vb[3]:.4f}")
    print(f"paths         : {len(paths)}  (hand {len(paths[0])} chars, LLC {len(paths[1])} chars)")
    print(f"metadata      : {len(svg) - len(body)} bytes stripped")
    print(f"wrote         : {(SRC_DIR / '626labs-wordmark.svg').relative_to(ROOT)}")
    print(f"wrote         : {(OUT_DIR / '626labs-wordmark.path.txt').relative_to(ROOT)}")


if __name__ == "__main__":
    sys.exit(main())
