"""Render the RoRoRo hook cut: a short 9:16 clip that opens on the eight-client wall.

    python docs/videos/hook-cut/render_hook.py [--hub DIR] [--fonts DIR] [--skill FILE]

Four beats, about 27 seconds:
  1. the wall, clients arriving one by one, then a slow push toward RoRoRo's own window
  2. the account list
  3. the Library
  4. an end card, with a spoken close

Every picture is a real capture of the shipping app. The narration is takes already approved
for the launch trailer (the hub repo's voiceover/slide_01, 02 and 05, and a close cut from
slide_10), so nothing here spends voice credits or says a word that has not been signed off.

Beat 3 is the Library and not Squad Launch: the only Squad Launch capture on hand is the
empty dialog (no saved servers), and an empty dialog held for seven seconds is the weakest
picture in the set. A capture with saved servers in it would earn the beat back.

--hub is the 626 Labs hub checkout (default: a sibling folder named 626labs-hub). It supplies
the narration takes and the cube mark.
Output: rororo-hook-9x16.mp4 and end-card-9x16.png next to this file.
"""
import argparse
import importlib.util
import os
import shutil
import subprocess
import sys
import tempfile

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
W, H, FPS = 1080, 1920, 30
NAVY, CYAN, MAG = (15, 31, 49), (23, 212, 250), (242, 47, 137)
INK0, INK200, INK300 = (255, 255, 255), (196, 205, 218), (164, 174, 189)

WALL = os.path.join(REPO, "docs", "store", "screenshots", "en", "05-multi-instance.png")
ACCOUNTS = os.path.join(REPO, "docs", "videos", "v136-shots", "en", "accounts-running.png")
LIBRARY = os.path.join(REPO, "docs", "videos", "v136-shots", "en", "library-grid.png")

# The nine windows of the wall capture, measured on the capture after the canvas is cropped
# off (1920 x 895): eight Roblox clients and RoRoRo's compact window. (x0, y0, x1, y1).
WINS = {
    "w1": (7, 12, 509, 407), "w2": (527, 15, 1031, 409), "w3": (1031, 13, 1531, 407),
    "w4": (1416, 4, 1918, 399), "w5": (11, 491, 513, 886), "w6": (519, 494, 1021, 888),
    "w7": (1022, 492, 1524, 887), "w8": (1420, 501, 1920, 895), "app": (778, 254, 1015, 613),
}
ABOVE = {"w2": ["app"], "w6": ["app"], "w3": ["w4"], "w7": ["w8"]}   # who covers whom
Z_ORDER = ["w1", "w2", "w3", "w4", "w5", "w6", "w7", "w8", "app"]
# Arrival order. A window that sits under another arrives after it, so it lands behind.
ARRIVE = ["app", "w1", "w2", "w5", "w6", "w4", "w3", "w8", "w7"]
RIGHT_FROM = 1022          # windows starting at or right of this go to the lower block
STEP, POP = 0.19, 0.20     # seconds between arrivals, and the length of one arrival
BLOCK_W = 900              # both blocks are this wide in the video, so the column reads as one wall
ZOOM_TO, ZOOM_START, ZOOM_END = 1.16, 1.8, 8.0

FONTS = None


def font(name, size, weight):
    f = ImageFont.truetype(os.path.join(FONTS, name), size)
    try:
        axes = f.get_variation_axes()
        f.set_variation_by_axes([weight if a["name"] in (b"Weight", "Weight") else a["default"] for a in axes])
    except Exception:
        pass
    return f


def field(w=W, h=H):
    im = Image.new("RGB", (w, h), NAVY)
    glow = Image.new("RGB", (w, h), NAVY)
    d = ImageDraw.Draw(glow)
    r = int(max(w, h) * 0.55)
    d.ellipse([-r * 0.4, -r * 0.5, r * 0.9, r * 0.8], fill=(20, 70, 95))
    d.ellipse([w - r * 0.9, h - r * 0.7, w + r * 0.4, h + r * 0.5], fill=(70, 30, 60))
    glow = glow.filter(ImageFilter.GaussianBlur(r * 0.35))
    return Image.blend(im, glow, 0.9)


