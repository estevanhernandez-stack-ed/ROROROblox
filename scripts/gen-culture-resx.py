#!/usr/bin/env python3
"""Apply a per-culture translation set to a satellite Strings.<culture>.resx.

The honest-picker guarantee (UiCulture.HasCatalog) means a language is offered
ONLY when its satellite catalog ships — so a shipped catalog must be COMPLETE:
every neutral key present, none extra, no empty values. This script enforces
that and emits a resx that mirrors the neutral file's schema (4 resheaders,
xml:space="preserve" on every data node, neutral key order for a clean diff).
Comments (translator source notes) are dropped from satellites — they're not
needed at runtime and only bloat the assembly.

    python scripts/gen-culture-resx.py pt-BR
        reads  docs/store/translations/ui-pt-BR.json   ({ "Key": "translated", ... }
                                                          or { "rows": [{key, ...}] })
        writes src/ROROROblox.App/Properties/Strings.pt-BR.resx

Fails loudly on any missing key, extra key, or empty translated value. Proper
nouns (RoRoRo, Roblox, 626 Labs, "Koii 4 eva", keyboard gestures, hex, URLs) are
expected to carry their English text verbatim in the translation JSON.
"""
from __future__ import annotations

import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
RESX = ROOT / "src" / "ROROROblox.App" / "Properties" / "Strings.resx"
TRANS_DIR = ROOT / "docs" / "store" / "translations"
OUT_DIR = ROOT / "src" / "ROROROblox.App" / "Properties"

RESHEADERS = [
    ("resmimetype", "text/microsoft-resx"),
    ("version", "2.0"),
    ("reader", "System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"),
    ("writer", "System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"),
]


def esc(text: str) -> str:
    return text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


# Product nouns that MUST stay English in every language (localization-plan.md, the
# approved listings' "product nouns stay English" rule + the Multi-Instance ruling). The
# es voice-review pilot (2026-09-07) caught Squad Launch / Recycle translated across all six
# catalogs; this guard makes that class of regression impossible to ship. Case-sensitive, so
# only the branded capitalised forms are enforced (descriptive "multi-instance lock" is free).
PRODUCT_NOUNS = ["RoRoRo", "Roblox", "Squad Launch", "Recycle", "Multi-Instance"]


def neutral_entries() -> list[tuple[str, str]]:
    tree = ET.parse(RESX)
    entries = []
    for data in tree.getroot().findall("data"):
        name = data.get("name")
        if name is None:
            continue
        value_el = data.find("value")
        entries.append((name, (value_el.text if value_el is not None else "") or ""))
    return entries


def load_translations(culture: str) -> dict[str, str]:
    path = TRANS_DIR / f"ui-{culture}.json"
    if not path.exists():
        raise SystemExit(f"no translation file at {path.relative_to(ROOT)}")
    raw = json.loads(path.read_text(encoding="utf-8"))
    if isinstance(raw, dict) and "rows" in raw:
        out = {}
        for r in raw["rows"]:
            out[r["key"]] = r.get("translated", r.get("value", ""))
        return out
    if isinstance(raw, dict):
        return {k: v for k, v in raw.items() if not k.startswith("_")}
    raise SystemExit(f"unrecognised translation JSON shape in {path.name}")


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit("usage: gen-culture-resx.py <culture>   e.g. pt-BR")
    culture = sys.argv[1]
    entries = neutral_entries()
    keys = [k for k, _ in entries]
    en = dict(entries)
    keyset = set(keys)
    trans = load_translations(culture)

    missing = [k for k in keys if k not in trans]
    extra = [k for k in trans if k not in keyset]
    empty = [k for k in keys if k in trans and not str(trans[k]).strip()]
    # Product-noun guard: if the English value carries a must-stay-English noun, the
    # translation must carry it verbatim (it was not translated away).
    noun_violations = [
        f"{k}: '{noun}' translated away"
        for k in keys
        if k in trans
        for noun in PRODUCT_NOUNS
        if noun in en.get(k, "") and noun not in str(trans[k])
    ]
    problems = []
    if noun_violations:
        problems.append(
            f"{len(noun_violations)} product-noun violation(s): {noun_violations[:8]}"
            + (" …" if len(noun_violations) > 8 else "")
        )
    if missing:
        problems.append(f"{len(missing)} MISSING key(s): {missing[:8]}{' …' if len(missing) > 8 else ''}")
    if extra:
        problems.append(f"{len(extra)} EXTRA key(s) not in neutral: {extra[:8]}{' …' if len(extra) > 8 else ''}")
    if empty:
        problems.append(f"{len(empty)} EMPTY value(s): {empty[:8]}{' …' if len(empty) > 8 else ''}")
    if problems:
        raise SystemExit("refusing to write an incomplete catalog:\n  - " + "\n  - ".join(problems))

    lines = ['<?xml version="1.0" encoding="utf-8"?>', "<root>"]
    for name, value in RESHEADERS:
        lines.append(f'  <resheader name="{name}">')
        lines.append(f"    <value>{esc(value)}</value>")
        lines.append("  </resheader>")
    for k in keys:
        lines.append(f'  <data name="{k}" xml:space="preserve">')
        lines.append(f"    <value>{esc(str(trans[k]))}</value>")
        lines.append("  </data>")
    lines.append("</root>")

    out = OUT_DIR / f"Strings.{culture}.resx"
    out.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"wrote {out.relative_to(ROOT)} — {len(keys)} keys ({culture})")


if __name__ == "__main__":
    main()
