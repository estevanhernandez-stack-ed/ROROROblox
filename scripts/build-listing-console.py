#!/usr/bin/env python3
"""Generate the Store Listing Console — the paste-helper for Partner Center submissions.

Partner Center takes one listing per language, and each listing wants the same eight fields typed
or pasted in one at a time. With six translated languages across nine listing rows that is a lot of
switching between an editor and a browser, in languages the operator cannot proofread by eye.

This builds a single self-contained page that holds every field for every listing, with the English
original one toggle away, character counts against the real caps, and per-entry copy for the
product features (the field you paste eighteen times).

    python scripts/build-listing-console.py          # -> docs/store/listing-console.html

Re-run it after any listing-copy change; the page is generated, never hand-edited.
"""
from __future__ import annotations

import json
import re
from pathlib import Path

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


def block_after(text: str, heading_pattern: str) -> str:
    """The first fenced block following a heading."""
    m = re.search(heading_pattern + r".*?\n```\n(.*?)\n```", text, re.S)
    return m.group(1).strip() if m else ""


def parse_english() -> dict:
    t = (STORE / "listing-copy.md").read_text(encoding="utf-8")
    wn = (STORE / "whats-new-1.27.0.0.md").read_text(encoding="utf-8")
    return {
        "short": block_after(t, r"## Short description"),
        "long": block_after(t, r"## Long description"),
        "features": block_after(t, r"## Product features"),
        "whatsnew": block_after(wn, r"## English"),
        "copyright": block_after(t, r"## Copyright"),
        "trademark": block_after(t, r"## Trademark info"),
        "license": block_after(t, r"## Additional license terms"),
        "developedby": block_after(t, r"## Developed by"),
        "keywords": block_after(t, r"## Keywords"),
    }


def parse_language(lang: str, en: dict) -> dict:
    t = (STORE / f"listing-copy-{lang}.md").read_text(encoding="utf-8")
    return {
        "short": block_after(t, r"## Short description"),
        "long": block_after(t, r"## Long description"),
        "features": block_after(t, r"## Product features"),
        "whatsnew": block_after(t, r"## What's new in this version"),
        "copyright": block_after(t, r"## Copyright"),
        "trademark": block_after(t, r"## Trademark info"),
        # Not translated in the repo — Partner Center takes the English for these.
        "license": en["license"],
        "developedby": en["developedby"],
        "keywords": en["keywords"],
    }


# Field order matches the order Partner Center asks for them. cap=None means no hard cap.
FIELDS = [
    ("short", "Short description", 200, "The Store snippet. Hard cap."),
    ("long", "Description", None, "The main listing body."),
    ("whatsnew", "What's new in this version", 1500, "Public — a different field from Notes for certification."),
    ("features", "Product features", 200, "Up to 20 entries, each capped separately. Paste one at a time."),
    ("copyright", "Copyright", None, None),
    ("trademark", "Trademark info", None, None),
    ("license", "Additional license terms", None, "English on file for every listing."),
    ("developedby", "Developed by", None, "English on file for every listing."),
    ("keywords", "Search terms", None, "English on file for every listing."),
]

ASSETS = [
    ("Screenshots", STORE / "screenshots", "Store listing screenshots."),
    ("Graphics", STORE / "graphics", "Box art, display icons, hero and poster images."),
]