def lerp(t):
    return tuple(int(CYAN[i] + (MAG[i] - CYAN[i]) * t) for i in range(3))


def crop_canvas(path, trim_black=False):
    """The capture inside a store composite: the bounding box of what is not canvas."""
    im = Image.open(path).convert("RGB")
    canvas = im.getpixel((2, 2))
    diff = ImageChops.difference(im, Image.new("RGB", im.size, canvas)).convert("L")
    box = diff.point(lambda v: 255 if v > 14 else 0).getbbox()
    im = im.crop(box) if box else im
    if trim_black:
        left, bottom = 0, im.height
        rows = [int(im.height * f) for f in (0.3, 0.5, 0.7, 0.9)]
        while left < im.width // 4 and all(max(im.getpixel((left, y))) < 8 for y in rows):
            left += 1
        cols = [int(im.width * f) for f in (0.3, 0.5, 0.7, 0.9)]
        while bottom > im.height * 3 // 4 and all(max(im.getpixel((x, bottom - 1))) < 8 for x in cols):
            bottom -= 1
        im = im.crop((left, 0, im.width, bottom))
    return im


# ---- beat 1: the wall --------------------------------------------------------------------

def wall_layout(q):
    """Sprites and their places at render scale q (1 = video pixels). Returns (panels, sprites)."""
    src = crop_canvas(WALL)
    left_w = max(b[2] for n, b in WINS.items() if b[0] < RIGHT_FROM)
    right_w = src.width - RIGHT_FROM
    scale = {"upper": BLOCK_W * q / left_w, "lower": BLOCK_W * q / right_w}
    height = {k: src.height * v for k, v in scale.items()}
    gap = 24 * q
    left = (W - BLOCK_W) * q / 2
    top = (H * q - (height["upper"] + height["lower"] + gap)) / 2 - 40 * q
    origins = {"upper": (left, top, 0), "lower": (left, top + height["upper"] + gap, RIGHT_FROM)}
    panels = [(left, origins[k][1], BLOCK_W * q, height[k]) for k in ("upper", "lower")]
    sprites = {}
    for name, (x0, y0, x1, y1) in WINS.items():
        sprite = src.crop((x0, y0, x1, y1)).convert("RGBA")
        hole = ImageDraw.Draw(sprite)
        for over in ABOVE.get(name, []):
            ox0, oy0, ox1, oy1 = WINS[over]
            hole.rectangle([ox0 - x0, oy0 - y0, ox1 - x0 - 1, oy1 - y0 - 1], fill=(0, 0, 0, 0))
        block = "lower" if x0 >= RIGHT_FROM else "upper"
        ox, oy, sx = origins[block]
        s = scale[block]
        size = (max(int((x1 - x0) * s), 2), max(int((y1 - y0) * s), 2))
        sprites[name] = (sprite.resize(size, Image.LANCZOS), ox + (x0 - sx) * s, oy + y0 * s)
    return panels, sprites


def wall_backdrop(q, panels):
    im = field(int(W * q), int(H * q)).convert("RGBA")
    d = ImageDraw.Draw(im)
    for x, y, w, h in panels:
        pad = 10 * q
        d.rounded_rectangle([x - pad, y - pad, x + w + pad, y + h + pad], radius=18 * q, fill=(0, 0, 0, 255))
    return im


def ease_out(u):
    u = min(max(u, 0.0), 1.0)
    return 1 - (1 - u) ** 3


def ease_in_out(u):
    u = min(max(u, 0.0), 1.0)
    return 3 * u * u - 2 * u * u * u


