#!/usr/bin/env python3
"""Merge the v1.36 account-library cycle's new UI keys into the six per-culture translation sets.

CUMULATIVE FOR THE v1.36 CYCLE. Item 2 (health vocabulary) wrote the first block; every later item
that adds a neutral key appends its own block to T below, under a comment naming the item, and
re-runs this script. It is idempotent: a key already present with the same value is kept, one that
differs is left alone unless --force. Nothing here is hand-edited into a Strings.<culture>.resx;
gen-culture-resx.py renders those from docs/store/translations/ui-<culture>.json.

Wording is matched to the SHIPPED catalog rather than freshly invented: "Session expired" /
"Limited by Roblox" reuse Shell_Status_SessionExpired / Shell_Status_Limited, "Re-authenticate"
reuses MainWindow_Reauthenticate, "Needs verification" takes the catalog's own verification noun
(Verifizierung, verificacion, verification, weryfikacja, verificacao, proverka), "account" is the
catalog's word per language (Konto, cuenta, compte, konto, conta, akkaunt), and the follow verbs
follow MainWindow_Follow_2 and MainWindow_JoinViaFriendChallengeProne. RoRoRo and Roblox stay English.

    python -I scripts/add-v136-translations.py          # merge, refuse to overwrite a different value
    python -I scripts/add-v136-translations.py --force  # overwrite existing values too
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
    # ---- item 2: health vocabulary ----------------------------------------------------------
    "Shell_Health_Expired": {
        "de": "Sitzung abgelaufen",
        "es": "Sesión caducada",
        "fr": "Session expirée",
        "pl": "Sesja wygasła",
        "pt-BR": "Sessão expirada",
        "ru": "Сессия истекла",
    },
    "Shell_Health_NeedsVerification": {
        "de": "Verifizierung nötig",
        "es": "Requiere verificación",
        "fr": "Vérification requise",
        "pl": "Wymaga weryfikacji",
        "pt-BR": "Requer verificação",
        "ru": "Нужна проверка",
    },
    "Shell_Health_Limited": {
        "de": "Von Roblox eingeschränkt",
        "es": "Limitado por Roblox",
        "fr": "Limité par Roblox",
        "pl": "Ograniczone przez Roblox",
        "pt-BR": "Limitado pelo Roblox",
        "ru": "Ограничено Roblox",
    },
    "Shell_Health_FollowMarked": {
        "de": "Tritt per Folgen bei",
        "es": "Se une siguiendo",
        "fr": "Rejoint en suivant",
        "pl": "Dołącza przez podążanie",
        "pt-BR": "Entra seguindo",
        "ru": "Присоединяется следом",
    },
    "Shell_Health_Action_Reauthenticate": {
        "de": "Erneut anmelden",
        "es": "Volver a autenticar",
        "fr": "Se réauthentifier",
        "pl": "Uwierzytelnij ponownie",
        "pt-BR": "Reautenticar",
        "ru": "Пройти аутентификацию заново",
    },
    "Shell_Health_Action_OpenRoblox": {
        "de": "Roblox für dieses Konto öffnen",
        "es": "Abrir Roblox para esta cuenta",
        "fr": "Ouvrir Roblox pour ce compte",
        "pl": "Otwórz Roblox dla tego konta",
        "pt-BR": "Abrir o Roblox para esta conta",
        "ru": "Открыть Roblox для этого аккаунта",
    },
    "Shell_Health_Action_ToggleMark": {
        "de": "Markierung umschalten",
        "es": "Alternar la marca",
        "fr": "Basculer la marque",
        "pl": "Przełącz oznaczenie",
        "pt-BR": "Alternar a marcação",
        "ru": "Переключить отметку",
    },
    "Shell_Health_BadgeName": {
        "de": "{0}, {1}",
        "es": "{0}, {1}",
        "fr": "{0}, {1}",
        "pl": "{0}, {1}",
        "pt-BR": "{0}, {1}",
        "ru": "{0}, {1}",
    },
    "Shell_Status_NeedsVerification": {
        "de": "Verifizierung nötig — Roblox für dieses Konto öffnen",
        "es": "Requiere verificación: abre Roblox para esta cuenta",
        "fr": "Vérification requise — ouvrez Roblox pour ce compte",
        "pl": "Wymaga weryfikacji — otwórz Roblox dla tego konta",
        "pt-BR": "Requer verificação — abra o Roblox para esta conta",
        "ru": "Нужна проверка — откройте Roblox для этого аккаунта",
    },
    "Shell_Status_FollowMarkedReady": {
        "de": "Tritt per Folgen bei · Bereit",
        "es": "Se une siguiendo · Listo",
        "fr": "Rejoint en suivant · Prêt",
        "pl": "Dołącza przez podążanie · Gotowe",
        "pt-BR": "Entra seguindo · Pronto",
        "ru": "Присоединяется следом · Готово",
    },
    "Shell_Launch_Clause_FollowMarked_one": {
        "de": "{0} folgt dem Hauptkonto",
        "es": "{0} seguirá a la cuenta principal",
        "fr": "{0} suivra le compte principal",
        "pl": "{0} podąży za kontem głównym",
        "pt-BR": "{0} seguirá a conta principal",
        "ru": "{0} последует за основным аккаунтом",
    },
    "Shell_Launch_Clause_FollowMarked_other": {
        "de": "{0} folgen dem Hauptkonto",
        "es": "{0} seguirán a la cuenta principal",
        "fr": "{0} suivront le compte principal",
        "pl": "{0} podąży za kontem głównym",
        "pt-BR": "{0} seguirão a conta principal",
        "ru": "{0} последуют за основным аккаунтом",
    },
    "Shell_Launch_Clause_FollowMarked_few": {
        "de": "{0} folgen dem Hauptkonto",
        "es": "{0} seguirán a la cuenta principal",
        "fr": "{0} suivront le compte principal",
        "pl": "{0} podążą za kontem głównym",
        "pt-BR": "{0} seguirão a conta principal",
        "ru": "{0} последуют за основным аккаунтом",
    },
    "Shell_Launch_Clause_FollowMarked_many": {
        "de": "{0} folgen dem Hauptkonto",
        "es": "{0} seguirán a la cuenta principal",
        "fr": "{0} suivront le compte principal",
        "pl": "{0} podąży za kontem głównym",
        "pt-BR": "{0} seguirão a conta principal",
        "ru": "{0} последуют за основным аккаунтом",
    },
    # ---- item 4: the Library join ------------------------------------------------------------
    # The history row's game name for a follow launch, until presence names the place. Verbs match
    # Shell_Msg_FollowingFromSource per language.
    "Shell_History_FollowingAFriend": {
        "de": "Folgt einem Freund",
        "es": "Siguiendo a un amigo",
        "fr": "Suit un ami",
        "pl": "Podąża za znajomym",
        "pt-BR": "Seguindo um amigo",
        "ru": "Следует за другом",
    },
    # ---- item 6: the facts line --------------------------------------------------------------
    "Shell_Fact_Masked": {
        "de": "••••",
        "es": "••••",
        "fr": "••••",
        "pl": "••••",
        "pt-BR": "••••",
        "ru": "••••",
    },
    "Shell_Fact_Hidden": {
        "de": "verborgen",
        "es": "oculto",
        "fr": "masqué",
        "pl": "ukryte",
        "pt-BR": "oculto",
        "ru": "скрыто",
    },
    "Shell_Fact_Unknown": {
        "de": "unbekannt",
        "es": "desconocido",
        "fr": "inconnu",
        "pl": "nieznane",
        "pt-BR": "desconhecido",
        "ru": "неизвестно",
    },
    "Shell_Fact_Robux_Name": {
        "de": "Robux, {0}",
        "es": "Robux, {0}",
        "fr": "Robux, {0}",
        "pl": "Robux, {0}",
        "pt-BR": "Robux, {0}",
        "ru": "Robux, {0}",
    },
    "Shell_Fact_Membership_Name": {
        "de": "Mitgliedschaft, {0}",
        "es": "Membresía, {0}",
        "fr": "Abonnement, {0}",
        "pl": "Członkostwo, {0}",
        "pt-BR": "Assinatura, {0}",
        "ru": "Подписка, {0}",
    },
    "Shell_Fact_Friends_Name": {
        "de": "Freunde online, {0}",
        "es": "Amigos en línea, {0}",
        "fr": "Amis en ligne, {0}",
        "pl": "Znajomi online, {0}",
        "pt-BR": "Amigos online, {0}",
        "ru": "Друзья в сети, {0}",
    },
    "Shell_Fact_LastPlayed_Name": {
        "de": "Zuletzt gespielt, {0}",
        "es": "Última partida, {0}",
        "fr": "Dernière partie, {0}",
        "pl": "Ostatnio grane, {0}",
        "pt-BR": "Jogado por último, {0}",
        "ru": "Последняя игра, {0}",
    },
    "Shell_Fact_Note_Name": {
        "de": "Notiz, {0}",
        "es": "Nota, {0}",
        "fr": "Note, {0}",
        "pl": "Notatka, {0}",
        "pt-BR": "Nota, {0}",
        "ru": "Заметка, {0}",
    },
    "Shell_Fact_FriendsOnline_one": {
        "de": "{0} Freund online",
        "es": "{0} amigo en línea",
        "fr": "{0} ami en ligne",
        "pl": "{0} znajomy online",
        "pt-BR": "{0} amigo online",
        "ru": "{0} друг в сети",
    },
    "Shell_Fact_FriendsOnline_other": {
        "de": "{0} Freunde online",
        "es": "{0} amigos en línea",
        "fr": "{0} amis en ligne",
        "pl": "{0} znajomych online",
        "pt-BR": "{0} amigos online",
        "ru": "{0} друга в сети",
    },
    "Shell_Fact_FriendsOnline_few": {
        "de": "{0} Freunde online",
        "es": "{0} amigos en línea",
        "fr": "{0} amis en ligne",
        "pl": "{0} znajomych online",
        "pt-BR": "{0} amigos online",
        "ru": "{0} друга в сети",
    },
    "Shell_Fact_FriendsOnline_many": {
        "de": "{0} Freunde online",
        "es": "{0} amigos en línea",
        "fr": "{0} amis en ligne",
        "pl": "{0} znajomych online",
        "pt-BR": "{0} amigos online",
        "ru": "{0} друзей в сети",
    },
    "Shell_Fact_Updated": {
        "de": "Aktualisiert {0}",
        "es": "Actualizado {0}",
        "fr": "Mis à jour {0}",
        "pl": "Zaktualizowano {0}",
        "pt-BR": "Atualizado {0}",
        "ru": "Обновлено {0}",
    },
    "Shell_Fact_UpdatedFailed": {
        "de": "Aktualisiert {0} · letzter Versuch fehlgeschlagen ({1})",
        "es": "Actualizado {0} · el último intento falló ({1})",
        "fr": "Mis à jour {0} · dernier essai échoué ({1})",
        "pl": "Zaktualizowano {0} · ostatnia próba nie powiodła się ({1})",
        "pt-BR": "Atualizado {0} · a última tentativa falhou ({1})",
        "ru": "Обновлено {0} · последняя попытка не удалась ({1})",
    },
    "Shell_Fact_NeverUpdatedFailed": {
        "de": "Noch nicht aktualisiert · letzter Versuch fehlgeschlagen ({0})",
        "es": "Aún sin actualizar · el último intento falló ({0})",
        "fr": "Pas encore mis à jour · dernier essai échoué ({0})",
        "pl": "Jeszcze nie zaktualizowano · ostatnia próba nie powiodła się ({0})",
        "pt-BR": "Ainda não atualizado · a última tentativa falhou ({0})",
        "ru": "Ещё не обновлено · последняя попытка не удалась ({0})",
    },
}


# Translation rows whose NEUTRAL key a later item deletes belong here, with the reason recorded,
# rather than hand-deleted out of six JSON files. gen-culture-resx.py refuses to render a catalog
# carrying a key the neutral file does not have, and it is right to: a stale row reads as coverage.
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
