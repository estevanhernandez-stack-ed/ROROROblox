#!/usr/bin/env python3
"""Compose the "K0ii 4 eva" mark for the About page easter egg (v1.33 item 10).

WHY OUTLINE AT ALL: the egg is Este's own words in Este's own hand, which the 626labs-design brand
rules reserve EsteFont Pro for. The font must not ship inside the binary — it is a licensed brand
face, not an app asset — so the text is converted to paths once, here, and the committed artefact
is geometry. Same reason the official 626Labs wordmark SVGs are outlined.

THREE DELIBERATE CHOICES, all made with Este looking at renders on 2026-10-07. Do not collapse
them back to "just set the font and draw the string" — each one was tried the simple way first and
failed for a reason recorded below.

1. THE ZERO COMES FROM REGULAR, EVERYTHING ELSE FROM BOLD.
   The clan is K0ii with a zero, and it had been misspelt Koii for a long time. Bold's zero is a
   round, squat shape that reads as a lowercase o at any size — measured at 1.21x the height of
   its own 'o' and the same width. Regular's zero is 1.26x and sits on much narrower strokes, so it
   reads as a zero. But Regular's double-i reads as "TT", so the i's stay Bold. Mixing cuts per
   glyph is only possible because this composes glyph by glyph; it is not a font fallback.

2. ESTE'S OWN PERIOD GOES IN THE MIDDLE OF THE ZERO.
   A dotted zero is the conventional way to separate 0 from O, and taking the dot from Regular's
   `period` keeps it in his hand instead of a drawn circle that would look imported. DOT_FRACTION
   is its width as a share of the zero's; at the mark's rendered size anything under ~0.3 is too
   few pixels to register, which is why it is 0.36 rather than something daintier.

3. THE FONT HAS NO ALTERNATES TO REACH FOR.
   EsteFontPro-Bold has no GSUB table at all — no stylistic sets, no slashed zero, nothing. That
   was checked before any of this; the only levers are which cut each glyph comes from, the dot,
   and the size the mark renders at.

    python scripts/make-koii-mark.py

Writes:
    src/ROROROblox.App/About/Marks/sources/koii-4-eva.svg   the outlined source
    src/ROROROblox.App/About/Marks/koii-4-eva.path.txt      the same geometry, WPF Path.Data

Needs fontTools and the brand fonts from the design skill. Deliberately NOT rasterised: the mark
renders at 40px on high-DPI displays, where a PNG is strictly worse than geometry, and WPF draws a
Path natively. Run scripts/gen-about-marks-xaml.py afterwards to refresh Marks.xaml.
"""
from __future__ import annotations

import pathlib
import sys

from fontTools.pens.boundsPen import BoundsPen
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont

TEXT = "K0ii 4 eva"          # a ZERO, not a lowercase o — the clan is K0ii (Este, 2026-10-07)
ZERO_CUT = "Regular"         # see choice 1
BODY_CUT = "Bold"
DOT_FRACTION = 0.36          # see choice 2

FONTS = pathlib.Path.home() / ".claude-personal" / "skills" / "626labs-design" / "fonts"
ROOT = pathlib.Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "src" / "ROROROblox.App" / "About" / "Marks"
SRC_DIR = OUT_DIR / "sources"

# Target em height for the emitted geometry. The mark is laid out in WPF by its own Height, so this
# only fixes the coordinate scale; 100 keeps the numbers readable in the XAML.
EM = 100.0


def load(cut: str) -> dict:
    path = FONTS / f"EsteFontPro-{cut}.ttf"
    if not path.is_file():
        raise SystemExit(f"EsteFont Pro {cut} not found at {path}")
    font = TTFont(path)
    return {
        "glyphs": font.getGlyphSet(),
        "cmap": font.getBestCmap(),
        "hmtx": font["hmtx"],
        "upem": font["head"].unitsPerEm,
    }


def bounds_of(cut: dict, name: str):
    pen = BoundsPen(cut["glyphs"])
    cut["glyphs"][name].draw(pen)
    return pen.bounds


