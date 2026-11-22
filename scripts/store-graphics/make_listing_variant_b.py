"""Build Store screenshot set "variant B": the same real captures, each under a headline.

Variant A (the set on the listing today) opens on the account list and carries no words.
Variant B opens on the eight-client wall and says what each frame is, for a Partner Center
product page experiment. Every frame is a real capture of the shipping app; nothing is drawn.

Usage:
    python scripts/store-graphics/make_listing_variant_b.py [--fonts DIR] [--lang en]

Fonts default to the 626labs-design skill's folder (Space Grotesk, Inter, JetBrains Mono).
Output: docs/store/screenshots-variant-b/<lang>/NN-name.png, 1920x1080.
"""
import argparse
import os

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
W, H = 1920, 1080
NAVY, CYAN, MAG = (15, 31, 49), (23, 212, 250), (242, 47, 137)
INK0, INK200, INK300 = (255, 255, 255), (196, 205, 218), (164, 174, 189)

# name, layout, source (repo-relative), kicker, headline, sub, optional note
#
# Nine frames, not ten: the theme builder capture is left out. The live capture shows its
# button row clipped on the left ("pen themes folder"), which is the dialog itself
# (ThemeBuilderWindow.xaml, five buttons in a right-aligned row 640 wide), not the capture.
FRAMES = [
    ("01-multi-instance", "wall", "docs/store/screenshots/{lang}/05-multi-instance.png",
     "Multi-Instance", "Eight Roblox clients. One PC.",
     "Eight accounts running at once. Each window title names the account signed into it.", None),
    ("02-accounts", "split", "docs/videos/v136-shots/{lang}/accounts-running.png",
     "Accounts", "Every account, one click each.",
     "Robux, Plus, friends online, and the game each one is in right now.",
     "You sign in on Roblox's own page. RoRoRo never sees your password."),
    ("03-account-panel", "split", "docs/videos/v136-shots/{lang}/accounts-panel.png",
     "Games", "Open an account, pick a game.",
     "Recent, favourites and private servers are right there. One click to play.", None),
    ("04-account-health", "split", "docs/videos/v136-shots/{lang}/accounts-health.png",
     "Account health", "It tells you what needs a fix.",
     "Session expired, needs verification, limited by Roblox. Each with a one-click fix.", None),
    ("05-library", "split", "docs/videos/v136-shots/{lang}/library-grid.png",
     "Library", "Every game in one Library.",
     "Search, filter, and choose which account plays.", None),
    ("06-history", "split", "docs/store/screenshots/{lang}/06-history.png",
     "History", "Every launch on record.",
     "See which account played what, and for how long.", None),
    ("07-themes", "themes", "docs/store/screenshots/{lang}/07-themes.png",
     "Themes", "Four themes built in.",
     "Flatline carries no meaning in colour, so nothing is lost to colour blindness or direct sun.", None),
    ("08-plugins", "split", "docs/store/screenshots/{lang}/09-plugins.png",
     "Plugins", "Plugins ask first.",
     "They run as separate processes. You grant each capability by name and can revoke it later.", None),
    ("09-compact", "split", "docs/store/screenshots/{lang}/10-compact.png",
     "Compact mode", "Only what is running.",
     "Pin compact mode to a corner of the screen and get back to the game.", None),
]

FONTS = None


def font(name, size, weight):
    f = ImageFont.truetype(os.path.join(FONTS, name), size)
    try:
        axes = f.get_variation_axes()
        f.set_variation_by_axes([weight if a["name"] in (b"Weight", "Weight") else a["default"] for a in axes])
    except Exception:
        pass
    return f


def sg(size, weight=700):
    return font("SpaceGrotesk-Variable.woff2", size, weight)


def inter(size, weight=400):
    return font("Inter-Variable.woff2", size, weight)


def mono(size, weight=500):
    return font("JetBrainsMono-Variable.woff2", size, weight)


def field():
    """Navy field with a cyan glow top-left and a magenta glow bottom-right (the pair, always)."""
    im = Image.new("RGB", (W, H), NAVY)
    glow = Image.new("RGB", (W, H), NAVY)
    d = ImageDraw.Draw(glow)
    r = int(max(W, H) * 0.55)
    d.ellipse([-r * 0.4, -r * 0.5, r * 0.9, r * 0.8], fill=(20, 70, 95))
    d.ellipse([W - r * 0.9, H - r * 0.7, W + r * 0.4, H + r * 0.5], fill=(70, 30, 60))
    glow = glow.filter(ImageFilter.GaussianBlur(r * 0.35))
    return Image.blend(im, glow, 0.9)


