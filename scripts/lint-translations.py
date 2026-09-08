#!/usr/bin/env python3
"""Deterministic translation guards — the mechanical half of the localization gate.

The division of labor (docs/store/localization-plan.md, Phase D): these checks catch what
a machine can catch with CERTAINTY — complete catalog coverage, product nouns kept English,
format tokens preserved — at zero cost and full reliability. The translation-verification
tool owns JUDGMENT (voice, register, plural grammar, cultural fit). Run this before a catalog
goes to the verifier, and in CI, so a mechanical regression never burns a (slower, costlier)
judgment pass — and so the deterministic net catches what the rubric can miss (during the
Phase C pilot our product-noun guard caught a `Squad Launch` the Gemini review missed).

    python scripts/lint-translations.py             # all shipped cultures
    python scripts/lint-translations.py fr de       # specific cultures

Exit code is non-zero if any catalog has a violation, so it works as a CI gate and as the
first stage of scripts/translate-cycle.py.
"""
from __future__ import annotations

import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
RESX = ROOT / "src" / "ROROROblox.App" / "Properties" / "Strings.resx"
TRANS_DIR = ROOT / "docs" / "store" / "translations"

# Culture file suffixes that ship (ui-<suffix>.json). Kept in step with UiCulture.Candidates.
CULTURES = ["fr", "de", "ru", "pt-BR", "pl", "es"]

# Must stay English wherever the English value contains them (case-sensitive: only the branded
# capitalised forms — descriptive "multi-instance lock" is free). Mirrors gen-culture-resx.py.
PRODUCT_NOUNS = ["RoRoRo", "Roblox", "Squad Launch", "Recycle", "Multi-Instance"]

# .NET composite-format tokens: {0}, {1}, {name}, {count}. The SET must survive translation
# (order may differ between languages), so tokens are compared sorted, as a multiset.
TOKEN_RE = re.compile(r"\{[^{}]+\}")

# ---------------------------------------------------------------------------------------------
# Cross-language parity (advisory). The pilot's one genuine user-facing defect was a security
# qualifier ("publicly") dropped in four languages and kept in two — a class three model runs
# still missed in one language (pt-BR). These deterministic checks catch that class for free, on
# every run, with no sampling. They are ADVISORY: they print review candidates and never change
# the exit code, because a heuristic must not gate CI on a false positive.

# High-stakes terms that must survive translation, English → accepted forms per culture. Seeded
# from the Shell_Export_SavedBody defect; grow it as new load-bearing terms appear in the catalog.
LOAD_BEARING_TERMS = {
    "publicly": {
        "fr": ("publiquement", "en public"),
        "de": ("öffentlich",),
        "es": ("públicamente", "en público"),
        "pt-BR": ("publicamente", "em público"),
        "ru": ("публично",),
        "pl": ("publicznie", "publiczn"),
    },
}

# Negation parity was tried and removed (2026-09-08): a strong English negation ("don't", "never")
# is legitimately rendered as an antonym or a positive construction — "can't be undone" -> "est
# irréversible", "never leaves except" -> "leaves only", "don't share anywhere" -> "teile nirgends"
# — which no marker list can enumerate. On this catalog it produced four false positives and zero
# real findings, so it is gone rather than left as noise. The two checks below produced zero false
# positives.

# A translation far shorter than its siblings for the same key is a likely dropped clause. Only
# checked on substantial strings (short labels vary too much to judge), and only flagged well
# below the median so natural length variance (German runs long, etc.) does not trip it.
PARITY_LENGTH_FLOOR = 40
PARITY_LENGTH_MIN_RATIO = 0.55


def neutral_en() -> dict[str, str]:
    tree = ET.parse(RESX)
    out: dict[str, str] = {}
    for data in tree.getroot().findall("data"):
        name = data.get("name")
        if name is None:
            continue
        v = data.find("value")
        out[name] = (v.text if v is not None else "") or ""
    return out


def load_catalog(suffix: str) -> dict[str, str] | None:
    path = TRANS_DIR / f"ui-{suffix}.json"
    if not path.exists():
        return None
    return json.loads(path.read_text(encoding="utf-8"))


