#!/usr/bin/env python3
"""Export the app UI translations as one JSON for the verification tool.

Sibling of export-listing-translations.py, for the APP strings instead of the
Store listing. Reads the neutral Strings.resx (English source of truth) and the
six per-culture docs/store/translations/ui-<culture>.json catalogs, and writes
docs/store/ui-translations.json in the tool's `fields` contract: each entry is
{field, en, translations{lang:...}}, one per resx key. The verifier ingests this
the same way it ingests the listing dataset; app strings carry no Store char cap,
so no cap keys are emitted.

Language codes match the tool's expected set (lowercase, pt-BR -> pt-br).

    python scripts/export-ui-translations.py            # -> docs/store/ui-translations.json
"""
from __future__ import annotations

import datetime
import json
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
RESX = ROOT / "src" / "ROROROblox.App" / "Properties" / "Strings.resx"
TRANS_DIR = ROOT / "docs" / "store" / "translations"
OUT = ROOT / "docs" / "store" / "ui-translations.json"

# (culture file suffix, tool language code) — the tool expects lowercase, pt-br.
LANGUAGES = [("fr", "fr"), ("de", "de"), ("ru", "ru"), ("pt-BR", "pt-br"), ("pl", "pl"), ("es", "es")]


def neutral_en() -> dict[str, str]:
    tree = ET.parse(RESX)
    out: dict[str, str] = {}
    for data in tree.getroot().findall("data"):
        name = data.get("name")
        if name is None:
            continue
        value_el = data.find("value")
        out[name] = (value_el.text if value_el is not None else "") or ""
    return out


def load_catalog(suffix: str) -> dict[str, str]:
    return json.loads((TRANS_DIR / f"ui-{suffix}.json").read_text(encoding="utf-8"))


def main() -> None:
    commit = subprocess.run(
        ["git", "rev-parse", "--short", "HEAD"], cwd=ROOT, capture_output=True, text=True
    ).stdout.strip()

    en = neutral_en()
    catalogs = {code: load_catalog(suffix) for suffix, code in LANGUAGES}

    fields = []
    for key in en:
        per_language = {}
        for _suffix, code in LANGUAGES:
            per_language[code] = catalogs[code].get(key, "")
        fields.append({"field": key, "en": en[key], "translations": per_language})

    payload = {
        "product": "RoRoRo",
        "storeId": "9NMJCS390KWB",
        "dataset": "app-ui-strings",
        "sourceLanguage": "en",
        "languages": [code for _suffix, code in LANGUAGES],
        "sourceCommit": commit,
        "generatedUtc": datetime.datetime.now(datetime.timezone.utc)
        .replace(microsecond=0)
        .isoformat(),
        "sourceOfTruth": "src/ROROROblox.App/Properties/Strings.resx (en) + docs/store/translations/ui-<culture>.json — corrections are edits to the ui-<culture>.json, then re-run gen-culture-resx.py and this script",
        "fields": fields,
    }

    OUT.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"wrote {OUT.relative_to(ROOT)} — {len(fields)} fields x {len(LANGUAGES)} languages, commit {commit}")


if __name__ == "__main__":
    main()
