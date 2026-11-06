# What's new in this version — v1.36.0.0

> Paste the matching fenced block into Partner Center → your app → **Store listings** → *What's new
> in this version*: the English block for the en listing, and each translated block for its own
> language listing. (Since v1.28 the rows go in through the listing console instead; the blocks are the
> same either way.)
>
> **Written for someone on the v1.35 Store install.** Single-version delta. The account manager leads
> because it changes what the main window shows the moment the update lands; the Library follows
> because "Games" is gone from the toolbar and the rail, and a reader has to be told where it went.
>
> **The quit line is the spec's (§7.7) and the README's.** An update that lands while RoRoRo is running
> can fail to install. It goes in every language.
>
> **Left out on purpose:** the input-credit fix for the idle clock (#221) and the cleanup of background
> Roblox clients (#222) are described in customer words, not mechanics; the new automated checks and
> fences are not mentioned at all. Nothing about plugin contracts.
>
> **UI names are the app's own translations**, read from the per-culture catalogs rather than
> re-translated: the Library (`ShellWindow_Games`), "Play as" (`Library_PlayAs`), and the three health
> words (`Shell_Health_Expired`, `Shell_Health_NeedsVerification`, `Shell_Health_Limited`). Each block
> quotes its own language's labels exactly as the app renders them.
>
> Register follows each existing sheet: **vous** in French, **вы** in Russian, **du** in German,
> informal elsewhere. Product nouns stay English: RoRoRo, Roblox, Robux, Plus, Microsoft Store.
>
> Sources: `docs/cycles/2026-11-01-account-library/` (spec, checklist), `docs/decisions.md`.

---

## English

```
v1.36.0.0

Your accounts, at a glance

• Each account shows its Robux, Plus and friends online, with a
  note you write yourself.
• Open an account to see its recent games, favourites and
  private servers, and play any of them in one click.
• When an account needs you, a badge says why: Session expired,
  Needs verification, or Limited by Roblox. One click starts the
  fix.
• Games is now the Library: every game you've played,
  favourited or saved, in a grid or a list. Search it, filter it,
  paste a game link, and choose who plays with "Play as".
• The welcome tour starts with one account; the tips for several
  accounts come after.
• Idle time now counts for the account you actually played on,
  even when you switch windows quickly.
• Background Roblox processes that never opened a window close
  on their own after two minutes.

Quit RoRoRo before updating from the Store: right-click the tray
icon and choose Quit.

Nothing else moved. Accounts, themes, plugins, history and every
other setting carry over untouched.
```

## Deutsch

```
v1.36.0.0

Deine Konten auf einen Blick

• Jedes Konto zeigt seine Robux, Plus und Freunde online, dazu
  eine Notiz, die du selbst schreibst.
• Öffne ein Konto, um seine zuletzt gespielten Spiele, Favoriten
  und privaten Server zu sehen, und spiele jedes davon mit einem
  Klick.
• Braucht ein Konto dich, sagt ein Badge, warum: „Sitzung
  abgelaufen“, „Verifizierung nötig“ oder „Von Roblox
  eingeschränkt“. Ein Klick startet die Lösung.
• „Spiele“ ist jetzt die „Bibliothek“: jedes Spiel, das du
  gespielt, favorisiert oder gespeichert hast, als Raster oder
  Liste. Durchsuche sie, filtere sie, füge einen Spiellink ein
  und wähle mit „Spielen als“, wer spielt.
• Die Willkommenstour beginnt mit einem Konto; die Tipps für
  mehrere Konten kommen danach.
• Die Inaktivitätszeit zählt jetzt für das Konto, auf dem du
  wirklich gespielt hast, auch wenn du schnell die Fenster
  wechselst.
• Roblox-Prozesse im Hintergrund, die nie ein Fenster geöffnet
  haben, schließen sich nach zwei Minuten von selbst.

Beende RoRoRo, bevor du aus dem Store aktualisierst: Rechtsklick
auf das Symbol im Infobereich, dann „Beenden“.

Sonst hat sich nichts geändert. Konten, Designs, Plugins,
Verlauf und alle anderen Einstellungen bleiben, wie sie sind.
```

## Français

```
v1.36.0.0

Vos comptes, d'un coup d'œil

• Chaque compte affiche ses Robux, Plus et amis en ligne, avec
  une note que vous écrivez vous-même.
• Ouvrez un compte pour voir ses jeux récents, ses favoris et
  ses serveurs privés, et jouez à n'importe lequel d'un clic.
• Quand un compte a besoin de vous, un badge dit pourquoi :
  « Session expirée », « Vérification requise » ou « Limité par
  Roblox ». Un clic lance la correction.
• « Jeux » devient la « Bibliothèque » : tous les jeux auxquels
  vous avez joué, mis en favori ou enregistrés, en grille ou en
  liste. Cherchez, filtrez, collez un lien de jeu, et choisissez
  qui joue grâce à « Jouer avec ».
• La visite guidée commence avec un seul compte ; les conseils
  pour plusieurs comptes viennent ensuite.
• Le temps d'inactivité compte désormais pour le compte sur
  lequel vous avez vraiment joué, même si vous changez vite de
  fenêtre.
• Les processus Roblox en arrière-plan qui n'ont jamais ouvert
  de fenêtre se ferment tout seuls au bout de deux minutes.

Quittez RoRoRo avant de mettre à jour depuis le Store : clic
droit sur l'icône de la zone de notification, puis « Quitter ».

Rien d'autre n'a changé. Comptes, thèmes, plugins, historique et
tous les autres réglages sont conservés tels quels.
```

## Русский

```
v1.36.0.0

Ваши аккаунты с одного взгляда

• Каждый аккаунт показывает свои Robux, Plus и друзей в сети,
  а также заметку, которую вы пишете сами.
• Откройте аккаунт, чтобы увидеть его недавние игры, избранное
  и приватные серверы, и запустите любую из них одним кликом.
• Когда аккаунту нужны вы, значок говорит почему: «Сессия
  истекла», «Нужна проверка» или «Ограничено Roblox». Один клик
  запускает исправление.
• «Игры» теперь «Библиотека»: все игры, в которые вы играли,
  добавили в избранное или сохранили, сеткой или списком.
  Ищите, фильтруйте, вставьте ссылку на игру и выберите, кто
  играет, через «Играть как».
• Приветственный тур начинается с одного аккаунта; советы для
  нескольких аккаунтов идут потом.
• Время простоя теперь засчитывается тому аккаунту, на котором
  вы действительно играли, даже если вы быстро переключаете
  окна.
• Фоновые процессы Roblox, так и не открывшие окно, сами
  закрываются через две минуты.

Выйдите из RoRoRo перед обновлением из Store: щёлкните правой
кнопкой по значку в трее и выберите «Выйти».

Больше ничего не изменилось. Аккаунты, темы, плагины, история и
все остальные настройки остаются как были.
```

## Português (Brasil)

```
v1.36.0.0

Suas contas à primeira vista

• Cada conta mostra seus Robux, Plus e amigos online, com uma
  nota que você mesmo escreve.
• Abra uma conta para ver os jogos recentes, favoritos e
  servidores privados dela, e jogue qualquer um com um clique.
• Quando uma conta precisa de você, um selo diz por quê:
  “Sessão expirada”, “Requer verificação” ou “Limitado pelo
  Roblox”. Um clique começa a correção.
• “Jogos” agora é a “Biblioteca”: todo jogo que você jogou,
  favoritou ou salvou, em grade ou lista. Pesquise, filtre,
  cole um link de jogo e escolha quem joga com “Jogar como”.
• O tour de boas-vindas começa com uma conta; as dicas para
  várias contas vêm depois.
• O tempo parado agora conta para a conta em que você realmente
  jogou, mesmo quando você troca de janela rápido.
• Processos do Roblox em segundo plano que nunca abriram uma
  janela fecham sozinhos depois de dois minutos.

Saia do RoRoRo antes de atualizar pela Store: clique com o
botão direito no ícone da bandeja e escolha “Sair”.

Nada mais mudou. Contas, temas, plugins, histórico e todas as
outras configurações continuam como estavam.
```

## Polski

```
v1.36.0.0

Twoje konta rzutem oka

• Każde konto pokazuje swoje Robux, Plus i znajomych online,
  razem z notatką, którą piszesz samodzielnie.
• Otwórz konto, żeby zobaczyć jego ostatnie gry, ulubione i
  prywatne serwery, i zagraj w dowolną jednym kliknięciem.
• Gdy konto cię potrzebuje, plakietka mówi dlaczego: „Sesja
  wygasła”, „Wymaga weryfikacji” albo „Ograniczone przez
  Roblox”. Jedno kliknięcie zaczyna naprawę.
• „Gry” to teraz „Biblioteka”: wszystkie gry, w które grasz,
  które masz w ulubionych albo zapisane, w siatce lub na liście.
  Przeszukuj, filtruj, wklej link do gry i wybierz, kto gra,
  przez „Graj jako”.
• Przewodnik powitalny zaczyna się od jednego konta; wskazówki
  dla kilku kont przychodzą potem.
• Czas bezczynności liczy się teraz dla konta, na którym
  naprawdę grasz, nawet gdy szybko przełączasz okna.
• Procesy Roblox w tle, które nigdy nie otworzyły okna, same
  się zamykają po dwóch minutach.

Zakończ RoRoRo przed aktualizacją ze Store: kliknij prawym
przyciskiem ikonę w zasobniku i wybierz „Zakończ”.

Nic więcej się nie zmieniło. Konta, motywy, pluginy, historia i
wszystkie inne ustawienia zostają bez zmian.
```

## Español

```
v1.36.0.0

Tus cuentas, de un vistazo

• Cada cuenta muestra sus Robux, Plus y amigos en línea, con
  una nota que escribes tú.
• Abre una cuenta para ver sus juegos recientes, favoritos y
  servidores privados, y juega a cualquiera con un clic.
• Cuando una cuenta te necesita, una insignia dice por qué:
  «Sesión caducada», «Requiere verificación» o «Limitado por
  Roblox». Un clic inicia el arreglo.
• «Juegos» ahora es la «Biblioteca»: todos los juegos que has
  jugado, marcado como favoritos o guardado, en cuadrícula o en
  lista. Búscalos, fíltralos, pega un enlace de juego y elige
  quién juega con «Jugar como».
• El recorrido de bienvenida empieza con una cuenta; los
  consejos para varias cuentas vienen después.
• El tiempo inactivo ahora cuenta para la cuenta con la que
  jugaste de verdad, aunque cambies de ventana rápido.
• Los procesos de Roblox en segundo plano que nunca abrieron
  una ventana se cierran solos a los dos minutos.

Sal de RoRoRo antes de actualizar desde la Store: haz clic
derecho en el icono de la bandeja y elige «Salir».

Nada más cambió. Cuentas, temas, plugins, historial y todos los
demás ajustes se mantienen intactos.
```
