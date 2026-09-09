#!/usr/bin/env python3
"""Generate the Store Listing Console - the paste-helper for Partner Center submissions.

Partner Center takes one listing per language, and each listing is a long form filled top to
bottom. With six translated languages across nine listing rows that is a lot of switching between
an editor and a browser, in languages the operator cannot proofread by eye.

This builds one self-contained page holding every field for every listing, IN THE ORDER THE STORE
FORM ASKS FOR THEM, so it can be worked straight down beside the real page. The English original is
one toggle away, character counts run against the caps the form actually states, and the fields
Partner Center takes one-entry-at-a-time (product features, keywords) get a copy button each.

    python scripts/build-listing-console.py          # -> docs/store/listing-console.html

Re-run after any listing-copy change; the page is generated, never hand-edited.
"""
from __future__ import annotations

import base64
import io
import json
import re
from pathlib import Path

from PIL import Image

# Screenshot thumbnails ride along as base64 JPEGs rather than file references. Two reasons, and
# the first is hard: the published artifact's CSP blocks images from every origin, so a <img
# src="screenshots/01.png"> renders as a broken icon there no matter what the path says. The
# second is that base64 is ASCII, so embedding them keeps the whole file's pure-ASCII property.
THUMB_WIDTH = 260
THUMB_QUALITY = 72

ROOT = Path(__file__).resolve().parent.parent
STORE = ROOT / "docs" / "store"
OUT = STORE / "listing-console.html"
VERSION = "1.27.0.0"

# Partner Center listing rows -> the language whose copy serves them. Several rows share one copy
# set (French Canada reads the France copy; all three Spanish rows read one neutral Spanish).
LISTINGS = [
    ("English (default)", "en", None),
    ("French (France)", "fr", None),
    ("French (Canada)", "fr", "same copy as French (France)"),
    ("German (Germany)", "de", None),
    ("Polish", "pl", None),
    ("Portuguese (Brazil)", "pt-br", None),
    ("Russian", "ru", None),
    ("Spanish (Spain)", "es", None),
    ("Spanish (Mexico)", "es", "same copy as Spanish (Spain)"),
    ("Spanish (Americas)", "es", "same copy as Spanish (Spain)"),
]

LANG_NAME = {
    "en": "English", "fr": "Français", "de": "Deutsch",
    "ru": "Русский", "pt-br": "Português (Brasil)", "pl": "Polski", "es": "Español",
}

# The reserved product names, per language. Partner Center requires a name that is already reserved
# under Manage app names; "RoRoRo" leads every one so the brand reads first and only the descriptor
# is translated.
PRODUCT_NAMES = {
    "en": "RoRoRo — Multi-launcher for Windows",
    "fr": "RoRoRo — Multi-lanceur pour Windows",
    "de": "RoRoRo — Multi-Instanz-Launcher für Windows",
    "pl": "RoRoRo — Multi-launcher na Windowsa",
    "pt-br": "RoRoRo — Multi-launcher para Windows",
    "ru": "RoRoRo — Мульти-лаунчер для Windows",
    "es": "RoRoRo — Multi-launcher para Windows",
}
PRODUCT_NAME_NOTES = {
    "de": "Reserved. 'Multi-Instanz' over plain 'Multi-Launcher' because running several instances "
          "IS the product - the German modding scene's own term.",
    "pl": "Reserved. Declined 'na Windowsa' is the colloquial form the reservation already uses.",
    "es": "NOT YET RESERVED. Reserve this, then delete 'Multi-inicializador' - Spanish-language "
          "Roblox tooling keeps 'launcher' in English, and 'inicializador' means 'initializer'.",
}

# Image slots exactly as the form lists them, mapped to the file that fills each one.
SLOTS = {
    "logos": [
        ("9:16 Poster art", "720 x 1080", "store-poster-720x1080.png"),
        ("9:16 Poster art", "1440 x 2160", "store-poster-1440x2160.png"),
        ("1:1 Box art", "1080 x 1080", "store-boxart-1080x1080.png"),
        ("1:1 Box art", "2160 x 2160", "store-boxart-2160x2160.png"),
    ],
    "display": [
        ("1:1 App tile icon", "300 x 300", "store-display-300x300.png"),
        ("1:1", "150 x 150", "store-display-150x150.png"),
        ("1:1", "71 x 71", "store-display-71x71.png"),
    ],
    "hero": [
        ("16:9 Super hero art", "1920 x 1080", "store-hero-1920x1080.png"),
        ("16:9 Super hero art", "3840 x 2160", "store-hero-3840x2160.png"),
    ],
}

