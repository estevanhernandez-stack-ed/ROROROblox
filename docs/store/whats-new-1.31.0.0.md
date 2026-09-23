# What's new in this version — v1.31.0.0

> Paste the matching fenced block into Partner Center → your app → **Store listings** → *What's new
> in this version* — the English block for the en listing, and each translated block for its own
> language listing. All seven live in this file rather than seven side files, because the workflow
> is paste-per-listing and one file is what you actually work from. (Since v1.28 the ten rows go in
> through the listing console instead; the blocks are the same either way.)
>
> **Written for someone on the v1.30 Store install.** v1.30 certified and published on 2026-09-21,
> so this is a single-version delta. No catching-up to do.
>
> **The block warns about the notice before the reader meets it.** The freeze entry is marked
> serious and carries no Roblox version bound, so every Store user sees one notice the first time
> 1.31 fetches the list. A notice nobody was told about reads as a new problem; the second section
> says it is the feature working. Same sentence as the release notes.
>
> **The clan story is not here.** The notes open on "twice someone thought RoRoRo was broken and it
> was Roblox". A Store reader has no such history with the app, and on a public field it reads as an
> admission rather than a reason. The block states what the page does.
>
> **Every translated block carries an English-content caveat, and the English block does not** —
> the same deliberate, precedented deviation v1.29 and v1.30 made for alert sentences, both
> certified. The page chrome is translated; the entries are English. A Polish reader told about a
> new page who then finds English text has been misled unless we say so; an English reader does not
> need telling. The verifier's claim-fidelity rule will flag those six lines as having no English
> counterpart; that flag is expected. Reversing it is Este's call, not the rubric's.
>
> **UI names are the app's own translations**, read from `Strings.<culture>.resx` rather than
> re-translated: the page title (`MainWindow_KnownRobloxIssues`) and the Tools menu
> (`MainWindow_Tools_2`). The German block says Strg+7, because that is what the key says there.
>
> Product nouns stay English throughout: RoRoRo, Roblox.
>
> **Verifier run: NOT YET DONE.** The 1.30 path was hand-written translations → a
> `whats-new-<version>.translations.json` dataset → the translation verifier (both shapes) → fixes →
> a re-run, recorded here with its commit. The dataset for 1.31 is written
> (`whats-new-1.31.0.0.translations.json`, `sourceCommit` left as `PENDING` because nothing is
> committed yet); the run needs the dataset at a commit the verifier can reach. Six hand-written
> translations, and 1.30 found three defects in six — do not paste these unverified.
>
> Sources: `docs/store/release-notes-1.31.0.0.md`, the 2026-09-23 entry in `docs/decisions.md`,
> `known-issues.json`, PR #219.

---

## English

```
v1.31.0.0

Known Roblox issues
• A new page lists problems in Roblox itself: what happens,
  what to do, and the RoRoRo feature that helps, with a button
  that takes you there. Open it from the Tools menu, or press
  Ctrl+7 in the tools window.
• The first two: the Roblox window that freezes when you drag
  or resize it, and Roblox using more memory the longer it
  runs.

A notice for the serious ones
• A serious issue shows one notice at the top of the main
  window. Close it and it stays closed; only a new issue brings
  it back.
• Expect one the first time you open this version, about the
  freezing window. That is the feature working, not a new
  problem.

The list stays current on its own
• RoRoRo checks for a new list when it starts and every four
  hours, from the same place it already gets its Roblox
  compatibility settings. The list is signed, and nothing about
  you is sent.
```

## Français (fr)