def build_data() -> dict:
    en = parse_english()
    copy = {"en": en}
    for lang in ("fr", "de", "ru", "pt-br", "pl", "es"):
        copy[lang] = parse_language(lang, en)

    assets = []
    for label, folder, note in ASSETS:
        files = sorted(p.name for p in folder.iterdir() if p.suffix.lower() in {".png", ".jpg", ".jpeg"})
        # Repo-RELATIVE, deliberately. An absolute path would carry this machine's user name into a
        # public repo (the pre-commit local-path guard rejects exactly that) and be wrong on anyone
        # else's checkout. The paired command is also better than a path: run from the repo root, it
        # opens the folder instead of asking you to paste into an address bar.
        rel = str(folder.relative_to(ROOT)).replace("/", "\\")
        assets.append({
            "label": label,
            "path": rel,
            "cmd": f"explorer {rel}",
            "note": note,
            "files": files,
        })

    return {
        "version": VERSION,
        "listings": [{"row": r, "lang": l, "note": n} for r, l, n in LISTINGS],
        "langName": LANG_NAME,
        "fields": [{"key": k, "label": lb, "cap": c, "note": n} for k, lb, c, n in FIELDS],
        "copy": copy,
        "assets": assets,
        "packages": [
            f"dist\\RORORO-Store-x64-{VERSION}.msix",
            f"dist\\RORORO-Store-arm64-{VERSION}.msix",
        ],
    }


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
    --cyan-bright: #5fe4ff;
    --magenta: #f22f89;
    --ok: #4ade9b;
    --warn: #f0b429;
    --over: #ff6b5e;
    --radius: 10px;
    --shadow: 0 0 0 1px rgba(23,212,250,.05), 0 12px 34px rgba(0,0,0,.7);
    color-scheme: dark;
  }

  * { box-sizing: border-box; }
  body {
    margin: 0; background: var(--ground); color: var(--ink);
    font-family: Inter, "Segoe UI", system-ui, sans-serif;
    font-size: 15px; line-height: 1.6;
  }
  .wrap { max-width: 1080px; margin: 0 auto; padding: 0 24px 96px; }

  header.top {
    position: sticky; top: 0; z-index: 20;
    background: color-mix(in srgb, var(--ground) 92%, transparent);
    backdrop-filter: blur(8px);
    border-bottom: 1px solid var(--line);
  }
  .top-inner { max-width: 1080px; margin: 0 auto; padding: 14px 24px; display: flex; flex-wrap: wrap; gap: 16px; align-items: center; }
  .brand { display: flex; flex-direction: column; gap: 2px; margin-right: auto; }
  .brand h1 {
    margin: 0; font-family: "Space Grotesk", Inter, sans-serif; font-weight: 700;
    font-size: 19px; letter-spacing: -.01em;
  }
  .brand .sub { font-family: "JetBrains Mono", ui-monospace, monospace; font-size: 11px; color: var(--ink-mute); letter-spacing: .04em; }

  .control { display: flex; align-items: center; gap: 8px; }
  .control label { font-size: 11px; text-transform: uppercase; letter-spacing: .09em; color: var(--ink-mute); font-weight: 600; }
  select {
    font: inherit; font-weight: 600; color: var(--ink); background: var(--surface);
    border: 1px solid var(--line); border-radius: 8px; padding: 8px 12px; min-width: 210px;
  }
  select:focus-visible, button:focus-visible { outline: 2px solid var(--cyan); outline-offset: 2px; }

  .toggle { display: inline-flex; align-items: center; gap: 8px; cursor: pointer; user-select: none;
    background: var(--surface); border: 1px solid var(--line); border-radius: 8px; padding: 8px 12px; font-size: 13px; font-weight: 600; }
  .toggle input { accent-color: var(--cyan); width: 15px; height: 15px; margin: 0; }

  .rowbanner {
    display: flex; flex-wrap: wrap; gap: 10px 18px; align-items: baseline;
    margin: 26px 0 4px; padding: 14px 16px; border-radius: var(--radius);
    background: var(--surface-2); border-left: 3px solid var(--cyan);
  }
  .rowbanner strong { font-family: "Space Grotesk", sans-serif; font-size: 16px; }
  .rowbanner .note { font-size: 13px; color: var(--ink-soft); }

  .progress { display: flex; flex-wrap: wrap; gap: 6px; margin: 18px 0 4px; }
  .chip {
    font-family: "JetBrains Mono", monospace; font-size: 11px; padding: 4px 9px; border-radius: 999px;
    border: 1px solid var(--line); color: var(--ink-mute); background: var(--surface); cursor: pointer;
  }
  .chip[aria-current="true"] { border-color: var(--cyan); color: var(--cyan); font-weight: 500; }
  .chip.done { border-color: var(--ok); color: var(--ok); }
  .chip.done::before { content: "\\2713\\00a0"; }

  section.field {
    background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius);
    margin-top: 16px; box-shadow: var(--shadow); overflow: hidden;
  }
  .fhead { display: flex; flex-wrap: wrap; gap: 10px; align-items: center; padding: 13px 16px; border-bottom: 1px solid var(--line); }
  .fhead h2 { margin: 0; font-family: "Space Grotesk", sans-serif; font-size: 15px; font-weight: 700; margin-right: auto; }
  .fnote { font-size: 12px; color: var(--ink-mute); flex-basis: 100%; order: 3; }
  .count { font-family: "JetBrains Mono", monospace; font-size: 11px; padding: 3px 8px; border-radius: 6px;
    background: var(--surface-2); color: var(--ink-soft); font-variant-numeric: tabular-nums; }
  .count.ok { color: var(--ok); }
  .count.warn { color: var(--warn); }
  .count.over { color: var(--over); font-weight: 600; }

  button.copy {
    font: inherit; font-size: 12px; font-weight: 600; cursor: pointer;
    background: var(--cyan); color: var(--ground); border: 0; border-radius: 7px; padding: 6px 13px;
  }
  button.copy:hover { filter: brightness(1.08); }
  button.copy.done { background: var(--ok); }
  button.ghost { background: transparent; color: var(--cyan); border: 1px solid var(--line); }

  pre.text {
    margin: 0; padding: 14px 16px; white-space: pre-wrap; word-break: break-word;
    font-family: Inter, sans-serif; font-size: 14px; line-height: 1.62; color: var(--ink);
    max-height: 340px; overflow: auto;
  }
  .english {
    border-top: 1px dashed var(--line); background: var(--surface-2);
  }
  .english .tag {
    font-family: "JetBrains Mono", monospace; font-size: 10px; letter-spacing: .1em; text-transform: uppercase;
    color: var(--ink-mute); padding: 9px 16px 0; display: block;
  }
  .english pre.text { color: var(--ink-soft); font-size: 13px; max-height: 260px; }

  ol.feats { margin: 0; padding: 8px 16px 14px 0; list-style: none; }
  ol.feats li { display: flex; gap: 10px; align-items: flex-start; padding: 7px 0 7px 16px; border-bottom: 1px solid var(--line); }
  ol.feats li:last-child { border-bottom: 0; }
  .fnum { font-family: "JetBrains Mono", monospace; font-size: 11px; color: var(--ink-mute); min-width: 20px; padding-top: 3px; font-variant-numeric: tabular-nums; }
  .ftext { flex: 1; font-size: 14px; }
  .fen { display: block; color: var(--ink-mute); font-size: 12.5px; margin-top: 3px; }

  h3.rule {
    font-family: "Space Grotesk", sans-serif; font-size: 12px; text-transform: uppercase; letter-spacing: .12em;
    color: var(--ink-mute); margin: 40px 0 0; padding-bottom: 8px; border-bottom: 1px solid var(--line);
  }
  .pathrow { display: flex; flex-wrap: wrap; gap: 10px; align-items: center; padding: 12px 16px; }
  code.path {
    font-family: "JetBrains Mono", monospace; font-size: 12px; background: var(--surface-2);
    padding: 6px 10px; border-radius: 6px; color: var(--ink); flex: 1; min-width: 260px; overflow-x: auto; white-space: nowrap;
  }
  .files { display: flex; flex-wrap: wrap; gap: 5px; padding: 0 16px 14px; }
  .file { font-family: "JetBrains Mono", monospace; font-size: 11px; color: var(--ink-soft); background: var(--surface-2); border-radius: 5px; padding: 3px 7px; }
  .hint { font-size: 12.5px; color: var(--ink-mute); padding: 0 16px 14px; }

  ol.steps { margin: 0; padding: 4px 16px 16px 34px; }
  ol.steps li { padding: 6px 0; }
  ol.steps code { font-family: "JetBrains Mono", monospace; font-size: 12px; background: var(--surface-2); padding: 2px 6px; border-radius: 5px; }

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

  <h3 class="rule">Asset folders</h3>
  <div id="assets"></div>

  <h3 class="rule">Packages to upload</h3>
  <div id="packages"></div>

  <h3 class="rule">Order of operations</h3>
  <section class="field">
    <ol class="steps">
      <li><strong>Packages</strong> &mdash; upload both MSIX above, wait for validation.</li>
      <li><strong>Notes for certification</strong> &mdash; from <code>reviewer-letter-1.27.0.0.md</code>. Reviewer-only, not public.</li>
      <li><strong>Store listings &rarr; [language]</strong> &mdash; work the fields below, one listing at a time.</li>
      <li><strong>What's new</strong> is a <em>different field</em> from Notes for certification. Both get filled, every release.</li>
      <li><strong>Submit.</strong> In submission &rarr; Certification &rarr; Publishing, typically 24&ndash;72h.</li>
    </ol>
  </section>