# (kind, key, label, cap, note) in Store-form order. kind: section | text | list | folder | slots | skip
BLOCKS = [
    ("section", "", "Store listing", None, None),
    ("text", "productname", "Product name", None, "Required. Must already be reserved under Manage app names."),
    ("text", "long", "Description", None, "Required. The main listing body."),
    ("text", "whatsnew", "What's new in this version", 1500, "Leave blank only on a first submission."),
    ("list", "features", "Product features", 200, "Up to 20. Partner Center takes one per box - Add more."),

    ("section", "", "Screenshots and images", None, None),
    ("folder", "screenshots", "Screenshots", 200,
     "At least one required. 1366 x 768 or larger, .png, under 50 MB, max 30 files. "
     "Each image takes its own caption, which doubles as its alt text."),
    ("slots", "logos", "Store logos", None,
     "9:16 Poster art is the main logo on Windows 10/11. Box art is the fallback."),
    ("slots", "display", "Store display images", None, "Shown in Store surfaces on Windows 10/11."),
    ("slots", "hero", "Super hero art", None,
     "Top of the listing. Must NOT include the product title. Also required if you ever add a trailer."),
    ("skip", "xbox", "Xbox images / Trailers", None,
     "Skip both - not an Xbox title, and we ship no trailer."),

    ("section", "", "Supplemental fields", None, None),
    ("skip", "titles", "Short title / Voice title", None, "Leave blank - Xbox-only fields."),
    ("text", "short", "Short description", 270,
     "Recommended 270 characters or fewer. Ours run under 200, so there is headroom."),

    ("section", "", "Additional information", None, None),
    ("list", "keywords", "Keywords", 40,
     "Up to 7, 40 characters each, 21 words total across all. One per box - press Enter."),
    ("text", "copyright_tm", "Copyright and trademark info", None,
     "One field in Partner Center - our copyright line and trademark paragraph go in together."),
    ("text", "license", "Additional license terms", None, None),
    ("text", "developedby", "Developed by", None, None),
]


def thumb_uri(path: Path) -> str:
    """Downscale one screenshot to a data: URI small enough to embed ten of."""
    with Image.open(path) as im:
        im = im.convert("RGB")
        h = round(im.height * THUMB_WIDTH / im.width)
        im = im.resize((THUMB_WIDTH, h), Image.LANCZOS)
        buf = io.BytesIO()
        im.save(buf, format="JPEG", quality=THUMB_QUALITY, optimize=True)
    return "data:image/jpeg;base64," + base64.b64encode(buf.getvalue()).decode("ascii")


def reviewer_letter() -> str:
    """The current release's letter to the Store reviewer, or empty if it isn't written yet."""
    p = STORE / f"reviewer-letter-{VERSION}.md"
    return p.read_text(encoding="utf-8").strip() if p.exists() else ""


def block_after(text: str, heading_pattern: str) -> str:
    m = re.search(heading_pattern + r".*?\n```\n(.*?)\n```", text, re.S)
    return m.group(1).strip() if m else ""


def parse_english() -> dict:
    t = (STORE / "listing-copy.md").read_text(encoding="utf-8")
    wn = (STORE / "whats-new-1.27.0.0.md").read_text(encoding="utf-8")
    cr = block_after(t, r"## Copyright")
    tm = block_after(t, r"## Trademark info")
    return {
        "productname": PRODUCT_NAMES["en"],
        "short": block_after(t, r"## Short description"),
        "long": block_after(t, r"## Long description"),
        "features": block_after(t, r"## Product features"),
        "whatsnew": block_after(wn, r"## English"),
        "captions": block_after(t, r"## Screenshot captions"),
        "copyright_tm": cr + "\n\n" + tm,
        "license": block_after(t, r"## Additional license terms"),
        "developedby": block_after(t, r"## Developed by"),
        "keywords": "\n".join(k.strip() for k in block_after(t, r"## Keywords").split(",") if k.strip()),
    }


def parse_language(lang: str, en: dict) -> dict:
    t = (STORE / f"listing-copy-{lang}.md").read_text(encoding="utf-8")
    cr = block_after(t, r"## Copyright")
    tm = block_after(t, r"## Trademark info")
    return {
        "productname": PRODUCT_NAMES[lang],
        "short": block_after(t, r"## Short description"),
        "long": block_after(t, r"## Long description"),
        "features": block_after(t, r"## Product features"),
        "whatsnew": block_after(t, r"## What's new in this version"),
        "captions": block_after(t, r"## Screenshot captions"),
        # Keywords ARE translated: this is the search field, so English here would be the one
        # place a fallback silently costs discovery rather than merely reading oddly.
        "keywords": block_after(t, r"## Keywords"),
        "copyright_tm": cr + "\n\n" + tm,
        # Legal boilerplate stays English - it names a US entity and an MIT licence text.
        "license": en["license"],
        "developedby": en["developedby"],
    }