```
v1.31.0.0

Problèmes Roblox connus
• Une nouvelle page liste les problèmes de Roblox lui-même :
  ce qui se passe, quoi faire, et la fonction de RoRoRo qui
  aide, avec un bouton qui vous y emmène. Ouvrez-la depuis le
  menu Outils, ou appuyez sur Ctrl+7 dans la fenêtre des
  outils.
• Les deux premiers : la fenêtre Roblox qui se fige quand vous
  la déplacez ou la redimensionnez, et Roblox qui consomme de
  plus en plus de mémoire au fil des heures.

Un avis pour les problèmes sérieux
• Un problème sérieux affiche un seul avis en haut de la
  fenêtre principale. Fermez-le et il reste fermé ; seul un
  nouveau problème le fait revenir.
• Attendez-vous à en voir un à la première ouverture de cette
  version, au sujet de la fenêtre qui se fige. C'est la
  fonction qui marche, pas un nouveau problème.

La liste se met à jour toute seule
• RoRoRo cherche une nouvelle liste au démarrage puis toutes
  les quatre heures, au même endroit que ses réglages de
  compatibilité Roblox. La liste est signée, et rien sur vous
  n'est envoyé.
• La page est dans votre langue ; les descriptions des
  problèmes sont en anglais pour l'instant.
```

## Deutsch (de)

```
v1.31.0.0

Bekannte Roblox-Probleme
• Eine neue Seite listet Probleme in Roblox selbst: was
  passiert, was du tun kannst und welche RoRoRo-Funktion
  hilft, mit einer Schaltfläche, die dich direkt hinführt.
  Öffne sie über das Menü Werkzeuge oder drück Strg+7 im
  Werkzeugfenster.
• Die ersten zwei: das Roblox-Fenster, das beim Verschieben
  oder bei einer Größenänderung einfriert, und Roblox, das mit
  der Zeit immer mehr Arbeitsspeicher braucht.

Ein Hinweis für die ernsten Fälle
• Ein ernstes Problem zeigt einen einzigen Hinweis oben im
  Hauptfenster. Schließ ihn, und er bleibt zu; nur ein neues
  Problem bringt ihn zurück.
• Beim ersten Öffnen dieser Version siehst du einen, zum
  einfrierenden Fenster. Das ist die Funktion bei der Arbeit,
  kein neues Problem.

Die Liste bleibt von selbst aktuell
• RoRoRo sucht beim Start und alle vier Stunden nach einer
  neuen Liste, am selben Ort, von dem es schon seine
  Roblox-Kompatibilitätseinstellungen holt. Die Liste ist
  signiert, und nichts über dich wird gesendet.
• Die Seite ist in deiner Sprache; die Problembeschreibungen
  sind vorerst auf Englisch.
```

## Русский (ru)

```
v1.31.0.0

Известные проблемы Roblox
• Новая страница перечисляет проблемы самого Roblox: что
  происходит, что делать и какая функция RoRoRo помогает, с
  кнопкой, которая ведёт прямо к ней. Откройте её из меню
  «Инструменты» или нажмите Ctrl+7 в окне инструментов.
• Первые две: окно Roblox, которое зависает при
  перетаскивании или изменении размера, и Roblox, который со
  временем занимает всё больше памяти.

Уведомление для серьёзных случаев
• Серьёзная проблема показывает одно уведомление вверху
  главного окна. Закройте его — и оно останется закрытым;
  вернуть его может только новая проблема.
• При первом запуске этой версии вы увидите одно такое
  уведомление — о зависающем окне. Это работает новая
  функция, а не новая проблема.

Список обновляется сам
• RoRoRo проверяет новый список при запуске и каждые четыре
  часа — там же, откуда уже получает настройки совместимости
  с Roblox. Список подписан, и никакие данные о вас не
  отправляются.
• Страница на вашем языке; описания проблем пока на
  английском.
```

## Português (Brasil) (pt-BR)