def lerp(t):
    return tuple(int(CYAN[i] + (MAG[i] - CYAN[i]) * t) for i in range(3))


def hairline(d, x0, y, w, h=4):
    for x in range(int(x0), int(x0 + w)):
        d.line([(x, y), (x, y + h - 1)], fill=lerp((x - x0) / w))


def meta(d, text, f, x, y, fill=INK300, anchor="left"):
    """Uppercase mono label with +0.12em tracking."""
    text = text.upper()
    track = f.size * 0.12
    widths = [f.getlength(c) for c in text]
    total = sum(widths) + track * (len(text) - 1)
    if anchor == "center":
        x -= total / 2
    for c, w in zip(text, widths):
        d.text((x, y), c, font=f, fill=fill)
        x += w + track


def wrap(text, f, max_w):
    lines, line = [], ""
    for word in text.split():
        trial = (line + " " + word).strip()
        if f.getlength(trial) <= max_w or not line:
            line = trial
        else:
            lines.append(line)
            line = word
    if line:
        lines.append(line)
    return lines


def window_of(path, trim_black=False):
    """The app window inside a store composite: the bounding box of what is not canvas.

    trim_black also drops the solid black strip PrintWindow leaves down the left edge and
    along the bottom of a captured window. Never for the wall: its black is the desktop.
    """
    im = Image.open(path).convert("RGB")
    canvas = im.getpixel((2, 2))
    diff = ImageChops.difference(im, Image.new("RGB", im.size, canvas)).convert("L")
    box = diff.point(lambda v: 255 if v > 14 else 0).getbbox()
    im = im.crop(box) if box else im
    if not trim_black:
        return im

    def black(px):
        return max(px) < 8

    left, bottom = 0, im.height
    rows = [int(im.height * f) for f in (0.3, 0.5, 0.7, 0.9)]
    while left < im.width // 4 and all(black(im.getpixel((left, y))) for y in rows):
        left += 1
    cols = [int(im.width * f) for f in (0.3, 0.5, 0.7, 0.9)]
    while bottom > im.height * 3 // 4 and all(black(im.getpixel((x, bottom - 1))) for x in cols):
        bottom -= 1
    return im.crop((left, 0, im.width, bottom))