def wall_frames(duration):
    """Yield every frame of beat 1 as an RGB image."""
    panels, sprites = wall_layout(1)
    backdrop = wall_backdrop(1, panels)
    arrive_at = {name: (-1.0 if i < 2 else (i - 1) * STEP) for i, name in enumerate(ARRIVE)}
    settled = max(arrive_at.values()) + POP

    q = 1.6
    hi_panels, hi_sprites = wall_layout(q)
    still = wall_backdrop(q, hi_panels)
    for name in Z_ORDER:
        sprite, x, y = hi_sprites[name]
        still.alpha_composite(sprite, (int(x), int(y)))
    still = still.convert("RGB")
    app, ax, ay = hi_sprites["app"]
    target = ((ax + app.width / 2) / q, (ay + app.height / 2) / q)

    for f in range(int(round(duration * FPS))):
        t = f / FPS
        if t <= settled:
            frame = backdrop.copy()
            for name in Z_ORDER:
                sprite, x, y = sprites[name]
                p = ease_out((t - arrive_at[name]) / POP)
                if p <= 0:
                    continue
                if p < 1:
                    k = 0.82 + 0.18 * p
                    small = sprite.resize((max(int(sprite.width * k), 2), max(int(sprite.height * k), 2)))
                    small.putalpha(small.getchannel("A").point(lambda v: int(v * p)))
                    frame.alpha_composite(small, (int(x + (sprite.width - small.width) / 2),
                                                  int(y + (sprite.height - small.height) / 2)))
                else:
                    frame.alpha_composite(sprite, (int(x), int(y)))
            yield frame.convert("RGB")
            continue
        u = ease_in_out((t - ZOOM_START) / (ZOOM_END - ZOOM_START))
        zoom = 1 + (ZOOM_TO - 1) * u
        cx = W / 2 + (target[0] - W / 2) * u
        cy = H / 2 + (target[1] - H / 2) * u
        vw, vh = W / zoom, H / zoom
        x0 = min(max(cx - vw / 2, 0), W - vw)
        y0 = min(max(cy - vh / 2, 0), H - vh)
        box = (x0 * q, y0 * q, (x0 + vw) * q, (y0 + vh) * q)
        yield still.resize((W, H), Image.LANCZOS, box=box)


def encode_frames(frames, audio, duration, out):
    cmd = ["ffmpeg", "-y", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "rgb24",
           "-s", f"{W}x{H}", "-r", str(FPS), "-i", "-", "-i", audio,
           "-filter_complex", "[1:a]apad[a]", "-map", "0:v", "-map", "[a]", "-t", f"{duration:.3f}",
           "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p", "-r", str(FPS),
           "-c:a", "aac", "-b:a", "160k", "-ar", "44100", "-ac", "2", "-movflags", "+faststart", out]
    proc = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    for frame in frames:
        proc.stdin.write(frame.tobytes())
    proc.stdin.close()
    if proc.wait() != 0:
        sys.exit("ffmpeg failed on the wall segment")


# ---- beat 2: a push-in on one capture (the launch trailer's move) -------------------------

