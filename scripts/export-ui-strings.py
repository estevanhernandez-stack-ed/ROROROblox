#!/usr/bin/env python3
"""Export the neutral UI catalog (Strings.resx) to a translation worksheet.

The worksheet is the working surface for the per-culture translation cycle: one
row per key with its English value, the trailing `<comment>` (translator note),
and flags a translator must respect — {0}-style placeholders that must survive
verbatim, and values that are pure proper nouns / glyphs / gestures that stay
English. Feeds both the human/Gemini translation pass and gen-culture-resx.py.

    python scripts/export-ui-strings.py            # -> docs/store/ui-strings.json
"""
from __future__ import annotations

import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
RESX = ROOT / "src" / "ROROROblox.App" / "Properties" / "Strings.resx"
OUT = ROOT / "docs" / "store" / "ui-strings.json"

PLACEHOLDER_RE = re.compile(r"\{\d+")


def main() -> None:
    tree = ET.parse(RESX)
    rows = []
    for data in tree.getroot().findall("data"):
        name = data.get("name")
        if name is None:
            continue
        value_el = data.find("value")
        value = (value_el.text if value_el is not None else "") or ""
        comment_el = data.find("comment")
        comment = (comment_el.text if comment_el is not None else "") or ""
        rows.append(
            {
                "key": name,
                "en": value,
                "comment": comment,
                "placeholders": PLACEHOLDER_RE.findall(value) and True or False,
            }
        )
    rows.sort(key=lambda r: r["key"])
    OUT.write_text(json.dumps({"source": "Strings.resx", "count": len(rows), "rows": rows}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    withph = sum(1 for r in rows if r["placeholders"])
    print(f"wrote {OUT.relative_to(ROOT)} — {len(rows)} keys, {withph} with placeholders")


if __name__ == "__main__":
    main()