def lint_culture(suffix: str, en: dict[str, str]) -> list[str]:
    cat = load_catalog(suffix)
    if cat is None:
        return [f"no catalog file: ui-{suffix}.json"]
    enkeys = set(en)
    problems: list[str] = []

    missing = [k for k in en if k not in cat]
    extra = [k for k in cat if k not in enkeys and not k.startswith("_")]
    empty = [k for k in en if k in cat and not str(cat[k]).strip()]
    if missing:
        problems.append(f"{len(missing)} missing key(s): {missing[:6]}{' …' if len(missing) > 6 else ''}")
    if extra:
        problems.append(f"{len(extra)} extra key(s): {extra[:6]}{' …' if len(extra) > 6 else ''}")
    if empty:
        problems.append(f"{len(empty)} empty value(s): {empty[:6]}{' …' if len(empty) > 6 else ''}")

    noun_viol: list[str] = []
    token_viol: list[str] = []
    for k, env in en.items():
        if k not in cat:
            continue
        tv = str(cat[k])
        for noun in PRODUCT_NOUNS:
            if noun in env and noun not in tv:
                noun_viol.append(f"{k}:'{noun}'")
        en_tokens = sorted(TOKEN_RE.findall(env))
        tv_tokens = sorted(TOKEN_RE.findall(tv))
        if en_tokens != tv_tokens:
            token_viol.append(f"{k}: en{en_tokens}!={suffix}{tv_tokens}")
    if noun_viol:
        problems.append(f"{len(noun_viol)} product-noun violation(s): {noun_viol[:6]}{' …' if len(noun_viol) > 6 else ''}")
    if token_viol:
        problems.append(f"{len(token_viol)} format-token mismatch(es): {token_viol[:6]}{' …' if len(token_viol) > 6 else ''}")
    return problems


def parity_candidates(en: dict[str, str], catalogs: dict[str, dict[str, str]]) -> list[str]:
    """Cross-language review candidates (advisory). Anchored on the English so it catches a
    load-bearing element dropped in SOME languages even when the majority dropped it too — the
    failure mode plain majority voting gets backwards."""
    out: list[str] = []
    for key, env in en.items():
        envl = env.lower()
        trans = {c: str(catalogs[c].get(key, "")) for c in CULTURES}

        # 1. Load-bearing keyword parity — the Shell_Export_SavedBody class.
        for term, forms in LOAD_BEARING_TERMS.items():
            if term in envl:
                for c in CULTURES:
                    if trans[c].strip() and not any(f.lower() in trans[c].lower() for f in forms.get(c, ())):
                        out.append(f"[keyword] {key} [{c}]: English carries '{term}', translation has no equivalent")

        # 2. Length-ratio outlier — a likely dropped clause.
        if len(env) >= PARITY_LENGTH_FLOOR:
            lens = {c: len(trans[c]) for c in CULTURES if trans[c].strip()}
            if len(lens) >= 4:
                ordered = sorted(lens.values())
                median = ordered[len(ordered) // 2]
                for c, length in lens.items():
                    if median and length < PARITY_LENGTH_MIN_RATIO * median:
                        out.append(f"[length] {key} [{c}]: {length} chars vs median {median} — possible dropped clause")
    return out


def _selftest() -> None:
    """Prove the parity net fires on a dropped keyword and stays quiet on a clean set."""
    en = {"K": "Keep it safe and don't post the file publicly."}
    clean = {c: {"K": f"... {('publiquement' if c=='fr' else 'öffentlich' if c=='de' else 'públicamente' if c=='es' else 'publicamente' if c=='pt-BR' else 'публично' if c=='ru' else 'publicznie')} ..."} for c in CULTURES}
    dropped = {c: {"K": "..."} for c in CULTURES}  # every language dropped the equivalent
    assert not any("[keyword]" in x for x in parity_candidates(en, clean)), "clean set must not flag keyword"
    flagged = [x for x in parity_candidates(en, dropped) if "[keyword]" in x]
    assert len(flagged) == len(CULTURES), f"dropped keyword must flag every language, got {flagged}"
    print("parity self-test ok")


def main() -> None:
    if "--selftest" in sys.argv:
        _selftest()
        return
    cultures = sys.argv[1:] or CULTURES
    en = neutral_en()
    print(f"linting {len(cultures)} culture(s) against {len(en)} neutral keys")
    failed = 0
    for c in cultures:
        probs = lint_culture(c, en)
        if probs:
            failed += 1
            print(f"  {c}: FAIL")
            for p in probs:
                print(f"     - {p}")
        else:
            print(f"  {c}: ok — {len(en)} keys, product nouns + format tokens preserved")
    if failed:
        raise SystemExit(f"\n{failed} catalog(s) with violations — fix the source ui-<culture>.json, then re-run")
    print("\nall catalogs clean (deterministic checks)")

    # Advisory cross-language parity — review candidates, never a gate (see the constants block).
    catalogs = {c: load_catalog(c) or {} for c in CULTURES}
    candidates = parity_candidates(en, catalogs)
    print(f"\ncross-language parity: {len(candidates)} review candidate(s) (advisory — not failures)")
    for c in candidates:
        print(f"     ? {c}")


if __name__ == "__main__":
    main()
