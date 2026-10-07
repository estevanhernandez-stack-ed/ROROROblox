#!/usr/bin/env python3
"""Outline "Koii 4 eva" in EsteFont Pro Bold to an SVG path, for v1.33 item 10.

WHY OUTLINE AT ALL: the About page's easter egg is Este's own words in Este's own hand, which the
626labs-design brand rules reserve EsteFont Pro for. But the font must not ship inside the binary —
it is a licensed brand face, not an app asset — so the text is converted to a path once, here, and
the committed artefact is geometry. The same reason the official 626Labs wordmark SVGs are outlined.

The output is a WPF-ready path string plus the SVG source kept beside it, following the avatar
pattern (sources/*.svg committed, the rendered artefact committed, edit the source and re-run).

    python -I scripts/make-koii-mark.py

Writes:
    src/ROROROblox.App/About/Marks/sources/koii-4-eva.svg   the outlined source
    src/ROROROblox.App/About/Marks/koii-4-eva.path.txt      the same geometry, WPF Path.Data

Needs fontTools (present at 4.66.1) and the brand font from the design skill. Deliberately NOT
rasterised: the mark renders at 40px and up on high-DPI displays, where a 256px PNG is strictly
worse than geometry, and WPF draws a Path natively.
"""
from __future__ import annotations

import pathlib
import sys

from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont

TEXT = "K0ii 4 eva"  # a ZERO, not a lowercase o - the clan is K0ii (Este, 2026-10-07)
FONT = pathlib.Path.home() / ".claude-personal" / "skills" / "626labs-design" / "fonts" / "EsteFontPro-Bold.ttf"
ROOT = pathlib.Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "src" / "ROROROblox.App" / "About" / "Marks"
SRC_DIR = OUT_DIR / "sources"

# Target em height for the emitted geometry. The mark is laid out in WPF by its own Width/Height, so
# this only fixes the coordinate scale; 100 keeps the numbers readable in the XAML.
EM = 100.0


def main() -> None:
    if not FONT.is_file():
        raise SystemExit(f"EsteFont Pro Bold not found at {FONT}")

    font = TTFont(FONT)
    upem = font["head"].unitsPerEm
    scale = EM / upem
    glyph_set = font.getGlyphSet()
    cmap = font.getBestCmap()
    hmtx = font["hmtx"]

    pen_out = SVGPathPen(glyph_set)
    x = 0.0
    missing = []
    for ch in TEXT:
        name = cmap.get(ord(ch))
        if name is None:
            missing.append(ch)
            continue
        advance = hmtx[name][0]
        # Flip Y: font space is y-up, SVG and WPF are y-down.
        tpen = TransformPen(pen_out, (scale, 0, 0, -scale, x * scale, EM))
        glyph_set[name].draw(tpen)
        x += advance

    if missing:
        raise SystemExit(f"EsteFont Pro Bold has no glyph for: {missing!r}")

    d = pen_out.getCommands()
    if not d.strip():
        raise SystemExit("the pen produced no geometry — the glyph set drew nothing")

    width = x * scale
    # Vertical extent from the font's own metrics, so the viewBox holds ascenders and descenders.
    ascent = font["hhea"].ascent * scale
    descent = font["hhea"].descent * scale          # negative
    top = EM - ascent
    height = ascent - descent

    SRC_DIR.mkdir(parents=True, exist_ok=True)

    svg = (
        f'<svg xmlns="http://www.w3.org/2000/svg" '
        f'viewBox="0 {top:.3f} {width:.3f} {height:.3f}" '
        f'width="{width:.0f}" height="{height:.0f}" role="img" aria-label="{TEXT}">'
        f"<title>{TEXT}</title>"
        f'<path fill="#ffffff" d="{d}"/>'
        f"</svg>\n"
    )
    (SRC_DIR / "koii-4-eva.svg").write_text(svg, encoding="utf-8")

    # WPF wants the geometry alone. Its Path uses the same mini-language as SVG's d.
    (OUT_DIR / "koii-4-eva.path.txt").write_text(d + "\n", encoding="utf-8")

    print(f"text          : {TEXT}")
    print(f"font          : {FONT.name} (unitsPerEm {upem})")
    print(f"viewBox       : 0 {top:.3f} {width:.3f} {height:.3f}")
    print(f"aspect (w/h)  : {width / height:.4f}")
    print(f"path length   : {len(d)} chars")
    print(f"wrote         : {(SRC_DIR / 'koii-4-eva.svg').relative_to(ROOT)}")
    print(f"wrote         : {(OUT_DIR / 'koii-4-eva.path.txt').relative_to(ROOT)}")


if __name__ == "__main__":
    sys.exit(main())
