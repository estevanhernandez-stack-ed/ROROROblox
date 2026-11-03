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
        "pl": "Dołącza przez znajomego",
        "pt-BR": "Entra seguindo",
        "ru": "Присоединяется следом",
    },
    "Shell_Health_Action_Reauthenticate": {
        "de": "Neu anmelden",
        "es": "Reautenticar",
        "fr": "Se réauthentifier",
        "pl": "Zaloguj ponownie",
        "pt-BR": "Reautenticar",
        "ru": "Войти заново",
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
        "pt-BR": "Última partida, {0}",
        "ru": "Последний запуск, {0}",
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
    # ---- item 7: the account panel (B1) -----------------------------------------------------
    "Shell_Panel_Expander_Name": {
        "de": "Letzte Spiele von {0} anzeigen",
        "es": "Mostrar juegos recientes de {0}",
        "fr": "Afficher les jeux récents de {0}",
        "pl": "Pokaż ostatnie gry konta {0}",
        "pt-BR": "Mostrar jogos recentes de {0}",
        "ru": "Показать недавние игры для {0}",
    },
    "Shell_Panel_Heading_Recent": {
        "de": "ZULETZT",
        "es": "RECIENTES",
        "fr": "RÉCENTS",
        "pl": "OSTATNIE",
        "pt-BR": "RECENTES",
        "ru": "НЕДАВНИЕ",
    },
    "Shell_Panel_Heading_Favourites": {
        "de": "FAVORITEN",
        "es": "FAVORITOS",
        "fr": "FAVORIS",
        "pl": "ULUBIONE",
        "pt-BR": "FAVORITOS",
        "ru": "ИЗБРАННОЕ",
    },
    "Shell_Panel_Heading_PrivateServers": {
        "de": "PRIVATE SERVER",
        "es": "SERVIDORES PRIVADOS",
        "fr": "SERVEURS PRIVÉS",
        "pl": "PRYWATNE SERWERY",
        "pt-BR": "SERVIDORES PRIVADOS",
        "ru": "ПРИВАТНЫЕ СЕРВЕРЫ",
    },
    "Shell_Panel_Empty_Recent": {
        "de": "Noch nichts gespielt. Starte ein Spiel, dann erscheint es hier.",
        "es": "Todavía no se ha jugado nada. Lanza un juego y aparecerá aquí.",
        "fr": "Rien n'a encore été joué. Lancez un jeu et il apparaîtra ici.",
        "pl": "Jeszcze w nic nie grano. Uruchom grę, a pojawi się tutaj.",
        "pt-BR": "Nada jogado ainda. Inicie um jogo e ele aparece aqui.",
        "ru": "Пока ничего не запускалось. Запустите игру, и она появится здесь.",
    },
    "Shell_Panel_Empty_Favourites": {
        "de": "Noch keine Favoriten. Speichere einen aus „Zuletzt“ oder füge Spiele in der Bibliothek hinzu.",
        "es": "Aún no hay favoritos. Guarda uno desde Recientes o añade juegos en la Biblioteca.",
        "fr": "Aucun favori pour l'instant. Enregistrez-en un depuis Récents ou ajoutez des jeux dans la Bibliothèque.",
        "pl": "Brak ulubionych. Zapisz grę z listy Ostatnie albo dodaj gry w Bibliotece.",
        "pt-BR": "Nenhum favorito ainda. Salve um de Recentes ou adicione jogos na Biblioteca.",
        "ru": "Пока нет избранного. Сохраните игру из списка «Недавние» или добавьте игры в Библиотеке.",
    },
    "Shell_Panel_Empty_PrivateServers": {
        "de": "Keine privaten Server gespeichert. Füge einen Serverlink in der Bibliothek ein, um einen hinzuzufügen.",
        "es": "No hay servidores privados guardados. Pega un enlace de servidor en la Biblioteca para añadir uno.",
        "fr": "Aucun serveur privé enregistré. Collez un lien de serveur dans la Bibliothèque pour en ajouter un.",
        "pl": "Brak zapisanych prywatnych serwerów. Wklej link do serwera w Bibliotece, aby go dodać.",
        "pt-BR": "Nenhum servidor privado salvo. Cole um link de servidor na Biblioteca para adicionar um.",
        "ru": "Нет сохранённых приватных серверов. Вставьте ссылку на сервер в Библиотеке, чтобы добавить его.",
    },
    "Shell_Panel_OpenHome": {
        "de": "Roblox-Startseite öffnen",
        "es": "Abrir el inicio de Roblox",
        "fr": "Ouvrir l'accueil de Roblox",
        "pl": "Otwórz stronę główną Roblox",
        "pt-BR": "Abrir a página inicial do Roblox",
        "ru": "Открыть главную Roblox",
    },
    "Shell_Panel_OpenHome_Name": {
        "de": "Roblox-Startseite als {0} öffnen",
        "es": "Abrir el inicio de Roblox como {0}",
        "fr": "Ouvrir l'accueil de Roblox en tant que {0}",
        "pl": "Otwórz stronę główną Roblox jako {0}",
        "pt-BR": "Abrir a página inicial do Roblox como {0}",
        "ru": "Открыть главную Roblox как {0}",
    },
    "Shell_Panel_Note_Placeholder": {
        "de": "Private Notiz, nur für dich sichtbar",
        "es": "Nota privada, solo tú la ves",
        "fr": "Note privée, visible de vous seul",
        "pl": "Prywatna notatka, widoczna tylko dla Ciebie",
        "pt-BR": "Nota privada, só você vê",
        "ru": "Личная заметка, видна только вам",
    },
    "Shell_Panel_Note_Name": {
        "de": "Private Notiz für {0}",
        "es": "Nota privada para {0}",
        "fr": "Note privée pour {0}",
        "pl": "Prywatna notatka dla {0}",
        "pt-BR": "Nota privada para {0}",
        "ru": "Личная заметка для {0}",
    },
    "Shell_Panel_Save": {
        "de": "Speichern",
        "es": "Guardar",
        "fr": "Enregistrer",
        "pl": "Zapisz",
        "pt-BR": "Salvar",
        "ru": "Сохранить",
    },
    "Shell_Panel_Save_Name": {
        "de": "{0} zu den Favoriten hinzufügen",
        "es": "Guardar {0} en favoritos",
        "fr": "Enregistrer {0} dans les favoris",
        "pl": "Zapisz {0} w ulubionych",
        "pt-BR": "Salvar {0} nos favoritos",
        "ru": "Сохранить {0} в избранное",
    },
    "Shell_Panel_Entry_Name": {
        "de": "{0} starten, {1}",
        "es": "Lanzar {0}, {1}",
        "fr": "Lancer {0}, {1}",
        "pl": "Uruchom {0}, {1}",
        "pt-BR": "Iniciar {0}, {1}",
        "ru": "Запустить {0}, {1}",
    },
    "Shell_Panel_Tag_Default": {
        "de": "STANDARD",
        "es": "PREDET.",
        "fr": "PAR DÉFAUT",
        "pl": "DOMYŚLNE",
        "pt-BR": "PADRÃO",
        "ru": "ПО УМОЛЧ.",
    },
    "Shell_Panel_Tag_Private": {
        "de": "PRIVAT",
        "es": "PRIVADO",
        "fr": "PRIVÉ",
        "pl": "PRYWATNY",
        "pt-BR": "PRIVADO",
        "ru": "ПРИВАТНЫЙ",
    },
    "Shell_Panel_Meta_NotPlayed": {
        "de": "Von diesem Konto nicht gespielt",
        "es": "Esta cuenta no lo ha jugado",
        "fr": "Pas joué par ce compte",
        "pl": "Nie grano z tego konta",
        "pt-BR": "Não jogado por esta conta",
        "ru": "Этот аккаунт не играл",
    },
    "Shell_Panel_Meta_InRecent": {
        "de": "{0} in letzten Sitzungen",
        "es": "{0} en sesiones recientes",
        "fr": "{0} dans les sessions récentes",
        "pl": "{0} w ostatnich sesjach",
        "pt-BR": "{0} em sessões recentes",
        "ru": "{0} в недавних сессиях",
    },
    "Shell_Panel_Duration_Minutes": {
        "de": "{0} Min.",
        "es": "{0} min",
        "fr": "{0} min",
        "pl": "{0} min",
        "pt-BR": "{0} min",
        "ru": "{0} мин",
    },
    "Shell_Panel_Duration_Hours": {
        "de": "{0} Std.",
        "es": "{0} h",
        "fr": "{0} h",
        "pl": "{0} godz",
        "pt-BR": "{0} h",
        "ru": "{0} ч",
    },
    "Shell_Panel_Duration_LessThanMinute": {
        "de": "<1 Min.",
        "es": "<1 min",
        "fr": "<1 min",
        "pl": "<1 min",
        "pt-BR": "<1 min",
        "ru": "<1 мин",
    },
    "Shell_Panel_Entry_PrivateUnsaved": {
        "de": "Dieser private Server ist nicht gespeichert und lässt sich von hier nicht erneut starten. Speichere seinen Link in der Bibliothek.",
        "es": "Este servidor privado no está guardado, así que no se puede volver a lanzar desde aquí. Guarda su enlace en la Biblioteca.",
        "fr": "Ce serveur privé n'est pas enregistré, il ne peut donc pas être relancé d'ici. Enregistrez son lien dans la Bibliothèque.",
        "pl": "Ten prywatny serwer nie jest zapisany, więc nie można go stąd ponownie uruchomić. Zapisz jego link w Bibliotece.",
        "pt-BR": "Este servidor privado não está salvo, então não dá para reiniciá-lo daqui. Salve o link dele na Biblioteca.",
        "ru": "Этот приватный сервер не сохранён, поэтому запустить его отсюда снова нельзя. Сохраните его ссылку в Библиотеке.",
    },
    # ---- item 8: the Library rename (VALUES of existing keys change; keys keep their names) -----
    # Word per culture is the one item 7's Shell_Panel_Empty_* lines already use: Bibliothek,
    # Biblioteca, Bibliotheque, Biblioteka, Biblioteca, Biblioteka (ru: Biblioteka in Cyrillic).
    "ShellWindow_Games": {
        "de": "Bibliothek",
        "es": "Biblioteca",
        "fr": "Bibliothèque",
        "pl": "Biblioteka",
        "pt-BR": "Biblioteca",
        "ru": "Библиотека",
    },
    "Shell_Title_Games": {
        "de": "Bibliothek",
        "es": "Biblioteca",
        "fr": "Bibliothèque",
        "pl": "Biblioteka",
        "pt-BR": "Biblioteca",
        "ru": "Библиотека",
    },
    "MainWindow_Games": {
        "de": "Bibliothek",
        "es": "Biblioteca",
        "fr": "Bibliothèque",
        "pl": "Biblioteka",
        "pt-BR": "Biblioteca",
        "ru": "Библиотека",
    },
    "GamesPage_Games": {
        "de": "Bibliothek",
        "es": "Biblioteca",
        "fr": "Bibliothèque",
        "pl": "Biblioteka",
        "pt-BR": "Biblioteca",
        "ru": "Библиотека",
    },
    "MainWindow_ManageGames": {
        "de": "Bibliothek verwalten…",
        "es": "Gestionar la Biblioteca…",
        "fr": "Gérer la Bibliothèque…",
        "pl": "Zarządzaj Biblioteką…",
        "pt-BR": "Gerenciar a Biblioteca…",
        "ru": "Управление Библиотекой…",
    },
    # The games-box placeholder on the account row. The empty box grows to fit it, so it is kept as
    # short as the meaning allows in every language.
    "MainWindow_AddGamesFromToolsGames": {
        "de": "In der Bibliothek hinzufügen",
        "es": "Añade en la Biblioteca",
        "fr": "Ajoutez via la Bibliothèque",
        "pl": "Dodaj w Bibliotece",
        "pt-BR": "Adicione na Biblioteca",
        "ru": "Добавьте в Библиотеку",
    },
    # ---- item 9: the Library page (grid, list, chips, one box, Play as) ----------------------
    "Library_Box_Placeholder": {
        "de": "Spiele durchsuchen oder Roblox-Link einfügen",
        "es": "Busca tus juegos o pega un enlace de Roblox",
        "fr": "Cherchez vos jeux ou collez un lien Roblox",
        "pl": "Szukaj w swoich grach lub wklej link Roblox",
        "pt-BR": "Pesquise seus jogos ou cole um link do Roblox",
        "ru": "Ищите свои игры или вставьте ссылку Roblox",
    },
    "Library_Box_Name": {
        "de": "Bibliothek durchsuchen oder Roblox-Link einfügen",
        "es": "Buscar en la Biblioteca o pegar un enlace de Roblox",
        "fr": "Rechercher dans la Bibliothèque ou coller un lien Roblox",
        "pl": "Przeszukaj Bibliotekę lub wklej link Roblox",
        "pt-BR": "Pesquisar na Biblioteca ou colar um link do Roblox",
        "ru": "Поиск по Библиотеке или вставка ссылки Roblox",
    },
    "Library_Box_Tip": {
        "de": "Tippen filtert. Enter fügt ein eingefügtes Spiel, eine Place-ID oder einen Link zu einem privaten Server hinzu oder durchsucht Roblox.",
        "es": "Escribe para filtrar. Intro añade un juego, un id de lugar o un enlace de servidor privado pegado, o busca en Roblox.",
        "fr": "Tapez pour filtrer. Entrée ajoute un jeu, un identifiant de lieu ou un lien de serveur privé collé, ou lance une recherche sur Roblox.",
        "pl": "Pisz, aby filtrować. Enter dodaje wklejoną grę, ID miejsca lub link do prywatnego serwera albo przeszukuje Roblox.",
        "pt-BR": "Digite para filtrar. Enter adiciona um jogo, ID de local ou link de servidor privado colado, ou pesquisa no Roblox.",
        "ru": "Печатайте для фильтрации. Enter добавляет вставленную игру, ID места или ссылку на приватный сервер либо запускает поиск в Roblox.",
    },
    "Library_View_Name": {
        "de": "Bibliothek-Ansicht",
        "es": "Vista de la Biblioteca",
        "fr": "Affichage de la Bibliothèque",
        "pl": "Widok Biblioteki",
        "pt-BR": "Exibição da Biblioteca",
        "ru": "Вид Библиотеки",
    },
    "Library_View_Grid": {
        "de": "Raster",
        "es": "Cuadrícula",
        "fr": "Grille",
        "pl": "Siatka",
        "pt-BR": "Grade",
        "ru": "Сетка",
    },
    "Library_View_List": {
        "de": "Liste",
        "es": "Lista",
        "fr": "Liste",
        "pl": "Lista",
        "pt-BR": "Lista",
        "ru": "Список",
    },
    "Library_Filters_Name": {
        "de": "Diese Arten anzeigen",
        "es": "Mostrar estos tipos",
        "fr": "Afficher ces types",
        "pl": "Pokaż te rodzaje",
        "pt-BR": "Mostrar estes tipos",
        "ru": "Показывать эти типы",
    },
    "Library_Chip_Recent": {
        "de": "Zuletzt",
        "es": "Recientes",
        "fr": "Récents",
        "pl": "Ostatnie",
        "pt-BR": "Recentes",
        "ru": "Недавние",
    },
    "Library_Chip_Favourites": {
        "de": "Favoriten",
        "es": "Favoritos",
        "fr": "Favoris",
        "pl": "Ulubione",
        "pt-BR": "Favoritos",
        "ru": "Избранное",
    },
    "Library_Chip_PrivateServers": {
        "de": "Private Server",
        "es": "Servidores privados",
        "fr": "Serveurs privés",
        "pl": "Prywatne serwery",
        "pt-BR": "Servidores privados",
        "ru": "Приватные серверы",
    },
    "Library_Play": {
        "de": "Spielen",
        "es": "Jugar",
        "fr": "Jouer",
        "pl": "Graj",
        "pt-BR": "Jogar",
        "ru": "Играть",
    },
    "Library_PlayAs": {
        "de": "Spielen als",
        "es": "Jugar como",
        "fr": "Jouer avec",
        "pl": "Graj jako",
        "pt-BR": "Jogar como",
        "ru": "Играть как",
    },
    "Library_Play_Name": {
        "de": "{0} spielen",
        "es": "Jugar a {0}",
        "fr": "Jouer à {0}",
        "pl": "Graj w {0}",
        "pt-BR": "Jogar {0}",
        "ru": "Играть в {0}",
    },
    "Library_PlayAs_Name": {
        "de": "{0} mit einem anderen Konto spielen",
        "es": "Jugar a {0} con otra cuenta",
        "fr": "Jouer à {0} avec un autre compte",
        "pl": "Graj w {0} z innego konta",
        "pt-BR": "Jogar {0} com outra conta",
        "ru": "Играть в {0} с другого аккаунта",
    },
    "Library_More_Name": {
        "de": "Weitere Aktionen für {0}",
        "es": "Más acciones para {0}",
        "fr": "Plus d'actions pour {0}",
        "pl": "Więcej działań dla {0}",
        "pt-BR": "Mais ações para {0}",
        "ru": "Другие действия для {0}",
    },
    "Library_Meta_NeverPlayed": {
        "de": "Nie gespielt",
        "es": "Nunca jugado",
        "fr": "Jamais joué",
        "pl": "Nigdy nie grano",
        "pt-BR": "Nunca jogado",
        "ru": "Ещё не играли",
    },
    "Library_Meta_Played": {
        "de": "{0} · {1}",
        "es": "{0} · {1}",
        "fr": "{0} · {1}",
        "pl": "{0} · {1}",
        "pt-BR": "{0} · {1}",
        "ru": "{0} · {1}",
    },
    "Library_Kind_Favourite": {
        "de": "Favorit",
        "es": "Favorito",
        "fr": "Favori",
        "pl": "Ulubiona",
        "pt-BR": "Favorito",
        "ru": "Избранное",
    },
    "Library_Kind_PrivateServer": {
        "de": "Privater Server",
        "es": "Servidor privado",
        "fr": "Serveur privé",
        "pl": "Prywatny serwer",
        "pt-BR": "Servidor privado",
        "ru": "Приватный сервер",
    },
    "Library_Kind_Recent": {
        "de": "Zuletzt",
        "es": "Reciente",
        "fr": "Récent",
        "pl": "Ostatnia",
        "pt-BR": "Recente",
        "ru": "Недавняя",
    },
    "Library_Col_Game": {
        "de": "Spiel",
        "es": "Juego",
        "fr": "Jeu",
        "pl": "Gra",
        "pt-BR": "Jogo",
        "ru": "Игра",
    },
    "Library_Col_Kind": {
        "de": "Art",
        "es": "Tipo",
        "fr": "Type",
        "pl": "Rodzaj",
        "pt-BR": "Tipo",
        "ru": "Тип",
    },
    "Library_Col_LastPlayed": {
        "de": "Zuletzt gespielt",
        "es": "Última partida",
        "fr": "Dernière partie",
        "pl": "Ostatnio grane",
        "pt-BR": "Última partida",
        "ru": "Последний запуск",
    },
    "Library_Col_Total": {
        "de": "Gesamt",
        "es": "Total",
        "fr": "Total",
        "pl": "Łącznie",
        "pt-BR": "Total",
        "ru": "Всего",
    },
    "GamesPage_EmptyLibrary": {
        "de": "Deine Bibliothek ist leer.",
        "es": "Tu Biblioteca está vacía.",
        "fr": "Votre Bibliothèque est vide.",
        "pl": "Twoja Biblioteka jest pusta.",
        "pt-BR": "Sua Biblioteca está vazia.",
        "ru": "Ваша Библиотека пуста.",
    },
    "GamesPage_EmptyLibrary_Hint": {
        "de": "Suche oben nach einem Spiel oder füge den Link eines Spiels oder privaten Servers ein. Gespeicherte Spiele erscheinen im Bereich jedes Kontos.",
        "es": "Busca un juego arriba o pega el enlace de un juego o de un servidor privado. Los juegos guardados aparecen en el panel de cada cuenta.",
        "fr": "Recherchez un jeu ci-dessus ou collez le lien d'un jeu ou d'un serveur privé. Les jeux enregistrés apparaissent dans le panneau de chaque compte.",
        "pl": "Wyszukaj grę powyżej lub wklej link do gry albo prywatnego serwera. Zapisane gry pojawiają się w panelu każdego konta.",
        "pt-BR": "Pesquise um jogo acima ou cole o link de um jogo ou de um servidor privado. Os jogos salvos aparecem no painel de cada conta.",
        "ru": "Найдите игру выше или вставьте ссылку на игру или приватный сервер. Сохранённые игры появятся на панели каждого аккаунта.",
    },
    "Library_NoMatch": {
        "de": "Keine Treffer in deiner Bibliothek. Drücke Enter, um Roblox zu durchsuchen.",
        "es": "Nada en tu Biblioteca coincide. Pulsa Intro para buscar en Roblox.",
        "fr": "Rien dans votre Bibliothèque ne correspond. Appuyez sur Entrée pour chercher sur Roblox.",
        "pl": "Nic w Twojej Bibliotece nie pasuje. Naciśnij Enter, aby przeszukać Roblox.",
        "pt-BR": "Nada na sua Biblioteca corresponde. Pressione Enter para buscar no Roblox.",
        "ru": "В вашей Библиотеке ничего не найдено. Нажмите Enter, чтобы искать в Roblox.",
    },
    "Library_FromRoblox": {
        "de": "Von Roblox",
        "es": "Desde Roblox",
        "fr": "Depuis Roblox",
        "pl": "Z Roblox",
        "pt-BR": "Do Roblox",
        "ru": "Из Roblox",
    },
    "Library_By": {
        "de": "von {0}",
        "es": "de {0}",
        "fr": "par {0}",
        "pl": "autor: {0}",
        "pt-BR": "por {0}",
        "ru": "автор: {0}",
    },
    "Library_Playing": {
        "de": "{0} spielen gerade",
        "es": "{0} jugando",
        "fr": "{0} en jeu",
        "pl": "grają: {0}",
        "pt-BR": "{0} jogando",
        "ru": "играют: {0}",
    },
    "Library_PlaceId": {
        "de": "Place-ID {0}",
        "es": "ID de lugar {0}",
        "fr": "ID du lieu {0}",
        "pl": "ID miejsca {0}",
        "pt-BR": "ID do local {0}",
        "ru": "ID места {0}",
    },
    "Library_Cancel": {
        "de": "Abbrechen",
        "es": "Cancelar",
        "fr": "Annuler",
        "pl": "Anuluj",
        "pt-BR": "Cancelar",
        "ru": "Отмена",
    },
    "Library_PlayAs_Menu_Name": {
        "de": "Konto zum Spielen wählen",
        "es": "Elige una cuenta con la que jugar",
        "fr": "Choisissez un compte pour jouer",
        "pl": "Wybierz konto, którym chcesz grać",
        "pt-BR": "Escolha uma conta para jogar",
        "ru": "Выберите аккаунт для игры",
    },
    "Library_PlayAs_ExpiredReason": {
        "de": "Sitzung abgelaufen. Melde dieses Konto im Hauptfenster erneut an.",
        "es": "Sesión caducada. Vuelve a autenticar esta cuenta en la ventana principal.",
        "fr": "Session expirée. Réauthentifiez ce compte dans la fenêtre principale.",
        "pl": "Sesja wygasła. Uwierzytelnij to konto ponownie w głównym oknie.",
        "pt-BR": "Sessão expirada. Reautentique esta conta na janela principal.",
        "ru": "Сессия истекла. Пройдите аутентификацию для этого аккаунта заново в главном окне.",
    },
    "Library_PlayAs_Item_Name": {
        "de": "{0}, {1}",
        "es": "{0}, {1}",
        "fr": "{0}, {1}",
        "pl": "{0}, {1}",
        "pt-BR": "{0}, {1}",
        "ru": "{0}, {1}",
    },
    "Library_NoAccount": {
        "de": "Füge zuerst ein Konto hinzu, dann kannst du spielen.",
        "es": "Añade primero una cuenta y luego juega.",
        "fr": "Ajoutez d'abord un compte, puis jouez.",
        "pl": "Najpierw dodaj konto, potem graj.",
        "pt-BR": "Adicione uma conta primeiro e depois jogue.",
        "ru": "Сначала добавьте аккаунт, потом играйте.",
    },
    "Shell_Games_PrivateSuffix": {
        "de": "(privater Server)",
        "es": "(servidor privado)",
        "fr": "(serveur privé)",
        "pl": "(prywatny serwer)",
        "pt-BR": "(servidor privado)",
        "ru": "(приватный сервер)",
    },
    "Library_Add_Name": {
        "de": "{0} zur Bibliothek hinzufügen",
        "es": "Añadir {0} a la Biblioteca",
        "fr": "Ajouter {0} à la Bibliothèque",
        "pl": "Dodaj {0} do Biblioteki",
        "pt-BR": "Adicionar {0} à Biblioteca",
        "ru": "Добавить {0} в Библиотеку",
    },
    # ---- C2 rework (2026-11-02): P1, three per column with See all in the header ------------
    "Shell_Panel_SeeAll": {
        "de": "Alle anzeigen",
        "es": "Ver todo",
        "fr": "Tout voir",
        "pl": "Zobacz wszystko",
        "pt-BR": "Ver tudo",
        "ru": "Показать все",
    },
    "Shell_Panel_SeeAll_Favourites_Name": {
        "de": "Alle Favoriten anzeigen",
        "es": "Ver todos los favoritos",
        "fr": "Voir tous les favoris",
        "pl": "Zobacz wszystkie ulubione",
        "pt-BR": "Ver todos os favoritos",
        "ru": "Показать все избранное",
    },
    "Shell_Panel_SeeAll_PrivateServers_Name": {
        "de": "Alle privaten Server anzeigen",
        "es": "Ver todos los servidores privados",
        "fr": "Voir tous les serveurs privés",
        "pl": "Zobacz wszystkie prywatne serwery",
        "pt-BR": "Ver todos os servidores privados",
        "ru": "Показать все приватные серверы",
    },
    # ---- item 10: the tour speaks to one account first --------------------------------------
    # UI names are the app's own labels per culture, reused not re-translated: MainWindow_AddAccount,
    # MainWindow_LaunchMultiple, MainWindow_SquadLaunch, Shell_Title_Games (the Library),
    # Library_Chip_* / Shell_Panel_Heading_* (Recent, Favourites, Private servers) and
    # Shell_Panel_OpenHome (Open Roblox home). Register follows each culture's existing WelcomeWindow rows.
    "WelcomeWindow_OnePlaceToLaunchRoblox": {
        "de": "Ein Ort zum Starten von Roblox: dein Konto, deine Spiele, deine Einstellungen. Füge später weitere Konten hinzu, dann laufen sie nebeneinander.",
        "es": "Un lugar para iniciar Roblox: tu cuenta, tus juegos, tus ajustes. Añade más cuentas después y se ejecutarán una al lado de la otra.",
        "fr": "Un seul endroit pour lancer Roblox : votre compte, vos jeux, vos réglages. Ajoutez d'autres comptes plus tard et ils s'exécuteront côte à côte.",
        "pl": "Jedno miejsce do uruchamiania Roblox: twoje konto, twoje gry, twoje ustawienia. Dodaj później więcej kont, a będą działać obok siebie.",
        "pt-BR": "Um só lugar para iniciar o Roblox: sua conta, seus jogos, suas configurações. Adicione mais contas depois e elas rodam lado a lado.",
        "ru": "Одно место, чтобы запускать Roblox: ваш аккаунт, ваши игры, ваши настройки. Добавьте аккаунты позже, и они будут работать бок о бок.",
    },
    "WelcomeWindow_PickOrPlayFromThePanel": {
        "de": "Spiele über das Panel unter deinem Konto.",
        "es": "Juega desde el panel bajo tu cuenta.",
        "fr": "Jouez depuis le panneau sous votre compte.",
        "pl": "Graj z panelu pod swoim kontem.",
        "pt-BR": "Jogue pelo painel sob a sua conta.",
        "ru": "Играйте из панели под вашим аккаунтом.",
    },
    "WelcomeWindow_PickOrPlayFromThePanel_Detail": {
        "de": "Zuletzt, Favoriten und Private Server findest du gleich dort. Noch kein Spiel? Öffne die Roblox-Startseite oder füge eines in der Bibliothek hinzu.",
        "es": "Recientes, Favoritos y Servidores privados están ahí mismo. ¿Aún sin juego? Abre el inicio de Roblox o añade uno en la Biblioteca.",
        "fr": "Récents, Favoris et Serveurs privés sont juste là. Pas encore de jeu ? Ouvrez l'accueil de Roblox ou ajoutez-en un dans la Bibliothèque.",
        "pl": "Ostatnie, Ulubione i Prywatne serwery są tuż obok. Nie masz jeszcze gry? Otwórz stronę główną Roblox albo dodaj ją w Bibliotece.",
        "pt-BR": "Recentes, Favoritos e Servidores privados estão logo ali. Ainda sem jogo? Abra a página inicial do Roblox ou adicione um na Biblioteca.",
        "ru": "Недавние, Избранное и Приватные серверы находятся прямо там. Ещё нет игры? Откройте главную Roblox или добавьте игру в Библиотеку.",
    },
    "WelcomeWindow_WhenYouWantAlts": {
        "de": "Wenn du Alts willst, füge ein weiteres Konto hinzu.",
        "es": "Cuando quieras alts, añade otra cuenta.",
        "fr": "Quand vous voulez des alts, ajoutez un autre compte.",
        "pl": "Gdy potrzebujesz więcej kont, dodaj kolejne.",
        "pt-BR": "Quando quiser alts, adicione outra conta.",
        "ru": "Когда нужны альты, добавьте ещё один аккаунт.",
    },
    "WelcomeWindow_WhenYouWantAlts_Detail": {
        "de": "Ab zwei Konten erscheinen Mehrere starten und Squad Launch, und jedes Konto öffnet sein eigenes Roblox-Fenster.",
        "es": "Con dos o más aparecen Iniciar varias y Squad Launch, y cada cuenta abre su propia ventana de Roblox.",
        "fr": "À partir de deux, Lancer plusieurs et Squad Launch apparaissent, et chaque compte ouvre sa propre fenêtre Roblox.",
        "pl": "Przy dwóch lub więcej pojawiają się Uruchom wiele i Squad Launch, a każde konto otwiera własne okno Roblox.",
        "pt-BR": "Com duas ou mais, Iniciar vários e Squad Launch aparecem, e cada conta abre a própria janela do Roblox.",
        "ru": "При двух и более появляются «Запустить несколько» и Squad Launch, и каждый аккаунт открывает собственное окно Roblox.",
    },
    "WelcomeWindow_WithMoreThanOneAccount": {
        "de": "Mit mehr als einem Konto",
        "es": "Con más de una cuenta",
        "fr": "Avec plus d'un compte",
        "pl": "Przy więcej niż jednym koncie",
        "pt-BR": "Com mais de uma conta",
        "ru": "Если аккаунтов больше одного",
    },
    # ---- item 11 (b): #52 Diagnostics says which install channel this is ----------------------
    # "Microsoft Store" is a product noun and stays English everywhere. The snapshot.txt line is
    # English and invariant and does not use these.
    "Diag_Label_Channel": {
        "de": "Kanal", "es": "Canal", "fr": "Canal", "pl": "Kanał", "pt-BR": "Canal", "ru": "Канал",
    },
    "Diag_Channel_Store": {
        "de": "Microsoft Store", "es": "Microsoft Store", "fr": "Microsoft Store",
        "pl": "Microsoft Store", "pt-BR": "Microsoft Store", "ru": "Microsoft Store",
    },
    "Diag_Channel_Sideload": {
        "de": "Sideload", "es": "Instalación manual", "fr": "Installation manuelle",
        "pl": "Instalacja ręczna", "pt-BR": "Instalação manual", "ru": "Ручная установка",
    },
    "Diag_Channel_DirectDownload": {
        "de": "Direkter Download", "es": "Descarga directa", "fr": "Téléchargement direct",
        "pl": "Pobranie bezpośrednie", "pt-BR": "Download direto", "ru": "Прямая загрузка",
    },
    "Diag_Channel_Dev": {
        "de": "Entwickler-Build", "es": "Compilación de desarrollo", "fr": "Build de développement",
        "pl": "Kompilacja deweloperska", "pt-BR": "Build de desenvolvimento", "ru": "Сборка разработчика",
    },
    # ---- item 13: a SHIPPED label shortened so an expired row's actions fit 900 px (LabelWrapMeasurementTests) ----
    # Re-authenticate took Launch As's slot on an expired row; in these four languages it was still wider than the
    # slot it took, and Remove clipped by 9 (es) to 119 (ru) DIP. fr and pt-BR fit and keep their values.
    "MainWindow_Reauthenticate": {
        "de": "Neu anmelden",
        "es": "Reautenticar",
        "fr": "Se réauthentifier",
        "pl": "Zaloguj ponownie",
        "pt-BR": "Reautenticar",
        "ru": "Войти заново",
    },
}


# Translation rows whose NEUTRAL key a later item deletes belong here, with the reason recorded,
# rather than hand-deleted out of six JSON files. gen-culture-resx.py refuses to render a catalog
# carrying a key the neutral file does not have, and it is right to: a stale row reads as coverage.
STALE: dict[str, str] = {
    "WelcomeWindow_OpenGamesAndAddThe": "item 10: tour step 2 now points at the panel (PickOrPlayFromThePanel)",
    "WelcomeWindow_SearchByNameOrPaste": "item 10: tour step 2 now points at the panel (PickOrPlayFromThePanel)",
    "WelcomeWindow_ClickLaunchAsOrLaunch": "item 10: tour step 3 is now WhenYouWantAlts",
    "WelcomeWindow_MultiInstanceIsOnBy": "item 10: tour step 3 is now WhenYouWantAlts",
    "WelcomeWindow_RunMultipleRobloxClientsSide": "item 10: hero subtitle is now OnePlaceToLaunchRoblox",
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