```
v1.31.0.0

Problemas conhecidos do Roblox
• Uma nova página lista problemas do próprio Roblox: o que
  acontece, o que fazer e o recurso do RoRoRo que ajuda, com
  um botão que leva você até ele. Abra pelo menu Ferramentas
  ou aperte Ctrl+7 na janela de ferramentas.
• Os dois primeiros: a janela do Roblox que trava quando você
  arrasta ou redimensiona, e o Roblox usando cada vez mais
  memória quanto mais tempo fica aberto.

Um aviso para os casos sérios
• Um problema sério mostra um único aviso no topo da janela
  principal. Feche e ele continua fechado; só um problema
  novo faz ele voltar.
• Na primeira vez que você abrir esta versão vai aparecer
  um, sobre a janela que trava. É o recurso funcionando, não
  um problema novo.

A lista se atualiza sozinha
• O RoRoRo procura uma lista nova ao iniciar e a cada quatro
  horas, no mesmo lugar de onde já pega as configurações de
  compatibilidade com o Roblox. A lista é assinada, e nada
  sobre você é enviado.
• A página está no seu idioma; as descrições dos problemas
  ainda estão em inglês.
```

## Polski (pl)

```
v1.31.0.0

Znane problemy Roblox
• Nowa strona wymienia problemy samego Roblox: co się dzieje,
  co zrobić i która funkcja RoRoRo pomaga, z przyciskiem,
  który do niej prowadzi. Otwórz ją z menu Narzędzia albo
  naciśnij Ctrl+7 w oknie narzędzi.
• Pierwsze dwa: okno Roblox, które zawiesza się przy
  przeciąganiu lub zmianie rozmiaru, oraz Roblox zużywający
  coraz więcej pamięci, im dłużej działa.

Powiadomienie o poważnych problemach
• Poważny problem pokazuje jedno powiadomienie u góry
  głównego okna. Zamknij je, a zostanie zamknięte; wróci
  tylko przy nowym problemie.
• Przy pierwszym otwarciu tej wersji zobaczysz jedno, o
  zawieszającym się oknie. To działa ta funkcja, a nie
  nowy problem.

Lista aktualizuje się sama
• RoRoRo sprawdza nową listę przy starcie i co cztery
  godziny, z tego samego miejsca, z którego już pobiera
  ustawienia zgodności z Roblox. Lista jest podpisana i nic
  o tobie nie jest wysyłane.
• Strona jest w twoim języku; opisy problemów są na razie
  po angielsku.
```

## Español (es)

```
v1.31.0.0

Problemas conocidos de Roblox
• Una página nueva enumera problemas del propio Roblox: qué
  pasa, qué hacer y la función de RoRoRo que ayuda, con un
  botón que te lleva a ella. Ábrela desde el menú
  Herramientas o pulsa Ctrl+7 en la ventana de herramientas.
• Los dos primeros: la ventana de Roblox que se congela al
  arrastrarla o cambiar su tamaño, y Roblox usando cada vez
  más memoria cuanto más tiempo lleva abierto.

Un aviso para los casos serios
• Un problema serio muestra un solo aviso arriba de la
  ventana principal. Ciérralo y se queda cerrado; solo un
  problema nuevo lo trae de vuelta.
• La primera vez que abras esta versión verás uno, sobre la
  ventana que se congela. Es la función haciendo su trabajo,
  no un problema nuevo.

La lista se mantiene al día sola
• RoRoRo busca una lista nueva al arrancar y cada cuatro
  horas, en el mismo sitio del que ya obtiene sus ajustes de
  compatibilidad con Roblox. La lista está firmada y no se
  envía nada sobre ti.
• La página está en tu idioma; las descripciones de los
  problemas están en inglés de momento.
```

## What is deliberately not in this copy

**The clan story.** See the preamble: it is the release notes' reason, not a Store reader's.

**The workarounds and the two feature buttons.** The frame-rate cap of 60, Recycle, and the memory
watchdog are the entries' own words, and the entries can change without an app update. A Store
field that repeats them would go stale the first time an entry is edited, with no release to fix it.

**The signature scheme, the four-hour timer's mechanics, the 256 KB limit, the cache files.** A
Store reader needs to know the list updates itself and sends nothing about them, not how.

**The notice stepping aside for the multi-instance warning.** True and in the notes; one sentence
too many for a public field where most readers never see that warning.

**The test counts and CI work.** Real, and invisible to a user.