def build_data() -> tuple[dict, list[str]]:
    warnings: list[str] = []
    en = parse_english()
    copy = {"en": en}
    for lang in ("fr", "de", "ru", "pt-br", "pl", "es"):
        copy[lang] = parse_language(lang, en)

    for lang, c in copy.items():
        for k, v in c.items():
            if not v:
                warnings.append(f"{lang}: empty field '{k}'")

    # Repo-RELATIVE paths, deliberately: an absolute path would carry this machine's user name into
    # a public repo (the pre-commit local-path guard rejects exactly that) and be wrong on anyone
    # else's checkout. The paired command opens the folder instead of asking for a paste.
    shots_dir = STORE / "screenshots"
    gfx_dir = STORE / "graphics"
    shots_rel = str(shots_dir.relative_to(ROOT)).replace("/", "\\")
    gfx_rel = str(gfx_dir.relative_to(ROOT)).replace("/", "\\")

    shot_paths = sorted(p for p in shots_dir.iterdir() if p.suffix.lower() == ".png")
    folders = {
        "screenshots": {
            "path": shots_rel,
            "cmd": f"explorer {shots_rel}",
            "files": [{"name": p.name, "thumb": thumb_uri(p)} for p in shot_paths],
        }
    }

    # Captions are matched to files by position, so a count mismatch would silently caption the
    # wrong image rather than fail - worth a warning at generate time.
    n_shots = len(folders["screenshots"]["files"])
    for lang, c in copy.items():
        n_caps = len([x for x in c["captions"].split("\n") if x.strip()])
        if n_caps != n_shots:
            warnings.append(f"{lang}: {n_caps} captions for {n_shots} screenshots")

    slots = {}
    for key, entries in SLOTS.items():
        rows = []
        for label, size, fname in entries:
            exists = (gfx_dir / fname).exists()
            if not exists:
                warnings.append(f"missing image for {label} {size}: {fname}")
            rows.append({"label": label, "size": size, "file": fname, "ok": exists})
        slots[key] = {"path": gfx_rel, "cmd": f"explorer {gfx_rel}", "rows": rows}

    return {
        "version": VERSION,
        "listings": [{"row": r, "lang": l, "note": n} for r, l, n in LISTINGS],
        "langName": LANG_NAME,
        "nameNotes": PRODUCT_NAME_NOTES,
        "blocks": [{"kind": k, "key": key, "label": lb, "cap": c, "note": n} for k, key, lb, c, n in BLOCKS],
        "copy": copy,
        "folders": folders,
        "slots": slots,
        "packages": [
            f"dist\\RORORO-Store-x64-{VERSION}.msix",
            f"dist\\RORORO-Store-arm64-{VERSION}.msix",
        ],
        # Not a listing field - it goes in the submission's Notes for certification, once per
        # release rather than once per language, so it sits at the bottom below the packages.
        "reviewerLetter": reviewer_letter(),
    }, warnings


