#!/usr/bin/env python3
"""Merge the 32 keys v1.33 item 6 added into the six per-culture translation sets.

One-shot companion to the catalog pipeline (v1.33 item 6b). The worksheet from
export-ui-strings.py carries the English; this supplies the six translations per key and writes
them into docs/store/translations/ui-<culture>.json, which gen-culture-resx.py then renders.

Wording is matched to the SHIPPED catalog rather than freshly invented: each destination line reuses
the exact pattern already used for drop-out, memory, recycle, auto-rejoin-paused and metric alerts
in that language, and the tray hint names the Desktop checkbox by the same word
SettingsPage_Desktop uses there (Desktop / Escritorio / Bureau / Pulpit / Area de trabalho /
Rabochiy stol). Idle vocabulary follows SettingsPage_IdleWarnThreshold.

Product nouns stay English by gen-culture-resx.py's PRODUCT_NOUNS guard; the only one in this batch
is RoRoRo, in the chime option, and every translation below keeps it verbatim.

    python -I scripts/add-v133-translations.py          # merge, refuse to overwrite a different value
    python -I scripts/add-v133-translations.py --force  # overwrite existing values too
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TRANS_DIR = ROOT / "docs" / "store" / "translations"
CULTURES = ["de", "es", "fr", "pl", "pt-BR", "ru"]

# key -> {culture: translation}
T: dict[str, dict[str, str]] = {
    "SettingsPage_AlertColumnAlert": {
        "de": "Benachrichtigung",
        "es": "Alerta",
        "fr": "Alerte",
        "pl": "Alert",
        "pt-BR": "Alerta",
        "ru": "Оповещение",
    },
    "SettingsPage_AlertColumnHowOften": {
        "de": "Höchstens einmal alle",
        "es": "Como máximo una vez cada",
        "fr": "Au maximum une fois toutes les",
        "pl": "Najwyżej raz na",
        "pt-BR": "No máximo uma vez a cada",
        "ru": "Не чаще одного раза в",
    },
    "SettingsPage_AlertColumnWhereItGoes": {
        "de": "Wohin sie geht",
        "es": "A dónde va",
        "fr": "Où elle va",
        "pl": "Gdzie trafia",
        "pt-BR": "Para onde vai",
        "ru": "Куда отправляется",
    },
    "SettingsPage_HowOftenAtMost": {
        "de": "Wie oft höchstens",
        "es": "Con qué frecuencia como máximo",
        "fr": "À quelle fréquence au maximum",
        "pl": "Jak często najwyżej",
        "pt-BR": "Com que frequência no máximo",
        "ru": "Как часто не более",
    },
    "SettingsPage_HowOftenAtMostHint": {
        "de": "Eine Ruhezeit pro Konto und pro Benachrichtigung, damit eine schlechte Minute eine "
              "Nachricht ergibt und nicht zwanzig. Stelle es auf „Jedes Mal“ und nichts wird "
              "zurückgehalten.",
        "es": "Un periodo de silencio por cuenta y por alerta, así un mal minuto es un mensaje en "
              "vez de veinte. Ponlo en «Cada vez» y nada se retiene.",
        "fr": "Une période de silence par compte et par alerte, pour qu'une mauvaise minute donne "
              "un message et non vingt. Réglez-le sur « Chaque fois » et rien n'est retenu.",
        "pl": "Okres ciszy dla każdego konta i każdego alertu, więc jedna zła minuta to jedna "
              "wiadomość, a nie dwadzieścia. Ustaw „Za każdym razem” i nic nie zostanie wstrzymane.",
        "pt-BR": "Um período de silêncio por conta e por alerta, então um minuto ruim é uma "
                 "mensagem em vez de vinte. Defina como “Sempre” e nada é retido.",
        "ru": "Период тишины для каждого аккаунта и каждого оповещения, чтобы одна плохая минута "
              "давала одно сообщение, а не двадцать. Выберите «Каждый раз» — и ничего не "
              "задерживается.",
    },
    "SettingsPage_HowOftenAnyAlertMayRepeat": {
        "de": "Wie oft sich eine Benachrichtigung höchstens wiederholen darf",
        "es": "Con qué frecuencia puede repetirse como máximo cualquier alerta",
        "fr": "À quelle fréquence une alerte peut se répéter au maximum",
        "pl": "Jak często najwyżej może powtarzać się dowolny alert",
        "pt-BR": "Com que frequência qualquer alerta pode repetir no máximo",
        "ru": "Как часто любое оповещение может повторяться не более",
    },
    "SettingsPage_EveryTime": {
        "de": "Jedes Mal",
        "es": "Cada vez",
        "fr": "Chaque fois",
        "pl": "Za każdym razem",
        "pt-BR": "Sempre",
        "ru": "Каждый раз",
    },
    "SettingsPage_1Minute": {
        "de": "1 Minute", "es": "1 minuto", "fr": "1 minute",
        "pl": "1 minuta", "pt-BR": "1 minuto", "ru": "1 минута",
    },
    "SettingsPage_5Minutes": {
        "de": "5 Minuten", "es": "5 minutos", "fr": "5 minutes",
        "pl": "5 minut", "pt-BR": "5 minutos", "ru": "5 минут",
    },
    "SettingsPage_30Minutes": {
        "de": "30 Minuten", "es": "30 minutos", "fr": "30 minutes",
        "pl": "30 minut", "pt-BR": "30 minutos", "ru": "30 минут",
    },
    "SettingsPage_60Minutes": {
        "de": "60 Minuten", "es": "60 minutos", "fr": "60 minutes",
        "pl": "60 minut", "pt-BR": "60 minutos", "ru": "60 минут",
    },
    "SettingsPage_FollowThePaceAbove": {
        "de": "Dem Takt oben folgen",
        "es": "Seguir el ritmo de arriba",
        "fr": "Suivre le rythme ci-dessus",
        "pl": "Zgodnie z tempem powyżej",
        "pt-BR": "Seguir o ritmo acima",
        "ru": "По частоте, заданной выше",
    },
    "SettingsPage_AlertSound": {
        "de": "Benachrichtigungston",
        "es": "Sonido de alerta",
        "fr": "Son d'alerte",
        "pl": "Dźwięk alertu",
        "pt-BR": "Som do alerta",
        "ru": "Звук оповещения",
    },
    "SettingsPage_AlertSoundHint": {
        "de": "Erklingt einmal pro Benachrichtigung, egal an wie viele Orte sie geht. „Stumm“ "
              "zeichnet die Benachrichtigung weiterhin und färbt weiterhin das Tray-Symbol.",
        "es": "Suena una vez por alerta, por muchos destinos que tenga. «Silencio» sigue dibujando "
              "la notificación y sigue coloreando la bandeja.",
        "fr": "Retentit une fois par alerte, quel que soit le nombre de destinations. "
              "« Silencieux » affiche toujours la notification et colore toujours la barre d'état "
              "système.",
        "pl": "Odtwarza się raz na alert, niezależnie od liczby miejsc, do których trafia. „Cisza” "
              "nadal rysuje powiadomienie i nadal koloruje zasobnik.",
        "pt-BR": "Toca uma vez por alerta, para quantos destinos ele for. “Silencioso” continua "
                 "desenhando a notificação e continua colorindo a bandeja.",
        "ru": "Звучит один раз на оповещение, сколько бы адресатов у него ни было. «Без звука» "
              "по-прежнему рисует уведомление и по-прежнему окрашивает значок в трее.",
    },
    "SettingsPage_WhatADesktopAlertSoundsLike": {
        "de": "Wie eine Desktop-Benachrichtigung klingt",
        "es": "Cómo suena una alerta de escritorio",
        "fr": "Le son d'une alerte sur le bureau",
        "pl": "Jak brzmi alert na pulpicie",
        "pt-BR": "Como soa um alerta na área de trabalho",
        "ru": "Как звучит оповещение на рабочем столе",
    },
    "SettingsPage_SoundSilent": {
        "de": "Stumm", "es": "Silencio", "fr": "Silencieux",
        "pl": "Cisza", "pt-BR": "Silencioso", "ru": "Без звука",
    },
    # RoRoRo is a product noun and stays verbatim in all six (PRODUCT_NOUNS guard).
    "SettingsPage_SoundRororoChime": {
        "de": "RoRoRo-Klang",
        "es": "Timbre de RoRoRo",
        "fr": "Carillon RoRoRo",
        "pl": "Dzwonek RoRoRo",
        "pt-BR": "Toque do RoRoRo",
        "ru": "Сигнал RoRoRo",
    },
    "SettingsPage_SoundWindowsDefault": {
        "de": "Windows-Standard",
        "es": "Predeterminado de Windows",
        "fr": "Son par défaut de Windows",
        "pl": "Domyślny dźwięk Windows",
        "pt-BR": "Padrão do Windows",
        "ru": "Стандартный звук Windows",
    },
    "SettingsPage_TheTrayIconColoursWhatever": {
        "de": "Das Tray-Symbol färbt sich für alles, was du hier abwählst, damit du es am PC immer "
              "noch mitbekommst. „Desktop“ abzuwählen nimmt die Benachrichtigung und ihren Ton "
              "weg, nie das Abzeichen.",
        "es": "El icono de la bandeja colorea todo lo que desmarques aquí, así siempre puedes "
              "enterarte en el PC. Desmarcar «Escritorio» quita la notificación y su sonido, nunca "
              "el distintivo.",
        "fr": "L'icône de la barre d'état système se colore pour tout ce que vous décochez ici, "
              "afin que vous puissiez toujours le voir sur le PC. Décocher « Bureau » supprime la "
              "notification et son son, jamais le badge.",
        "pl": "Ikona w zasobniku koloruje wszystko, co tutaj odznaczysz, więc zawsze dowiesz się "
              "przy komputerze. Odznaczenie „Pulpit” usuwa powiadomienie i jego dźwięk, nigdy "
              "plakietkę.",
        "pt-BR": "O ícone da bandeja colore tudo o que você desmarcar aqui, então você sempre "
                 "descobre no PC. Desmarcar “Área de trabalho” tira a notificação e o som dela, "
                 "nunca o selo.",
        "ru": "Значок в трее окрашивается для всего, что вы здесь снимаете, так что вы всё равно "
              "узнаете об этом за компьютером. Снятие «Рабочий стол» убирает уведомление и его "
              "звук, но никогда не значок.",
    },
    # Este's ruling 2026-10-07: idle means IN-GAME idle, and the label has to say so.
    "SettingsPage_AnAccountGoesIdle": {
        "de": "Ein Konto wird in einem Spiel inaktiv",
        "es": "Una cuenta queda inactiva en un juego",
        "fr": "Un compte devient inactif dans un jeu",
        "pl": "Konto staje się bezczynne w grze",
        "pt-BR": "Uma conta fica inativa em um jogo",
        "ru": "Аккаунт простаивает в игре",
    },
    "SettingsPage_SendIdleAlertsToThe": {
        "de": "Inaktivitäts-Benachrichtigungen an den Desktop senden",
        "es": "Enviar alertas de inactividad al escritorio",
        "fr": "Envoyer les alertes d'inactivité au bureau",
        "pl": "Wysyłaj alerty o bezczynności na pulpit",
        "pt-BR": "Enviar alertas de inatividade para a área de trabalho",
        "ru": "Отправлять оповещения о простое на рабочий стол",
    },
    "SettingsPage_SendIdleAlertsToMy": {
        "de": "Inaktivitäts-Benachrichtigungen an meinen Discord-Kanal senden",
        "es": "Enviar alertas de inactividad a mi canal de Discord",
        "fr": "Envoyer les alertes d'inactivité à mon canal Discord",
        "pl": "Wysyłaj alerty o bezczynności na mój kanał Discord",
        "pt-BR": "Enviar alertas de inatividade para o meu canal do Discord",
        "ru": "Отправлять оповещения о простое в мой канал Discord",
    },
    "SettingsPage_SendIdleAlertsToThe_2": {
        "de": "Inaktivitäts-Benachrichtigungen an den Clan-Discord-Kanal senden",
        "es": "Enviar alertas de inactividad al canal de Discord del clan",
        "fr": "Envoyer les alertes d'inactivité au canal Discord du clan",
        "pl": "Wysyłaj alerty o bezczynności na kanał Discord klanu",
        "pt-BR": "Enviar alertas de inatividade para o canal do clã no Discord",
        "ru": "Отправлять оповещения о простое в канал Discord клана",
    },
    "SettingsPage_SendIdleAlertsToMy_2": {
        "de": "Inaktivitäts-Benachrichtigungen an mein Handy senden",
        "es": "Enviar alertas de inactividad a mi teléfono",
        "fr": "Envoyer les alertes d'inactivité à mon téléphone",
        "pl": "Wysyłaj alerty o bezczynności na mój telefon",
        "pt-BR": "Enviar alertas de inatividade para o meu celular",
        "ru": "Отправлять оповещения о простое на мой телефон",
    },
    "SettingsPage_NobodyHasTouchedTheAccount": {
        "de": "Wie lange ein Konto ohne Eingabe von dir dasteht, bevor es als inaktiv gilt. Nur "
              "Konten, die tatsächlich in einem Spiel sind, lösen diese Benachrichtigung aus, denn "
              "nur sie können dafür hinausgeworfen werden. Der Chip in der Zeile erscheint ohnehin "
              "im selben Moment, egal wie diese Benachrichtigung eingestellt ist.",
        "es": "Cuánto tiempo pasa una cuenta sin recibir nada de ti antes de contar como inactiva. "
              "Solo las cuentas que están realmente en un juego generan esta alerta, ya que solo a "
              "ellas se las puede expulsar por ello. El distintivo de la fila aparece en el mismo "
              "momento de todos modos, sea cual sea el ajuste de esta alerta.",
        "fr": "Combien de temps un compte reste sans aucune action de votre part avant d'être "
              "considéré comme inactif. Seuls les comptes réellement dans un jeu déclenchent cette "
              "alerte, puisque seuls eux peuvent en être expulsés. La puce de la ligne apparaît au "
              "même moment de toute façon, quel que soit le réglage de cette alerte.",
        "pl": "Jak długo konto pozostaje bez żadnych działań z twojej strony, zanim zostanie "
              "uznane za bezczynne. Ten alert wywołują tylko konta, które naprawdę są w grze, bo "
              "tylko one mogą za to zostać wyrzucone. Plakietka w wierszu pojawia się i tak w tym "
              "samym momencie, niezależnie od ustawienia tego alertu.",
        "pt-BR": "Quanto tempo uma conta fica sem nenhuma ação sua antes de contar como inativa. "
                 "Só contas que estão realmente em um jogo geram este alerta, já que só elas podem "
                 "ser expulsas por isso. O selo da linha aparece no mesmo momento de qualquer "
                 "forma, seja qual for o ajuste deste alerta.",
        "ru": "Сколько времени аккаунт остаётся без ваших действий, прежде чем считается "
              "простаивающим. Это оповещение вызывают только аккаунты, которые действительно "
              "находятся в игре, ведь только их за это могут выкинуть. Значок в строке появляется "
              "в тот же момент в любом случае, независимо от настройки этого оповещения.",
    },
    # The six per-kind cadence labels. Each reuses that kind's established noun phrase.
    "SettingsPage_HowOftenDropOutAlertsMay": {
        "de": "Wie oft sich Ausfall-Benachrichtigungen höchstens wiederholen dürfen",
        "es": "Con qué frecuencia pueden repetirse como máximo las alertas de desconexión",
        "fr": "À quelle fréquence les alertes de décrochage peuvent se répéter au maximum",
        "pl": "Jak często najwyżej mogą powtarzać się alerty o wypadnięciu",
        "pt-BR": "Com que frequência os alertas de queda podem repetir no máximo",
        "ru": "Как часто оповещения об отвале могут повторяться не более",
    },
    "SettingsPage_HowOftenMemoryWarningsMay": {
        "de": "Wie oft sich Speicherwarnungen höchstens wiederholen dürfen",
        "es": "Con qué frecuencia pueden repetirse como máximo las advertencias de memoria",
        "fr": "À quelle fréquence les avertissements de mémoire peuvent se répéter au maximum",
        "pl": "Jak często najwyżej mogą powtarzać się ostrzeżenia o pamięci",
        "pt-BR": "Com que frequência os avisos de memória podem repetir no máximo",
        "ru": "Как часто предупреждения о памяти могут повторяться не более",
    },
    "SettingsPage_HowOftenIdleAlertsMay": {
        "de": "Wie oft sich Inaktivitäts-Benachrichtigungen höchstens wiederholen dürfen",
        "es": "Con qué frecuencia pueden repetirse como máximo las alertas de inactividad",
        "fr": "À quelle fréquence les alertes d'inactivité peuvent se répéter au maximum",
        "pl": "Jak często najwyżej mogą powtarzać się alerty o bezczynności",
        "pt-BR": "Com que frequência os alertas de inatividade podem repetir no máximo",
        "ru": "Как часто оповещения о простое могут повторяться не более",
    },
    "SettingsPage_HowOftenRecycleNoticesMay": {
        "de": "Wie oft sich Neu-verbinden-Hinweise höchstens wiederholen dürfen",
        "es": "Con qué frecuencia pueden repetirse como máximo los avisos de reconexión",
        "fr": "À quelle fréquence les avis de recyclage peuvent se répéter au maximum",
        "pl": "Jak często najwyżej mogą powtarzać się powiadomienia o ponownym połączeniu",
        "pt-BR": "Com que frequência os avisos de reciclagem podem repetir no máximo",
        "ru": "Как часто уведомления о переподключении могут повторяться не более",
    },
    "SettingsPage_HowOftenAutoRejoinPausedAlertsMay": {
        "de": "Wie oft sich Neubeitritt-Pause-Benachrichtigungen höchstens wiederholen dürfen",
        "es": "Con qué frecuencia pueden repetirse como máximo las alertas de reingreso pausado",
        "fr": "À quelle fréquence les alertes de reconnexion suspendue peuvent se répéter au maximum",
        "pl": "Jak często najwyżej mogą powtarzać się alerty o wstrzymaniu ponownego dołączania",
        "pt-BR": "Com que frequência os alertas de reentrada pausada podem repetir no máximo",
        "ru": "Как часто оповещения о паузе автоперезахода могут повторяться не более",
    },
    "SettingsPage_HowOftenMetricAlertsMay": {
        "de": "Wie oft sich Metrik-Benachrichtigungen höchstens wiederholen dürfen",
        "es": "Con qué frecuencia pueden repetirse como máximo las alertas de métricas",
        "fr": "À quelle fréquence les alertes métriques peuvent se répéter au maximum",
        "pl": "Jak często najwyżej mogą powtarzać się alerty o wskaźnikach",
        "pt-BR": "Com que frequência os alertas de métricas podem repetir no máximo",
        "ru": "Как часто оповещения о метриках могут повторяться не более",
    },
    "SettingsPage_FixedAtTwoHours": {
        "de": "Fest auf zwei Stunden.",
        "es": "Fijo en dos horas.",
        "fr": "Fixé à deux heures.",
        "pl": "Ustalone na dwie godziny.",
        "pt-BR": "Fixo em duas horas.",
        "ru": "Фиксировано: два часа.",
    },
}


# Translation rows whose NEUTRAL key item 6 deleted when it collapsed the three alert cards into one
# section. gen-culture-resx.py refuses to render a catalog carrying a key the neutral file does not
# have, and it is right to: a stale row reads as coverage. Pruned here with the reason recorded,
# rather than hand-deleted out of six JSON files.
STALE = {
    "SettingsPage_MuteIdleAlerts": "the single mute flag item 3 replaced with per-destination ticks",
    "SettingsPage_RororoShowsOneTrayToast": "the old tray-toast hint, gone with the drawn balloon",
    "SettingsPage_IdleAccounts": "card header; the three cards became one Alerts section",
    "SettingsPage_Memory": "card header; same consolidation",
}


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

    # Guard the prune against a typo or a key that came back: only drop what the neutral catalog
    # really no longer has.
    neutral = neutral_keys()
    resurrected = sorted(k for k in STALE if k in neutral)
    if resurrected:
        raise SystemExit("STALE names a key the neutral catalog still has: " + ", ".join(resurrected))

    for culture in CULTURES:
        path = TRANS_DIR / f"ui-{culture}.json"
        data = json.loads(path.read_text(encoding="utf-8"))
        if not isinstance(data, dict):
            raise SystemExit(f"{path.name} is not a flat key->value object")

        pruned = 0
        for key, why in STALE.items():
            if key in data:
                del data[key]
                pruned += 1
                if culture == CULTURES[0]:
                    print(f"  pruning {key} — {why}")

        # Anything else the neutral catalog has dropped is a surprise; name it rather than carry it.
        orphans = sorted(k for k in data if k not in neutral)
        if orphans:
            raise SystemExit(f"{culture}: {len(orphans)} translation row(s) with no neutral key and "
                             f"no STALE entry: {', '.join(orphans[:8])}. Add them to STALE with a "
                             "reason, or restore the neutral key.")

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

        path.write_text(json.dumps(data, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
                        encoding="utf-8")
        print(f"  {culture:<5} added {added:>2}, overwrote {changed:>2}, kept {kept:>2}, "
              f"pruned {pruned:>2}  -> {len(data)} keys")

    print("\nNow run: python -I scripts/gen-culture-resx.py <culture>  for each of "
          + ", ".join(CULTURES))


if __name__ == "__main__":
    main()
