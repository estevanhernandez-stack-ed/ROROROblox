#!/usr/bin/env python3
"""Merge the v1.41 "Roblox at a glance" cycle's new UI keys into the six per-culture translation sets.

CUMULATIVE FOR THE v1.41 CYCLE. Item 7 (the Roblox line) wrote the first block; every later item that
adds a neutral key appends its own block to T below, under a comment naming the item, and re-runs this
script. It is idempotent: a key already present with the same value is kept, one that differs is left
alone unless --force, and a second run reports "no change". Nothing here is hand-edited into a
Strings.<culture>.resx; gen-culture-resx.py renders those from docs/store/translations/ui-<culture>.json.

Wording is matched to the SHIPPED catalog rather than freshly invented. The update sentence names the
main window's batch button, so its label is read from MainWindow_LaunchMultiple in each ui-<culture>.json
and checked against the table below; "Roblox" stays English. A plural family carries every CLDR category
the app asks for (one/other for en, fr, de, es, pt; one/few/many for ru, pl) plus `other` everywhere, the
same shape as Shell_Fact_FriendsOnline.

    python -I scripts/add-v141-translations.py          # merge, refuse to overwrite a different value
    python -I scripts/add-v141-translations.py --force  # overwrite existing values too
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TRANS_DIR = ROOT / "docs" / "store" / "translations"
CULTURES = ["de", "es", "fr", "pl", "pt-BR", "ru"]

# The main window's batch button, MainWindow_LaunchMultiple, as each shipped catalog words it. The
# update sentence below must carry this label verbatim; main() verifies it against the catalog.
LAUNCH_LABEL = {
    "de": "Mehrere starten",
    "es": "Iniciar varias",
    "fr": "Lancer plusieurs",
    "pl": "Uruchom wiele",
    "pt-BR": "Iniciar vários",
    "ru": "Запустить несколько",
}

NBSP = " "

# key -> {culture: translation}
T: dict[str, dict[str, str]] = {
    # ---- item 7: the Roblox line --------------------------------------------------------------
    "RobloxLine_Update_Many": {
        "de": f"Roblox hat ein Update. Der nächste Start installiert es, und „{LAUNCH_LABEL['de']}“ hält die übrigen zurück, bis es fertig ist.",
        "es": f"Roblox tiene una actualización. El próximo inicio la instalará, y “{LAUNCH_LABEL['es']}” retiene las demás hasta que termine.",
        "fr": f"Roblox a une mise à jour. Le prochain lancement l'installera, et «{NBSP}{LAUNCH_LABEL['fr']}{NBSP}» retient les autres jusqu'à la fin.",
        "pl": f"Roblox ma aktualizację. Następne uruchomienie ją zainstaluje, a „{LAUNCH_LABEL['pl']}” wstrzyma pozostałe do jej zakończenia.",
        "pt-BR": f"Há uma atualização do Roblox. O próximo início vai instalá-la, e “{LAUNCH_LABEL['pt-BR']}” segura os outros até terminar.",
        "ru": f"У Roblox есть обновление. Следующий запуск установит его, а «{LAUNCH_LABEL['ru']}» удержит остальные, пока оно не завершится.",
    },
    "RobloxLine_Update_One": {
        "de": "Roblox hat ein Update. Der nächste Start installiert es.",
        "es": "Roblox tiene una actualización. El próximo inicio la instalará.",
        "fr": "Roblox a une mise à jour. Le prochain lancement l'installera.",
        "pl": "Roblox ma aktualizację. Następne uruchomienie ją zainstaluje.",
        "pt-BR": "Há uma atualização do Roblox. O próximo início vai instalá-la.",
        "ru": "У Roblox есть обновление. Следующий запуск установит его.",
    },
    "RobloxLine_WhatChanged": {
        "de": "Was sich geändert hat",
        "es": "Qué ha cambiado",
        "fr": "Ce qui a changé",
        "pl": "Co się zmieniło",
        "pt-BR": "O que mudou",
        "ru": "Что изменилось",
    },
    # One sentence per level; the row name follows the colon so it never has to agree with a verb.
    "RobloxLine_Status_Outage": {
        "de": "Roblox meldet eine Störung: {0}",
        "es": "Roblox informa de una caída: {0}",
        "fr": f"Roblox signale une panne{NBSP}: {{0}}",
        "pl": "Roblox zgłasza awarię: {0}",
        "pt-BR": "Roblox informa uma queda: {0}",
        "ru": "Roblox сообщает о сбое: {0}",
    },
    "RobloxLine_Status_Partial": {
        "de": "Roblox meldet eine teilweise Störung: {0}",
        "es": "Roblox informa de una caída parcial: {0}",
        "fr": f"Roblox signale une panne partielle{NBSP}: {{0}}",
        "pl": "Roblox zgłasza częściową awarię: {0}",
        "pt-BR": "Roblox informa uma queda parcial: {0}",
        "ru": "Roblox сообщает о частичном сбое: {0}",
    },
    "RobloxLine_Status_Degraded": {
        "de": "Roblox meldet eingeschränkte Leistung: {0}",
        "es": "Roblox informa de un rendimiento degradado: {0}",
        "fr": f"Roblox signale des performances dégradées{NBSP}: {{0}}",
        "pl": "Roblox zgłasza obniżoną wydajność: {0}",
        "pt-BR": "Roblox informa desempenho degradado: {0}",
        "ru": "Roblox сообщает о снижении производительности: {0}",
    },
    "RobloxLine_Status_Security": {
        "de": "Roblox meldet ein Sicherheitsproblem: {0}",
        "es": "Roblox informa de un problema de seguridad: {0}",
        "fr": f"Roblox signale un problème de sécurité{NBSP}: {{0}}",
        "pl": "Roblox zgłasza problem z bezpieczeństwem: {0}",
        "pt-BR": "Roblox informa um problema de segurança: {0}",
        "ru": "Roblox сообщает о проблеме с безопасностью: {0}",
    },
    "RobloxLine_Status_Maintenance": {
        "de": "Roblox meldet laufende Wartungsarbeiten: {0}",
        "es": "Roblox informa de un mantenimiento en curso: {0}",
        "fr": f"Roblox signale une maintenance en cours{NBSP}: {{0}}",
        "pl": "Roblox zgłasza trwające prace serwisowe: {0}",
        "pt-BR": "Roblox informa manutenção em andamento: {0}",
        "ru": "Roblox сообщает о проводимых технических работах: {0}",
    },
    "RobloxLine_Status_Problem": {
        "de": "Roblox meldet ein Problem: {0}",
        "es": "Roblox informa de un problema: {0}",
        "fr": f"Roblox signale un problème{NBSP}: {{0}}",
        "pl": "Roblox zgłasza problem: {0}",
        "pt-BR": "Roblox informa um problema: {0}",
        "ru": "Roblox сообщает о проблеме: {0}",
    },
    "RobloxLine_Row_Website": {
        "de": "Website",
        "es": "Sitio web",
        "fr": "Site web",
        "pl": "Strona",
        "pt-BR": "Site",
        "ru": "Сайт",
    },
    "RobloxLine_Row_Games": {
        "de": "Spiele",
        "es": "Juegos",
        "fr": "Jeux",
        "pl": "Gry",
        "pt-BR": "Jogos",
        "ru": "Игры",
    },
    "RobloxLine_Row_Avatar": {
        "de": "Avatare",
        "es": "Avatares",
        "fr": "Avatars",
        "pl": "Awatary",
        "pt-BR": "Avatares",
        "ru": "Аватары",
    },
    "RobloxLine_Row_GameJoin": {
        "de": "Spielbeitritt",
        "es": "Unirse a juegos",
        "fr": "Rejoindre des jeux",
        "pl": "Dołączanie do gier",
        "pt-BR": "Entrada em jogos",
        "ru": "Вход в игры",
    },
    # "{0} and {1} more": {0} the row, {1} how many other rows (1 to 3), which picks the form.
    "RobloxLine_Status_AndMore_one": {
        "de": "{0} und {1} weitere",
        "es": "{0} y {1} más",
        "fr": "{0} et {1} autre",
        "pl": "{0} i jeszcze {1}",
        "pt-BR": "{0} e mais {1}",
        "ru": "{0} и ещё {1}",
    },
    "RobloxLine_Status_AndMore_other": {
        "de": "{0} und {1} weitere",
        "es": "{0} y {1} más",
        "fr": "{0} et {1} autres",
        "pl": "{0} i jeszcze {1}",
        "pt-BR": "{0} e mais {1}",
        "ru": "{0} и ещё {1}",
    },
    "RobloxLine_Status_AndMore_few": {
        "de": "{0} und {1} weitere",
        "es": "{0} y {1} más",
        "fr": "{0} et {1} autres",
        "pl": "{0} i jeszcze {1}",
        "pt-BR": "{0} e mais {1}",
        "ru": "{0} и ещё {1}",
    },
    "RobloxLine_Status_AndMore_many": {
        "de": "{0} und {1} weitere",
        "es": "{0} y {1} más",
        "fr": "{0} et {1} autres",
        "pl": "{0} i jeszcze {1}",
        "pt-BR": "{0} e mais {1}",
        "ru": "{0} и ещё {1}",
    },
    "RobloxLine_StatusLink": {
        "de": "Roblox-Status",
        "es": "Estado de Roblox",
        "fr": "État de Roblox",
        "pl": "Status Roblox",
        "pt-BR": "Status do Roblox",
        "ru": "Статус Roblox",
    },
    "RobloxLine_More": {
        "de": "+{0} weitere",
        "es": "+{0} más",
        "fr": "+{0} autres",
        "pl": "+{0} więcej",
        "pt-BR": "+{0} mais",
        "ru": "+{0} ещё",
    },
    "RobloxLine_More_Name_one": {
        "de": "{0} weitere Roblox-Meldung",
        "es": "{0} aviso más de Roblox",
        "fr": "{0} autre avis Roblox",
        "pl": "Jeszcze {0} powiadomienie Roblox",
        "pt-BR": "Mais {0} aviso do Roblox",
        "ru": "Ещё {0} уведомление Roblox",
    },
    "RobloxLine_More_Name_other": {
        "de": "{0} weitere Roblox-Meldungen",
        "es": "{0} avisos más de Roblox",
        "fr": "{0} autres avis Roblox",
        "pl": "Jeszcze {0} powiadomień Roblox",
        "pt-BR": "Mais {0} avisos do Roblox",
        "ru": "Ещё {0} уведомлений Roblox",
    },
    "RobloxLine_More_Name_few": {
        "de": "{0} weitere Roblox-Meldungen",
        "es": "{0} avisos más de Roblox",
        "fr": "{0} autres avis Roblox",
        "pl": "Jeszcze {0} powiadomienia Roblox",
        "pt-BR": "Mais {0} avisos do Roblox",
        "ru": "Ещё {0} уведомления Roblox",
    },
    "RobloxLine_More_Name_many": {
        "de": "{0} weitere Roblox-Meldungen",
        "es": "{0} avisos más de Roblox",
        "fr": "{0} autres avis Roblox",
        "pl": "Jeszcze {0} powiadomień Roblox",
        "pt-BR": "Mais {0} avisos do Roblox",
        "ru": "Ещё {0} уведомлений Roblox",
    },
    # ---- item 9: the off switch ---------------------------------------------------------------
    # The description names Status.io as the third party; the name stays as written in every language.
    "SettingsPage_RobloxFeeds": {
        "de": "Roblox-Status und Spielaktivität anzeigen.",
        "es": "Mostrar el estado de Roblox y la actividad de los juegos.",
        "fr": "Afficher l'état de Roblox et l'activité des jeux.",
        "pl": "Pokazuj status Roblox i aktywność w grach.",
        "pt-BR": "Mostrar o status do Roblox e a atividade dos jogos.",
        "ru": "Показывать статус Roblox и активность в играх.",
    },
    "SettingsPage_RobloxFeeds_Description": {
        "de": "Prüft die Statusseite von Roblox und liest, wie viele Leute in deinen gespeicherten Spielen sind. Die Statusprüfung geht an Status.io, einen Drittanbieter, der die Seite für Roblox hostet. Es werden keine Informationen über dich oder deine Konten gesendet.",
        "es": "Consulta la página de estado de Roblox y lee cuánta gente hay en tus juegos guardados. La consulta del estado va a Status.io, un tercero que aloja la página para Roblox. No se envía nada sobre ti ni sobre tus cuentas.",
        "fr": "Consulte la page d'état de Roblox et lit combien de personnes sont dans tes jeux enregistrés. La vérification de l'état passe par Status.io, un tiers qui héberge la page pour Roblox. Rien sur toi ni sur tes comptes n'est envoyé.",
        "pl": "Sprawdza stronę statusu Roblox i odczytuje, ile osób jest w twoich zapisanych grach. Sprawdzenie statusu trafia do Status.io, podmiotu trzeciego, który hostuje tę stronę dla Roblox. Nic o tobie ani o twoich kontach nie jest wysyłane.",
        "pt-BR": "Consulta a página de status do Roblox e lê quantas pessoas estão nos seus jogos salvos. A consulta de status vai para o Status.io, um terceiro que hospeda a página para o Roblox. Nada sobre você ou suas contas é enviado.",
        "ru": "Проверяет страницу статуса Roblox и считывает, сколько людей в твоих сохранённых играх. Проверка статуса идёт на Status.io, стороннему сервису, который размещает эту страницу для Roblox. Ничего о тебе или твоих аккаунтах не отправляется.",
    },
}

# Neutral keys this cycle retired from the translation files. None yet.
STALE: dict[str, str] = {}


def neutral_keys() -> set[str]:
    import xml.etree.ElementTree as ET
    resx = ROOT / "src" / "ROROROblox.App" / "Properties" / "Strings.resx"
    return {d.get("name") for d in ET.parse(resx).getroot().findall("data")
            if d.get("name") is not None}


def main() -> None:
    force = "--force" in sys.argv

    # Every key must carry all six, or the catalog would ship a hole this script was meant to close.
    incomplete = {k: sorted(set(CULTURES) - set(v)) for k, v in T.items()
                  if set(CULTURES) - set(v)}
    if incomplete:
        raise SystemExit("translation table is incomplete: "
                         + "; ".join(f"{k} missing {','.join(c)}" for k, c in incomplete.items()))

    print(f"{len(T)} keys x {len(CULTURES)} cultures = {len(T) * len(CULTURES)} translations")

    neutral = neutral_keys()
    absent = sorted(k for k in T if k not in neutral)
    if absent:
        raise SystemExit("keys with no neutral Strings.resx entry (add English by hand first): "
                         + ", ".join(absent))
    resurrected = sorted(k for k in STALE if k in neutral)
    if resurrected:
        raise SystemExit("STALE names a key the neutral catalog still has: " + ", ".join(resurrected))

    total_added = total_changed = total_pruned = 0
    for culture in CULTURES:
        path = TRANS_DIR / f"ui-{culture}.json"
        data = json.loads(path.read_text(encoding="utf-8"))
        if not isinstance(data, dict):
            raise SystemExit(f"{path.name} is not a flat key->value object")

        # The update sentence must name the batch button exactly as this culture's catalog labels it.
        label = data.get("MainWindow_LaunchMultiple")
        if label != LAUNCH_LABEL[culture]:
            raise SystemExit(f"{culture}: MainWindow_LaunchMultiple is {label!r}, the table says "
                             f"{LAUNCH_LABEL[culture]!r}. Re-read the button's label and fix LAUNCH_LABEL.")
        if LAUNCH_LABEL[culture] not in T["RobloxLine_Update_Many"][culture]:
            raise SystemExit(f"{culture}: RobloxLine_Update_Many does not carry the button's label")

        pruned = 0
        for key, why in STALE.items():
            if key in data:
                del data[key]
                pruned += 1
                if culture == CULTURES[0]:
                    print(f"  pruning {key}: {why}")

        added = changed = kept = 0
        for key, per_culture in T.items():
            new = per_culture[culture]
            old = data.get(key)
            if old is None:
                data[key] = new
                added += 1
            elif old == new:
                kept += 1
            elif force:
                data[key] = new
                changed += 1
            else:
                print(f"  {culture}: {key} already differs; left alone (use --force to overwrite)")
                kept += 1

        # Existing order is kept and new keys append, so the diff is only the new rows.
        rendered = json.dumps(data, ensure_ascii=False, indent=2) + "\n"
        if rendered != path.read_text(encoding="utf-8"):
            path.write_bytes(rendered.encode("utf-8"))  # LF, as .gitattributes wants for json
        print(f"  {culture:<5} added {added:>2}, overwrote {changed:>2}, kept {kept:>2}, "
              f"pruned {pruned:>2}  -> {len(data)} keys")
        total_added += added
        total_changed += changed
        total_pruned += pruned

    if total_added == total_changed == total_pruned == 0:
        print("\nno change")
    else:
        print("\nNow run: python -I scripts/gen-culture-resx.py <culture>  for each of "
              + ", ".join(CULTURES))


if __name__ == "__main__":
    main()