def main() -> None:
    cuts = {name: load(name) for name in {ZERO_CUT, BODY_CUT}}
    dot_cut = cuts[ZERO_CUT]
    dot_name = dot_cut["cmap"].get(ord("."))
    if dot_name is None:
        raise SystemExit(f"EsteFont Pro {ZERO_CUT} has no period to put in the zero")
    dx0, dy0, dx1, dy1 = bounds_of(dot_cut, dot_name)

    pen = SVGPathPen(cuts[BODY_CUT]["glyphs"])
    x = 0.0
    missing = []
    for ch in TEXT:
        cut_name = ZERO_CUT if ch == "0" else BODY_CUT
        cut = cuts[cut_name]
        name = cut["cmap"].get(ord(ch))
        if name is None:
            missing.append((ch, cut_name))
            continue
        scale = EM / cut["upem"]
        # Flip Y: font space is y-up, SVG and WPF are y-down.
        cut["glyphs"][name].draw(TransformPen(pen, (scale, 0, 0, -scale, x, EM)))

        if ch == "0":
            zx0, zy0, zx1, zy1 = bounds_of(cut, name)
            centre_x = x + ((zx0 + zx1) / 2) * scale
            centre_y = EM - ((zy0 + zy1) / 2) * scale
            target_w = (zx1 - zx0) * scale * DOT_FRACTION
            dot_scale = (EM / dot_cut["upem"]) * (
                target_w / ((dx1 - dx0) * (EM / dot_cut["upem"])))
            # The dot winds the same way as the zero's outer contour, so under FillRule=Nonzero it
            # fills inside the counter rather than punching a second hole. Verified by render.
            dot_cut["glyphs"][dot_name].draw(TransformPen(pen, (
                dot_scale, 0, 0, -dot_scale,
                centre_x - ((dx0 + dx1) / 2) * dot_scale,
                centre_y + ((dy0 + dy1) / 2) * dot_scale)))

        x += cut["hmtx"][name][0] * scale

    if missing:
        raise SystemExit(f"glyphs not found: {missing!r}")

    d = pen.getCommands()
    if not d.strip():
        raise SystemExit("the pen produced no geometry")

    body = cuts[BODY_CUT]
    ascent = body["font"]["hhea"].ascent if "font" in body else None  # not kept; computed below
    font = TTFont(FONTS / f"EsteFontPro-{BODY_CUT}.ttf")
    scale = EM / font["head"].unitsPerEm
    ascent = font["hhea"].ascent * scale
    descent = font["hhea"].descent * scale
    top = EM - ascent
    height = ascent - descent

    SRC_DIR.mkdir(parents=True, exist_ok=True)
    svg = (
        f'<svg xmlns="http://www.w3.org/2000/svg" '
        f'viewBox="0 {top:.3f} {x:.3f} {height:.3f}" '
        f'width="{x:.0f}" height="{height:.0f}" role="img" aria-label="{TEXT}">'
        f"<title>{TEXT}</title>"
        f'<path fill="#ffffff" fill-rule="nonzero" d="{d}"/>'
        f"</svg>\n"
    )
    (SRC_DIR / "koii-4-eva.svg").write_text(svg, encoding="utf-8")
    (OUT_DIR / "koii-4-eva.path.txt").write_text(d + "\n", encoding="utf-8")

    print(f"text        : {TEXT}")
    print(f"zero        : {ZERO_CUT}, with a {DOT_FRACTION:.0%} {ZERO_CUT} period centred in it")
    print(f"everything else: {BODY_CUT}")
    print(f"advance     : {x:.0f}   path {len(d)} chars")
    print(f"wrote       : {(SRC_DIR / 'koii-4-eva.svg').relative_to(ROOT)}")
    print(f"wrote       : {(OUT_DIR / 'koii-4-eva.path.txt').relative_to(ROOT)}")
    print("next        : python scripts/gen-about-marks-xaml.py")


if __name__ == "__main__":
    sys.exit(main())