</div>

<script>
const DATA = __DATA__;

const $ = (s) => document.querySelector(s);
const store = {
  get(k, d) { try { const v = localStorage.getItem(k); return v === null ? d : JSON.parse(v); } catch (e) { return d; } },
  set(k, v) { try { localStorage.setItem(k, JSON.stringify(v)); } catch (e) { /* private window */ } }
};

let current = store.get("rororo.listing", 0);
if (typeof current !== "number" || current < 0 || current >= DATA.listings.length) current = 0;
let showEn = store.get("rororo.showen", false);
let done = store.get("rororo.done", {});

$("#ver").textContent = "v" + DATA.version + " \\u00b7 Partner Center";

const pick = $("#pick");
DATA.listings.forEach((l, i) => {
  const o = document.createElement("option");
  o.value = String(i);
  o.textContent = l.row;
  pick.appendChild(o);
});
pick.value = String(current);
$("#showen").checked = showEn;

function copyText(text, btn) {
  navigator.clipboard.writeText(text).then(() => {
    const was = btn.textContent;
    btn.textContent = "Copied";
    btn.classList.add("done");
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

function render() {
  const listing = DATA.listings[current];
  const lang = listing.lang;
  const copy = DATA.copy[lang];
  const en = DATA.copy.en;

  // banner
  const banner = $("#banner");
  banner.innerHTML = "";
  const strong = document.createElement("strong");
  strong.textContent = listing.row;
  banner.appendChild(strong);
  const meta = document.createElement("span");
  meta.className = "note";
  meta.textContent = DATA.langName[lang] + (listing.note ? " \\u00b7 " + listing.note : "");
  banner.appendChild(meta);
  const mark = document.createElement("button");
  mark.className = "copy ghost";
  mark.textContent = done[listing.row] ? "Done \\u2713" : "Mark done";
  mark.addEventListener("click", () => {
    done[listing.row] = !done[listing.row];
    store.set("rororo.done", done);
    render();
  });
  banner.appendChild(mark);

  // progress chips
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

  // fields
  const host = $("#fields");
  host.innerHTML = "";
  DATA.fields.forEach((f) => {
    const val = copy[f.key] || "";
    const enVal = en[f.key] || "";
    const sec = document.createElement("section");
    sec.className = "field";

    const head = document.createElement("div");
    head.className = "fhead";
    const h = document.createElement("h2");
    h.textContent = f.label;
    head.appendChild(h);

    if (f.key === "features") {
      const entries = val.split("\\n").filter((x) => x.trim());
      const cnt = document.createElement("span");
      cnt.className = "count";
      cnt.textContent = entries.length + " entries / 20";
      head.appendChild(cnt);
      head.appendChild(makeBtn("Copy all", val));
    } else {
      const cnt = document.createElement("span");
      cnt.className = "count " + countClass(val.length, f.cap);
      cnt.textContent = f.cap ? val.length + " / " + f.cap : val.length + " chars";
      head.appendChild(cnt);
      head.appendChild(makeBtn("Copy", val));
    }
    if (f.note) {
      const n = document.createElement("span");
      n.className = "fnote";
      n.textContent = f.note;
      head.appendChild(n);
    }
    sec.appendChild(head);

    if (f.key === "features") {
      const entries = val.split("\\n").filter((x) => x.trim());
      const enEntries = enVal.split("\\n").filter((x) => x.trim());
      const ol = document.createElement("ol");
      ol.className = "feats";
      entries.forEach((e, i) => {
        const li = document.createElement("li");
        const num = document.createElement("span");
        num.className = "fnum";
        num.textContent = String(i + 1).padStart(2, "0");
        li.appendChild(num);
        const tx = document.createElement("div");
        tx.className = "ftext";
        tx.textContent = e;
        if (showEn && lang !== "en" && enEntries[i]) {
          const s = document.createElement("span");
          s.className = "fen";
          s.textContent = enEntries[i];
          tx.appendChild(s);
        }
        li.appendChild(tx);
        const cc = document.createElement("span");
        cc.className = "count " + countClass(e.length, 200);
        cc.textContent = e.length;
        li.appendChild(cc);
        li.appendChild(makeBtn("Copy", e));
        ol.appendChild(li);
      });
      sec.appendChild(ol);
    } else {
      const pre = document.createElement("pre");
      pre.className = "text";
      pre.textContent = val;
      sec.appendChild(pre);
      if (showEn && lang !== "en" && enVal && enVal !== val) {
        const wrapEn = document.createElement("div");
        wrapEn.className = "english";
        const tag = document.createElement("span");
        tag.className = "tag";
        tag.textContent = "English reference";
        wrapEn.appendChild(tag);
        const p2 = document.createElement("pre");
        p2.className = "text";
        p2.textContent = enVal;
        wrapEn.appendChild(p2);
        sec.appendChild(wrapEn);
      }
    }
    host.appendChild(sec);
  });
}

// assets
const assetHost = $("#assets");
DATA.assets.forEach((a) => {
  const sec = document.createElement("section");
  sec.className = "field";
  const head = document.createElement("div");
  head.className = "fhead";
  const h = document.createElement("h2");
  h.textContent = a.label;
  head.appendChild(h);
  const cnt = document.createElement("span");
  cnt.className = "count";
  cnt.textContent = a.files.length + " files";
  head.appendChild(cnt);
  head.appendChild(makeBtn("Copy command", a.cmd));
  sec.appendChild(head);
  const row = document.createElement("div");
  row.className = "pathrow";
  const c = document.createElement("code");
  c.className = "path";
  c.textContent = a.cmd;
  row.appendChild(c);
  row.appendChild(makeBtn("Copy path", a.path, "ghost"));
  sec.appendChild(row);
  const files = document.createElement("div");
  files.className = "files";
  a.files.forEach((f) => {
    const s = document.createElement("span");
    s.className = "file";
    s.textContent = f;
    files.appendChild(s);
  });
  sec.appendChild(files);
  const hint = document.createElement("p");
  hint.className = "hint";
  hint.textContent = "A browser can't open a local folder. Paste the command in a terminal at the repo root and it opens.";
  sec.appendChild(hint);
  assetHost.appendChild(sec);
});

// packages
const pkgHost = $("#packages");
const pkgSec = document.createElement("section");
pkgSec.className = "field";
DATA.packages.forEach((p) => {
  const row = document.createElement("div");
  row.className = "pathrow";
  const c = document.createElement("code");
  c.className = "path";
  c.textContent = p;
  row.appendChild(c);
  row.appendChild(makeBtn("Copy", p));
  pkgSec.appendChild(row);
});
const pkgHint = document.createElement("p");
pkgHint.className = "hint";
pkgHint.textContent = "Both go in the Packages slot. Unsigned by design \\u2014 Partner Center signs after upload.";
pkgSec.appendChild(pkgHint);
pkgHost.appendChild(pkgSec);

pick.addEventListener("change", () => {
  current = Number(pick.value);
  store.set("rororo.listing", current);
  render();
});
$("#showen").addEventListener("change", (e) => {
  showEn = e.target.checked;
  store.set("rororo.showen", showEn);
  render();
});

render();
</script>
"""


def main() -> None:
    data = build_data()
    # ensure_ascii deliberately: every non-ASCII character in the listing copy ships as a \\uXXXX
    # escape, so the six translated languages render correctly no matter what encoding the document
    # is parsed as. This is not cosmetic — a misdecoded document puts mojibake in the DOM, and the
    # copy buttons would then copy mojibake straight into the Store listing.
    payload = json.dumps(data, ensure_ascii=True).replace("<", "\\u003c")
    OUT.write_text(HTML.replace("__DATA__", payload), encoding="utf-8")
    langs = len(data["copy"])
    print(f"wrote {OUT.relative_to(ROOT)} — {len(data['listings'])} listings, {langs} copy sets, "
          f"{sum(len(a['files']) for a in data['assets'])} assets")
    for lang, c in data["copy"].items():
        missing = [k for k, v in c.items() if not v]
        if missing:
            print(f"  WARNING {lang}: empty fields {missing}")


if __name__ == "__main__":
    main()