def kenburns_segment(bv, path, cx, cy, zoom_to, dur, audio, out, tmpdir):
    im = Image.open(path).convert("RGB")
    cover = max(W / im.width, H / im.height)
    bg = im.resize((int(im.width * cover) + 1, int(im.height * cover) + 1))
    bg = bg.crop(((bg.width - W) // 2, (bg.height - H) // 2, (bg.width - W) // 2 + W, (bg.height - H) // 2 + H))
    bg = bg.filter(ImageFilter.GaussianBlur(28)).point(lambda v: int(v * 0.92))
    fit = min(W * 0.92 / im.width, H * 0.86 / im.height)
    fw, fh = int(im.width * fit), int(im.height * fit)
    fx, fy = (W - fw) // 2, (H - fh) // 2
    bg.paste(im.resize((fw, fh), Image.LANCZOS), (fx, fy))
    composed = os.path.join(tmpdir, os.path.basename(out) + ".png")
    bg.save(composed)
    tx, ty = (fx + cx * fw) / W, (fy + cy * fh) / H
    n = max(int(dur * FPS), 2)
    z = f"1+({zoom_to}-1)*on/{n - 1}"
    x = f"min(max({tx}*iw-(iw/zoom)/2,0),iw-iw/zoom)"
    y = f"min(max({ty}*ih-(ih/zoom)/2,0),ih-ih/zoom)"
    vf = f"[0:v]scale={W * 2}:{H * 2},zoompan=z='{z}':x='{x}':y='{y}':d={n}:s={W}x{H}:fps={FPS},format=yuv420p[v]"
    bv.run(["ffmpeg", "-y", "-i", composed, "-i", audio, "-filter_complex", vf + ";[1:a]apad[a]",
            "-map", "[v]", "-map", "[a]", "-t", f"{dur:.3f}",
            "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-r", str(FPS),
            "-c:a", "aac", "-b:a", "160k", "-ar", "44100", "-ac", "2", "-movflags", "+faststart", out])


# ---- beat 4: the end card ---------------------------------------------------------------

def end_card(cube_path, out):
    im = field().convert("RGB")
    d = ImageDraw.Draw(im)

    def center(text, f, y, fill=INK0):
        d.text((W / 2 - f.getlength(text) / 2, y), text, font=f, fill=fill)

    def meta(text, f, y, fill=INK300):
        text = text.upper()
        track = f.size * 0.12
        widths = [f.getlength(c) for c in text]
        x = W / 2 - (sum(widths) + track * (len(text) - 1)) / 2
        for c, w in zip(text, widths):
            d.text((x, y), c, font=f, fill=fill)
            x += w + track

    mono = font("JetBrainsMono-Variable.woff2", 30, 500)
    meta("626 Labs  /  RoRoRo", mono, 250, CYAN)
    size = 380
    cube = Image.open(cube_path).convert("RGBA").resize((size, size), Image.LANCZOS)
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1], radius=size // 8, fill=255)
    im.paste(cube, ((W - size) // 2, 350), mask)
    big = font("SpaceGrotesk-Variable.woff2", 100, 700)
    center("Free on the", big, 820)
    center("Microsoft Store.", big, 930)
    x0 = (W - 320) / 2
    for x in range(int(x0), int(x0 + 320)):
        d.line([(x, 1090), (x, 1094)], fill=lerp((x - x0) / 320))
    center("Search RoRoRo", font("SpaceGrotesk-Variable.woff2", 70, 500), 1135, CYAN)
    small = font("Inter-Variable.woff2", 30, 400)
    center("Windows 10 and 11.", small, 1260, INK200)
    center("Independent tool. Not affiliated with Roblox Corporation.", small, 1640, INK300)
    meta("626 Labs  /  Imagine Something Else.", font("JetBrainsMono-Variable.woff2", 24, 500), 1760)
    im.save(out, optimize=True)


# The trailer's closing take reads: "Free on the Microsoft Store, / Windows AND Mac. / Scan the
# code, / save the page, / grab it on your desktop. / RoRoRo, / from six-two-six Labs. / Imagine
# something else." ffmpeg's silencedetect finds eight pauses in it, one after each phrase
# (measured 2026-11-22: 1.45, 2.87, 3.98, 5.04, 6.32, 7.79, 9.42, 11.15 s). The close below
# keeps the first phrase and the sixth and seventh.
CLOSE_FIRST = (0.0, 1.55)     # "Free on the Microsoft Store"
CLOSE_SECOND = (6.62, 9.50)   # "RoRoRo, from six-two-six Labs."
CLOSE_PAUSE = 0.28


def spoken_close(bv, vo, tmpdir):
    """The end card's narration.

    A file at voiceover/close.mp3 beside this script wins: that is where a line generated or
    recorded for this cut goes. Without one, the close is cut from the launch trailer's
    approved closing take, so it is made only of words already signed off:
    "Free on the Microsoft Store. RoRoRo, from six-two-six Labs."
    """
    override = os.path.join(HERE, "voiceover", "close.mp3")
    if os.path.exists(override):
        return override
    src = os.path.join(vo, "slide_10.mp3")
    out = os.path.join(tmpdir, "close.wav")
    fmt = "aformat=sample_rates=44100:channel_layouts=mono"
    a0, a1 = CLOSE_FIRST
    b0, b1 = CLOSE_SECOND
    graph = (
        f"[0:a]atrim={a0}:{a1},asetpts=PTS-STARTPTS,{fmt},afade=t=out:st={a1 - a0 - 0.05:.3f}:d=0.05[a];"
        f"anullsrc=r=44100:cl=mono,atrim=0:{CLOSE_PAUSE},{fmt}[s];"
        f"[0:a]atrim={b0}:{b1},asetpts=PTS-STARTPTS,{fmt},afade=t=in:d=0.03,"
        f"afade=t=out:st={b1 - b0 - 0.05:.3f}:d=0.05[b];"
        "[a][s][b]concat=n=3:v=0:a=1[out]")
    bv.run(["ffmpeg", "-y", "-i", src, "-filter_complex", graph, "-map", "[out]", out])
    return out


def main():
    global FONTS
    ap = argparse.ArgumentParser()
    ap.add_argument("--hub", default=os.path.abspath(os.path.join(REPO, "..", "626labs-hub")))
    ap.add_argument("--fonts", default=os.path.expanduser("~/.claude-personal/skills/626labs-design/fonts"))
    ap.add_argument("--skill", default=os.path.expanduser(
        "~/.claude-personal/skills/tiktok-video-maker/scripts/build_video.py"))
    ap.add_argument("--out", default=os.path.join(HERE, "rororo-hook-9x16.mp4"))
    args = ap.parse_args()
    FONTS = args.fonts
    vo = os.path.join(args.hub, "assets", "video", "rororo", "src", "voiceover")
    cube = os.path.join(args.hub, "assets", "thumb-rororo.png")
    for need in (vo, cube, args.skill, WALL, ACCOUNTS, LIBRARY):
        if not os.path.exists(need):
            sys.exit(f"missing: {need}")

    spec = importlib.util.spec_from_file_location("build_video", args.skill)
    bv = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(bv)
    tmp = tempfile.mkdtemp(prefix="rororo_hook_")
    gap = 0.4
    segs, total = [], 0.0

    a1 = os.path.join(vo, "slide_01.mp3")
    d1 = bv.probe_duration(a1) + gap
    segs.append(os.path.join(tmp, "seg_000.mp4"))
    encode_frames(wall_frames(d1), a1, d1, segs[-1])
    print(f"[1/4] the wall ({d1:.2f}s)")
    total += d1

    a2 = os.path.join(vo, "slide_02.mp3")
    d2 = bv.probe_duration(a2) + gap
    segs.append(os.path.join(tmp, "seg_001.mp4"))
    kenburns_segment(bv, ACCOUNTS, 0.36, 0.40, 1.85, d2, a2, segs[-1], tmp)
    print(f"[2/4] the account list ({d2:.2f}s)")
    total += d2

    a3 = os.path.join(vo, "slide_05.mp3")
    d3 = bv.probe_duration(a3) + gap
    segs.append(os.path.join(tmp, "seg_002.mp4"))
    kenburns_segment(bv, LIBRARY, 0.45, 0.48, 1.5, d3, a3, segs[-1], tmp)
    print(f"[3/4] the Library ({d3:.2f}s)")
    total += d3

    card = os.path.join(HERE, "end-card-9x16.png")
    end_card(cube, card)
    close = spoken_close(bv, vo, tmp)
    d4 = bv.probe_duration(close) + 0.5
    bv.W, bv.H = W, H
    fitted = bv.image_target
    bv.image_target = lambda iw, ih, n, layout, idx: (W, H, 0, 0) if (iw, ih) == (W, H) else fitted(iw, ih, n, layout, idx)
    slide4 = {"images": [{"path": card, "effect": "fade"}]}
    segs.append(bv.build_slide(3, slide4, {"fps": FPS, "background": {"type": "color", "color": "#0f1f31"}}, d4, close, tmp))
    print(f"[4/4] the end card ({d4:.2f}s)")
    total += d4

    listing = os.path.join(tmp, "concat.txt")
    with open(listing, "w") as f:
        for s in segs:
            f.write("file '" + s.replace("\\", "/") + "'\n")
    bv.run(["ffmpeg", "-y", "-f", "concat", "-safe", "0", "-i", listing, "-c", "copy",
            "-movflags", "+faststart", args.out])
    shutil.rmtree(tmp, ignore_errors=True)
    print(f"done: {args.out} ({total:.1f}s)")


if __name__ == "__main__":
    main()