def card(im, radius=10):
    """Rounded window with a hairline edge; returns (rgba, shadow) to paste in that order."""
    im = im.convert("RGBA")
    mask = Image.new("L", im.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, im.width - 1, im.height - 1], radius=radius, fill=255)
    im.putalpha(mask)
    edge = ImageDraw.Draw(im)
    edge.rounded_rectangle([0, 0, im.width - 1, im.height - 1], radius=radius, outline=(255, 255, 255, 46), width=1)
    pad = 80
    shadow = Image.new("RGBA", (im.width + pad * 2, im.height + pad * 2), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle(
        [pad, pad + 18, pad + im.width, pad + 18 + im.height], radius=radius, fill=(2, 6, 14, 190))
    return im, shadow.filter(ImageFilter.GaussianBlur(30)), pad


def place(canvas, im, x, y):
    win, shadow, pad = card(im)
    canvas.paste(shadow, (x - pad, y - pad), shadow)
    canvas.paste(win, (x, y), win)


def footer(d, x, anchor="left"):
    meta(d, "626 Labs  /  Imagine Something Else.", mono(20), x, H - 62, INK300, anchor)


def wall(src, kicker, headline, sub, note):
    canvas = field().convert("RGBA")
    d = ImageDraw.Draw(canvas)
    meta(d, "RoRoRo  /  " + kicker, mono(24), W / 2, 44, CYAN, "center")
    hf = sg(84)
    d.text((W / 2 - hf.getlength(headline) / 2, 78), headline, font=hf, fill=INK0)
    sf = inter(31)
    d.text((W / 2 - sf.getlength(sub) / 2, 190), sub, font=sf, fill=INK200)
    im = window_of(src)
    tw = 1700
    th = int(im.height * tw / im.width)
    max_h = H - 252 - 30
    if th > max_h:
        th, tw = max_h, int(im.width * max_h / im.height)
    im = im.resize((tw, th), Image.LANCZOS)
    place(canvas, im, (W - tw) // 2, 252)
    hairline(ImageDraw.Draw(canvas), (W - 220) / 2, 240 - 6, 220)
    return canvas.convert("RGB")


THEME_NAMES = ["Brand", "Midnight", "Magenta Heat", "Flatline"]


def theme_windows(path):
    """The four theme windows of the 2x2 themes composite, in reading order.

    Each is found as the non-canvas bounding box inside its quadrant's window band, which
    leaves out the composite's own text labels (they sit below and left of the windows).
    """
    im = Image.open(path).convert("RGB")
    canvas = im.getpixel((2, 2))
    out = []
    for qy in (0, im.height // 2):
        for qx in (0, im.width // 2):
            band = (qx + int(im.width * 0.12), qy + int(im.height * 0.05),
                    qx + im.width // 2, qy + int(im.height * 0.40))
            region = im.crop(band)
            diff = ImageChops.difference(region, Image.new("RGB", region.size, canvas)).convert("L")
            box = diff.point(lambda v: 255 if v > 14 else 0).getbbox()
            out.append(region.crop(box))
    return out


def text_column(canvas, kicker, headline, sub, note):
    d = ImageDraw.Draw(canvas)
    x, col = 96, 590
    hf, sf, nf = sg(70), inter(31), inter(25)
    h_lines = wrap(headline, hf, col)
    s_lines = wrap(sub, sf, col)
    n_lines = wrap(note, nf, col - 28) if note else []
    h_lh, s_lh, n_lh = 78, 44, 36
    block = 34 + 30 + len(h_lines) * h_lh + 26 + len(s_lines) * s_lh
    if n_lines:
        block += 44 + len(n_lines) * n_lh
    y = (H - block) // 2 - 10
    meta(d, "RoRoRo  /  " + kicker, mono(23), x, y, CYAN)
    y += 34
    hairline(d, x, y + 8, 120)
    y += 30
    for line in h_lines:
        d.text((x, y), line, font=hf, fill=INK0)
        y += h_lh
    y += 26
    for line in s_lines:
        d.text((x, y), line, font=sf, fill=INK200)
        y += s_lh
    if n_lines:
        y += 44
        d.line([(x, y + 4), (x, y + len(n_lines) * n_lh - 6)], fill=MAG, width=4)
        for line in n_lines:
            d.text((x + 28, y), line, font=nf, fill=INK0)
            y += n_lh
    footer(d, x)


def split(src, kicker, headline, sub, note, composite):
    canvas = field().convert("RGBA")
    im = window_of(src, trim_black=True) if composite else Image.open(src).convert("RGB")
    box_x, box_w, box_y, box_h = 770, 1080, 70, 940
    scale = min(box_w / im.width, box_h / im.height, 1.2)
    tw, th = int(im.width * scale), int(im.height * scale)
    if scale != 1.0:
        im = im.resize((tw, th), Image.LANCZOS)
    place(canvas, im, box_x + (box_w - tw) // 2, box_y + (box_h - th) // 2)
    text_column(canvas, kicker, headline, sub, note)
    return canvas.convert("RGB")


def themes(src, kicker, headline, sub, note):
    """Four real theme windows in a 2x2, each named in the mono label under it."""
    canvas = field().convert("RGBA")
    wins = theme_windows(src)
    box_x, box_w, box_y, box_h = 770, 1080, 110, 860
    gap_x, gap_y, label_h = 40, 34, 44
    cell_w = (box_w - gap_x) // 2
    cell_h = (box_h - gap_y) // 2 - label_h
    for i, win in enumerate(wins):
        scale = min(cell_w / win.width, cell_h / win.height)
        tw, th = int(win.width * scale), int(win.height * scale)
        win = win.resize((tw, th), Image.LANCZOS)
        col, row = i % 2, i // 2
        x = box_x + col * (cell_w + gap_x) + (cell_w - tw) // 2
        y = box_y + row * (cell_h + label_h + gap_y)
        place(canvas, win, x, y)
        meta(ImageDraw.Draw(canvas), THEME_NAMES[i], mono(21), x, y + th + 16, INK200)
    text_column(canvas, kicker, headline, sub, note)
    return canvas.convert("RGB")


def main():
    global FONTS
    ap = argparse.ArgumentParser()
    ap.add_argument("--fonts", default=os.path.expanduser("~/.claude-personal/skills/626labs-design/fonts"))
    ap.add_argument("--lang", default="en")
    args = ap.parse_args()
    FONTS = args.fonts
    out_dir = os.path.join(REPO, "docs", "store", "screenshots-variant-b", args.lang)
    os.makedirs(out_dir, exist_ok=True)
    for name, layout, src, kicker, headline, sub, note in FRAMES:
        path = os.path.join(REPO, src.format(lang=args.lang))
        if layout == "wall":
            im = wall(path, kicker, headline, sub, note)
        elif layout == "themes":
            im = themes(path, kicker, headline, sub, note)
        else:
            im = split(path, kicker, headline, sub, note, composite="docs/store/screenshots/" in src)
        im.save(os.path.join(out_dir, name + ".png"), optimize=True)
        print("built", name, im.size)


if __name__ == "__main__":
    main()
