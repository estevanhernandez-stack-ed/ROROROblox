#!/usr/bin/env python3
"""The localization cycle, one command — lint → generate → dataset → (verify).

Wraps the two halves of the localization gate (docs/store/localization-plan.md, Phase D):
the DETERMINISTIC half (lint, generate, dataset) runs locally at zero cost and full
reliability; the JUDGMENT half (verify) goes to the translation-verification tool. Corrections
come back as edits to the source-of-truth `docs/store/translations/ui-<culture>.json`, then the
cycle re-runs. This is the repeatable entry point so verification is a pipeline step, not a
hand-driven MCP dance.

Stages (default: lint + generate + dataset — the local, tool-independent half):
  lint      deterministic gate — coverage, product nouns, format tokens (lint-translations.py).
            Runs FIRST and hard-stops the cycle on any violation, so a mechanical error never
            reaches the slower/costlier judgment gate.
  generate  rebuild Strings.<culture>.resx from ui-<culture>.json (gen-culture-resx.py; its own
            inline guards re-check on the way out).
  dataset   rebuild the verifier dataset docs/store/ui-translations.json (export-ui-translations.py).
  verify    [TOOL SEAM] submit to translation-verification for judgment review. See the note below.

    python scripts/translate-cycle.py                  # lint + generate + dataset, all cultures
    python scripts/translate-cycle.py --cultures es    # one culture
    python scripts/translate-cycle.py --stages lint    # just the gate (CI use)

VERIFY SEAM: today the verifier ingests a pushed dataset URL and reviews synchronously (it
times out at app scale). Per translation-verification issue #13 this becomes inline-ingest +
async-poll + incremental (skip unchanged textHash). When that lands, wire the call into the
`verify` stage here so the full cycle is one command. Until then `verify` prints the dataset
path and the manual MCP steps, and the deterministic gate above already carries the mechanical
load the tool no longer has to.
"""
from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SCRIPTS = ROOT / "scripts"
DATASET = ROOT / "docs" / "store" / "ui-translations.json"
CULTURES = ["fr", "de", "ru", "pt-BR", "pl", "es"]
ALL_STAGES = ["lint", "generate", "dataset", "verify"]
DEFAULT_STAGES = ["lint", "generate", "dataset"]


def run(script: str, *args: str) -> int:
    print(f"\n$ python scripts/{script} {' '.join(args)}".rstrip())
    return subprocess.run([sys.executable, str(SCRIPTS / script), *args], cwd=ROOT).returncode


def stage_lint(cultures: list[str]) -> None:
    if run("lint-translations.py", *cultures) != 0:
        raise SystemExit("lint failed — fix the source ui-<culture>.json and re-run; the cycle stops here.")


def stage_generate(cultures: list[str]) -> None:
    for c in cultures:
        if run("gen-culture-resx.py", c) != 0:
            raise SystemExit(f"generate failed for {c}.")


def stage_dataset(_cultures: list[str]) -> None:
    if run("export-ui-translations.py") != 0:
        raise SystemExit("dataset export failed.")


def stage_verify(cultures: list[str]) -> None:
    print("\n=== verify (tool seam) ===")
    print(f"Dataset ready for the verifier: {DATASET.relative_to(ROOT)}")
    print("Deterministic checks already passed (lint), so the verifier only needs to judge:")
    print("  voice · register · plural grammar · cultural fit — on the changed strings.")
    print("Today (manual, until translation-verification issue #13 lands inline-ingest + async):")
    print("  1. commit + push the dataset, then ingest_translations(<raw-github-url@commit>)")
    print(f"  2. run_translation_review per culture: {', '.join(cultures)}")
    print("  3. apply verdicts to ui-<culture>.json (export_verdicts -> scripts/apply-translation-verdicts.py)")
    print("  4. re-run this cycle (lint re-checks; only changed strings need re-verifying once #13 ships)")


STAGES = {"lint": stage_lint, "generate": stage_generate, "dataset": stage_dataset, "verify": stage_verify}


def main() -> None:
    ap = argparse.ArgumentParser(description="Run the localization cycle.")
    ap.add_argument("--cultures", default=",".join(CULTURES), help="comma-separated culture suffixes")
    ap.add_argument("--stages", default=",".join(DEFAULT_STAGES), help=f"comma-separated, from {ALL_STAGES}")
    a = ap.parse_args()
    cultures = [c.strip() for c in a.cultures.split(",") if c.strip()]
    stages = [s.strip() for s in a.stages.split(",") if s.strip()]
    bad = [s for s in stages if s not in STAGES]
    if bad:
        raise SystemExit(f"unknown stage(s): {bad}; valid: {ALL_STAGES}")

    print(f"translate-cycle — cultures: {cultures} — stages: {stages}")
    for s in stages:
        STAGES[s](cultures)
    print("\ntranslate-cycle done.")


if __name__ == "__main__":
    main()