HTML = """<meta charset="utf-8">
<title>RoRoRo Listing Console</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Space+Grotesk:wght@500;700&family=Inter:wght@400;500;600&family=JetBrains+Mono:wght@400;500&display=swap">
<style>
  /* OLED dark, single theme by request: a true-black ground so the panel's pixels are actually
     off, with the 626 brand cyan and magenta carrying the accents. Committed deliberately rather
     than following the host - so every colour is painted from a token and the page holds its own
     even when it opens on a light ground. The whole file is intentionally pure ASCII; see the
     ensure_ascii note in the generator. */
  :root {
    --ground: #000000;
    --surface: #0a0e13;
    --surface-2: #131a22;
    --line: #212a34;
    --ink: #e9f1f8;
    --ink-soft: #a3b6c8;
    --ink-mute: #75899c;
    --cyan: #17d4fa;
    --magenta: #f22f89;
    --ok: #4ade9b;
    --warn: #f0b429;
    --over: #ff6b5e;
    --radius: 10px;
    --shadow: 0 0 0 1px rgba(23,212,250,.05), 0 12px 34px rgba(0,0,0,.7);
    color-scheme: dark;
  }
  * { box-sizing: border-box; }
  body { margin: 0; background: var(--ground); color: var(--ink);
    font-family: Inter, "Segoe UI", system-ui, sans-serif; font-size: 15px; line-height: 1.6; }
  .wrap { max-width: 1080px; margin: 0 auto; padding: 0 24px 96px; }

  header.top { position: sticky; top: 0; z-index: 20; background: rgba(0,0,0,.92);
    backdrop-filter: blur(8px); border-bottom: 1px solid var(--line); }
  .top-inner { max-width: 1080px; margin: 0 auto; padding: 14px 24px; display: flex; flex-wrap: wrap; gap: 16px; align-items: center; }
  .brand { display: flex; flex-direction: column; gap: 2px; margin-right: auto; }
  .brand h1 { margin: 0; font-family: "Space Grotesk", Inter, sans-serif; font-weight: 700; font-size: 19px; letter-spacing: -.01em; }
  .brand .sub { font-family: "JetBrains Mono", ui-monospace, monospace; font-size: 11px; color: var(--ink-mute); letter-spacing: .04em; }
  .control { display: flex; align-items: center; gap: 8px; }
  .control label { font-size: 11px; text-transform: uppercase; letter-spacing: .09em; color: var(--ink-mute); font-weight: 600; }
  select { font: inherit; font-weight: 600; color: var(--ink); background: var(--surface);
    border: 1px solid var(--line); border-radius: 8px; padding: 8px 12px; min-width: 210px; }
  select:focus-visible, button:focus-visible { outline: 2px solid var(--cyan); outline-offset: 2px; }
  .toggle { display: inline-flex; align-items: center; gap: 8px; cursor: pointer; user-select: none;
    background: var(--surface); border: 1px solid var(--line); border-radius: 8px; padding: 8px 12px; font-size: 13px; font-weight: 600; }
  .toggle input { accent-color: var(--cyan); width: 15px; height: 15px; margin: 0; }

  .progress { display: flex; flex-wrap: wrap; gap: 6px; margin: 18px 0 4px; }
  .chip { font-family: "JetBrains Mono", monospace; font-size: 11px; padding: 4px 9px; border-radius: 999px;
    border: 1px solid var(--line); color: var(--ink-mute); background: var(--surface); cursor: pointer; }
  .chip[aria-current="true"] { border-color: var(--cyan); color: var(--cyan); font-weight: 500; }
  .chip.done { border-color: var(--ok); color: var(--ok); }
  .chip.done::before { content: "\\2713\\00a0"; }

  .rowbanner { display: flex; flex-wrap: wrap; gap: 10px 18px; align-items: center;
    margin: 20px 0 4px; padding: 14px 16px; border-radius: var(--radius);
    background: var(--surface-2); border-left: 3px solid var(--cyan); }
  .rowbanner strong { font-family: "Space Grotesk", sans-serif; font-size: 16px; }
  .rowbanner .note { font-size: 13px; color: var(--ink-soft); margin-right: auto; }

  h3.rule { font-family: "Space Grotesk", sans-serif; font-size: 12px; text-transform: uppercase; letter-spacing: .12em;
    color: var(--ink-mute); margin: 34px 0 0; padding-bottom: 8px; border-bottom: 1px solid var(--line); }

  section.field { background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius);
    margin-top: 14px; box-shadow: var(--shadow); overflow: hidden; }
  section.field.skip { opacity: .62; }
  .fhead { display: flex; flex-wrap: wrap; gap: 10px; align-items: center; padding: 13px 16px; border-bottom: 1px solid var(--line); }
  .fhead h2 { margin: 0; font-family: "Space Grotesk", sans-serif; font-size: 15px; font-weight: 700; margin-right: auto; }
  .fnote { font-size: 12px; color: var(--ink-mute); flex-basis: 100%; order: 3; }
  .fnote.warn { color: var(--warn); font-weight: 500; }
  .count { font-family: "JetBrains Mono", monospace; font-size: 11px; padding: 3px 8px; border-radius: 6px;
    background: var(--surface-2); color: var(--ink-soft); font-variant-numeric: tabular-nums; }
  .count.ok { color: var(--ok); } .count.warn { color: var(--warn); } .count.over { color: var(--over); font-weight: 600; }

  button.copy { font: inherit; font-size: 12px; font-weight: 600; cursor: pointer;
    background: var(--cyan); color: #000; border: 0; border-radius: 7px; padding: 6px 13px; }
  button.copy:hover { filter: brightness(1.1); }
  button.copy.done { background: var(--ok); }
  button.ghost { background: transparent; color: var(--cyan); border: 1px solid var(--line); }

  pre.text { margin: 0; padding: 14px 16px; white-space: pre-wrap; word-break: break-word;
    font-family: Inter, sans-serif; font-size: 14px; line-height: 1.62; color: var(--ink); max-height: 340px; overflow: auto; }
  .english { border-top: 1px dashed var(--line); background: var(--surface-2); }
  .english .tag { font-family: "JetBrains Mono", monospace; font-size: 10px; letter-spacing: .1em; text-transform: uppercase;
    color: var(--ink-mute); padding: 9px 16px 0; display: block; }
  .english pre.text { color: var(--ink-soft); font-size: 13px; max-height: 260px; }

  ol.items { margin: 0; padding: 6px 16px 12px 0; list-style: none; }
  ol.items li { display: flex; gap: 10px; align-items: flex-start; padding: 7px 0 7px 16px; border-bottom: 1px solid var(--line); }
  ol.items li:last-child { border-bottom: 0; }
  .fnum { font-family: "JetBrains Mono", monospace; font-size: 11px; color: var(--ink-mute); min-width: 20px; padding-top: 3px; font-variant-numeric: tabular-nums; }
  .ftext { flex: 1; font-size: 14px; }
  .fen { display: block; color: var(--ink-mute); font-size: 12.5px; margin-top: 3px; }
  /* Magenta is the brand's second accent and has no other job on this page, so it marks the one
     thing in a row that is NOT pasted text: the file you upload alongside the caption. */
  .shotfile { display: block; font-family: "JetBrains Mono", monospace; font-size: 11px;
    color: var(--magenta); letter-spacing: .02em; margin-bottom: 3px; }
  .shotthumb { width: 132px; flex: 0 0 132px; border-radius: 5px; border: 1px solid var(--line);
    cursor: zoom-in; background: var(--ground); display: block; }
  .shotthumb:hover { border-color: var(--cyan); }

  details.letter { border-top: 1px solid var(--line); }
  details.letter > summary { cursor: pointer; padding: 12px 16px; font-size: 13px; font-weight: 600;
    color: var(--cyan); list-style: none; }
  details.letter > summary::-webkit-details-marker { display: none; }
  details.letter > summary::before { content: "\\25B8\\00a0"; display: inline-block; transition: transform .15s; }
  details.letter[open] > summary::before { content: "\\25BE\\00a0"; }
  details.letter pre.text { max-height: 520px; font-family: "JetBrains Mono", monospace; font-size: 12.5px; line-height: 1.65; }

  table.slots { width: 100%; border-collapse: collapse; }
  table.slots td { padding: 9px 16px; border-bottom: 1px solid var(--line); font-size: 13.5px; vertical-align: middle; }
  table.slots tr:last-child td { border-bottom: 0; }
  td.slotsize { font-family: "JetBrains Mono", monospace; font-size: 11.5px; color: var(--ink-mute); white-space: nowrap; font-variant-numeric: tabular-nums; }
  td.slotfile { font-family: "JetBrains Mono", monospace; font-size: 12px; color: var(--cyan); word-break: break-all; }
  td.slotfile.missing { color: var(--over); }

  .pathrow { display: flex; flex-wrap: wrap; gap: 10px; align-items: center; padding: 12px 16px; }
  code.path { font-family: "JetBrains Mono", monospace; font-size: 12px; background: var(--surface-2);
    padding: 6px 10px; border-radius: 6px; color: var(--ink); flex: 1; min-width: 240px; overflow-x: auto; white-space: nowrap; }
  .files { display: flex; flex-wrap: wrap; gap: 5px; padding: 0 16px 14px; }
  .file { font-family: "JetBrains Mono", monospace; font-size: 11px; color: var(--ink-soft); background: var(--surface-2); border-radius: 5px; padding: 3px 7px; }
  .hint { font-size: 12.5px; color: var(--ink-mute); padding: 0 16px 14px; margin: 0; }
  @media (prefers-reduced-motion: reduce) { * { transition: none !important; } }
</style>

<header class="top">
  <div class="top-inner">
    <div class="brand">
      <h1>Listing Console</h1>
      <span class="sub" id="ver"></span>
    </div>
    <div class="control">
      <label for="pick">Listing</label>
      <select id="pick"></select>
    </div>
    <label class="toggle"><input type="checkbox" id="showen"> Show English</label>
  </div>
</header>

<div class="wrap">
  <div class="progress" id="prog"></div>
  <div class="rowbanner" id="banner"></div>
  <main id="fields"></main>

  <h3 class="rule">Packages to upload</h3>
  <div id="packages"></div>

  <h3 class="rule">Notes for certification</h3>
  <div id="letter"></div>
</div>

<script>
const DATA = __DATA__;
const $ = (s) => document.querySelector(s);
const store = {
  get(k, d) { try { const v = localStorage.getItem(k); return v === null ? d : JSON.parse(v); } catch (e) { return d; } },
  set(k, v) { try { localStorage.setItem(k, JSON.stringify(v)); } catch (e) {} }
};

let current = store.get("rororo.listing", 0);
if (typeof current !== "number" || current < 0 || current >= DATA.listings.length) current = 0;
let showEn = store.get("rororo.showen", false);
let done = store.get("rororo.done", {});

$("#ver").textContent = "v" + DATA.version + " \\u00b7 Partner Center order";

const pick = $("#pick");
DATA.listings.forEach((l, i) => {
  const o = document.createElement("option");
  o.value = String(i); o.textContent = l.row; pick.appendChild(o);
});
pick.value = String(current);
$("#showen").checked = showEn;

function copyText(text, btn) {
  navigator.clipboard.writeText(text).then(() => {
    const was = btn.textContent;
    btn.textContent = "Copied"; btn.classList.add("done");
    setTimeout(() => { btn.textContent = was; btn.classList.remove("done"); }, 1200);
  }).catch(() => { btn.textContent = "Copy failed"; });
}
function countClass(len, cap) {
  if (!cap) return "";
  if (len > cap) return "over";
  if (len > cap * 0.92) return "warn";
  return "ok";
}
function makeBtn(label, text, cls) {
  const b = document.createElement("button");
  b.className = "copy" + (cls ? " " + cls : "");
  b.textContent = label;
  b.addEventListener("click", () => copyText(text, b));
  return b;
}
function head(label, extras) {
  const h = document.createElement("div");
  h.className = "fhead";
  const t = document.createElement("h2");
  t.textContent = label; h.appendChild(t);
  (extras || []).forEach((e) => h.appendChild(e));
  return h;
}
function noteEl(text, warn) {
  const n = document.createElement("span");
  n.className = "fnote" + (warn ? " warn" : "");
  n.textContent = text;
  return n;
}

function render() {
  const listing = DATA.listings[current];
  const lang = listing.lang;
  const copy = DATA.copy[lang];
  const en = DATA.copy.en;

  const banner = $("#banner");
  banner.innerHTML = "";
  const strong = document.createElement("strong");
  strong.textContent = listing.row; banner.appendChild(strong);
  const meta = document.createElement("span");
  meta.className = "note";
  meta.textContent = DATA.langName[lang] + (listing.note ? " \\u00b7 " + listing.note : "");
  banner.appendChild(meta);
  const mark = document.createElement("button");
  mark.className = "copy ghost";
  mark.textContent = done[listing.row] ? "Done \\u2713" : "Mark done";
  mark.addEventListener("click", () => { done[listing.row] = !done[listing.row]; store.set("rororo.done", done); render(); });
  banner.appendChild(mark);

  const prog = $("#prog");
  prog.innerHTML = "";
  DATA.listings.forEach((l, i) => {
    const c = document.createElement("button");
    c.className = "chip" + (done[l.row] ? " done" : "");
    if (i === current) c.setAttribute("aria-current", "true");
    c.textContent = l.row;
    c.addEventListener("click", () => { current = i; store.set("rororo.listing", i); pick.value = String(i); render(); });
    prog.appendChild(c);
  });

  const host = $("#fields");
  host.innerHTML = "";

  DATA.blocks.forEach((b) => {
    if (b.kind === "section") {
      const h3 = document.createElement("h3");
      h3.className = "rule"; h3.textContent = b.label; host.appendChild(h3);
      return;
    }
    const sec = document.createElement("section");
    sec.className = "field" + (b.kind === "skip" ? " skip" : "");

    if (b.kind === "skip") {
      sec.appendChild(head(b.label, []));
      if (b.note) { const p = document.createElement("p"); p.className = "hint"; p.textContent = b.note; sec.appendChild(p); }
      host.appendChild(sec); return;
    }

    if (b.kind === "folder") {
      const f = DATA.folders[b.key];
      sec.appendChild(head(b.label, [
        Object.assign(document.createElement("span"), { className: "count", textContent: f.files.length + " files" }),
        makeBtn("Copy command", f.cmd)
      ]));
      const row = document.createElement("div"); row.className = "pathrow";
      const c = document.createElement("code"); c.className = "path"; c.textContent = f.cmd; row.appendChild(c);
      row.appendChild(makeBtn("Copy path", f.path, "ghost"));
      sec.appendChild(row);
      // Each row pairs the file being uploaded with the caption that goes under it, in upload
      // order, so the two never drift apart while working down the form.
      const caps = (copy.captions || "").split("\\n").filter((x) => x.trim());
      const enCaps = (en.captions || "").split("\\n").filter((x) => x.trim());
      const ol = document.createElement("ol"); ol.className = "items";
      f.files.forEach((file, i) => {
        const cap = caps[i] || "";
        const li = document.createElement("li");
        const num = document.createElement("span"); num.className = "fnum";
        num.textContent = String(i + 1).padStart(2, "0"); li.appendChild(num);
        // The thumbnail is the point of this row: ten filenames all look alike, one glance at the
        // frame does not. Clicking opens it full size in a new tab.
        const thumb = document.createElement("img");
        thumb.className = "shotthumb"; thumb.src = file.thumb; thumb.alt = cap || file.name;
        thumb.loading = "lazy";
        thumb.addEventListener("click", () => {
          const w = window.open();
          if (w) { w.document.write('<img src="' + file.thumb + '" style="width:100%">'); w.document.close(); }
        });
        li.appendChild(thumb);
        const tx = document.createElement("div"); tx.className = "ftext";
        const fn = document.createElement("span"); fn.className = "shotfile"; fn.textContent = file.name; tx.appendChild(fn);
        const cp = document.createElement("span"); cp.textContent = cap || "(no caption)"; tx.appendChild(cp);
        if (showEn && lang !== "en" && enCaps[i] && enCaps[i] !== cap) {
          const s2 = document.createElement("span"); s2.className = "fen"; s2.textContent = enCaps[i]; tx.appendChild(s2);
        }
        li.appendChild(tx);
        const cc = document.createElement("span");
        cc.className = "count " + countClass(cap.length, b.cap); cc.textContent = cap.length; li.appendChild(cc);
        li.appendChild(makeBtn("Copy", cap));
        ol.appendChild(li);
      });
      sec.appendChild(ol);
      const p = document.createElement("p"); p.className = "hint";
      p.textContent = (b.note ? b.note + " " : "") + "A browser can't open a local folder - paste the command in a terminal at the repo root.";
      sec.appendChild(p);
      host.appendChild(sec); return;
    }

    if (b.kind === "slots") {
      const s = DATA.slots[b.key];
      sec.appendChild(head(b.label, [makeBtn("Copy command", s.cmd, "ghost")]));
      const tbl = document.createElement("table"); tbl.className = "slots";
      s.rows.forEach((r) => {
        const tr = document.createElement("tr");
        const a = document.createElement("td"); a.textContent = r.label; tr.appendChild(a);
        const sz = document.createElement("td"); sz.className = "slotsize"; sz.textContent = r.size; tr.appendChild(sz);
        const fl = document.createElement("td"); fl.className = "slotfile" + (r.ok ? "" : " missing");
        fl.textContent = r.ok ? r.file : r.file + " (MISSING)"; tr.appendChild(fl);
        tbl.appendChild(tr);
      });
      sec.appendChild(tbl);
      if (b.note) { const p = document.createElement("p"); p.className = "hint"; p.textContent = b.note; sec.appendChild(p); }
      host.appendChild(sec); return;
    }

    const val = copy[b.key] || "";
    const enVal = en[b.key] || "";

    if (b.kind === "list") {
      const entries = val.split("\\n").filter((x) => x.trim());
      const enEntries = enVal.split("\\n").filter((x) => x.trim());
      sec.appendChild(head(b.label, [
        Object.assign(document.createElement("span"), { className: "count", textContent: entries.length + (b.key === "keywords" ? " / 7" : " / 20") }),
        makeBtn("Copy all", val)
      ]));
      if (b.note) sec.lastChild.appendChild(noteEl(b.note));
      const ol = document.createElement("ol"); ol.className = "items";
      entries.forEach((e, i) => {
        const li = document.createElement("li");
        const num = document.createElement("span"); num.className = "fnum"; num.textContent = String(i + 1).padStart(2, "0"); li.appendChild(num);
        const tx = document.createElement("div"); tx.className = "ftext"; tx.textContent = e;
        if (showEn && lang !== "en" && enEntries[i] && enEntries[i] !== e) {
          const s2 = document.createElement("span"); s2.className = "fen"; s2.textContent = enEntries[i]; tx.appendChild(s2);
        }
        li.appendChild(tx);
        const cc = document.createElement("span"); cc.className = "count " + countClass(e.length, b.cap); cc.textContent = e.length; li.appendChild(cc);
        li.appendChild(makeBtn("Copy", e));
        ol.appendChild(li);
      });
      sec.appendChild(ol);
      host.appendChild(sec); return;
    }

    // text
    const extras = [
      Object.assign(document.createElement("span"), {
        className: "count " + countClass(val.length, b.cap),
        textContent: b.cap ? val.length + " / " + b.cap : val.length + " chars"
      }),
      makeBtn("Copy", val)
    ];
    const h = head(b.label, extras);
    if (b.note) h.appendChild(noteEl(b.note));
    if (b.key === "productname" && DATA.nameNotes[lang]) h.appendChild(noteEl(DATA.nameNotes[lang], true));
    sec.appendChild(h);
    const pre = document.createElement("pre"); pre.className = "text"; pre.textContent = val; sec.appendChild(pre);
    if (showEn && lang !== "en" && enVal && enVal !== val) {
      const w = document.createElement("div"); w.className = "english";
      const tag = document.createElement("span"); tag.className = "tag"; tag.textContent = "English reference"; w.appendChild(tag);
      const p2 = document.createElement("pre"); p2.className = "text"; p2.textContent = enVal; w.appendChild(p2);
      sec.appendChild(w);
    }
    host.appendChild(sec);
  });
}

const pkgSec = document.createElement("section");
pkgSec.className = "field";
DATA.packages.forEach((p) => {
  const row = document.createElement("div"); row.className = "pathrow";
  const c = document.createElement("code"); c.className = "path"; c.textContent = p; row.appendChild(c);
  row.appendChild(makeBtn("Copy", p));
  pkgSec.appendChild(row);
});
const pkgHint = document.createElement("p");
pkgHint.className = "hint";
pkgHint.textContent = "Both go in the Packages slot. Unsigned by design \\u2014 Partner Center signs after upload.";
pkgSec.appendChild(pkgHint);
$("#packages").appendChild(pkgSec);

// The reviewer letter is per-release, not per-language, so it lives below the listings rather
// than inside them. Collapsed by default: it is long, and it is read once per submission.
const letterSec = document.createElement("section");
letterSec.className = "field";
if (DATA.reviewerLetter) {
  letterSec.appendChild(head("Letter to the Store reviewer \\u00b7 v" + DATA.version, [
    Object.assign(document.createElement("span"), {
      className: "count", textContent: DATA.reviewerLetter.length + " chars"
    }),
    makeBtn("Copy", DATA.reviewerLetter)
  ]));
  const det = document.createElement("details");
  det.className = "letter";
  const sum = document.createElement("summary");
  sum.textContent = "Read it";
  det.appendChild(sum);
  const pre = document.createElement("pre");
  pre.className = "text"; pre.textContent = DATA.reviewerLetter;
  det.appendChild(pre);
  letterSec.appendChild(det);
  const lh = document.createElement("p"); lh.className = "hint";
  lh.textContent = "Goes in Notes for certification on the submission page \\u2014 once per release, English only.";
  letterSec.appendChild(lh);
} else {
  letterSec.appendChild(head("Letter to the Store reviewer", []));
  const lh = document.createElement("p"); lh.className = "hint";
  lh.textContent = "Not written yet for v" + DATA.version + " \\u2014 expected at docs/store/reviewer-letter-" + DATA.version + ".md";
  letterSec.appendChild(lh);
}
$("#letter").appendChild(letterSec);

pick.addEventListener("change", () => { current = Number(pick.value); store.set("rororo.listing", current); render(); });
$("#showen").addEventListener("change", (e) => { showEn = e.target.checked; store.set("rororo.showen", showEn); render(); });
render();
</script>
"""


def main() -> None:
    data, warnings = build_data()
    # ensure_ascii deliberately: every non-ASCII character in the listing copy ships as a \\uXXXX
    # escape, so the six translated languages render correctly no matter what encoding the document
    # is parsed as. Not cosmetic - a misdecoded document puts mojibake in the DOM, and the copy
    # buttons would then copy mojibake straight into the Store listing.
    payload = json.dumps(data, ensure_ascii=True).replace("<", "\\u003c")
    OUT.write_text(HTML.replace("__DATA__", payload), encoding="ascii")
    print(f"wrote {OUT.relative_to(ROOT)} - {len(data['listings'])} listings, "
          f"{len(data['copy'])} copy sets, {len(data['blocks'])} blocks")
    for w in warnings:
        print(f"  WARNING {w}")


if __name__ == "__main__":
    main()
