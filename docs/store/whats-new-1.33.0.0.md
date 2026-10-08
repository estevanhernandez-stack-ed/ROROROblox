# What's new in this version — v1.33.0.0

> Paste the matching fenced block into Partner Center → your app → **Store listings** → *What's new
> in this version*: the English block for the en listing, and each translated block for its own
> language listing. (Since v1.28 the ten rows go in through the listing console instead; the blocks
> are the same either way.)
>
> **Written for someone on the v1.32.1 Store install.** Single-version delta, but a wide one: this
> is the alerts cycle, twelve built items.
>
> **One deliberate departure from v1.32.1's house rule.** That release's field said what a setting
> does now and never that it had been wrong, because the defect was a mechanism (read once at
> startup) that a Store field has no business carrying. Here the first bullet does say memory
> warnings used to ignore the routing grid, because that is an **outcome** a reader needs: without
> it, "alerts obey the grid" reads as a restatement of what the listing already promised, and the
> person whose unticked Desktop box never worked is told nothing. The mechanism — three separate
> code paths to the screen — still stays out. The clan-facing notes carry it.
>
> **UI names are the app's own translations**, read from `Strings.<culture>.resx` rather than
> re-translated: `SettingsPage_Desktop`, `SettingsPage_HowOftenAtMost`, `SettingsPage_EveryTime`,
> `SettingsPage_AlertSound`, `SettingsPage_HearIt`, `SettingsPage_AnAccountGoesIdle` and
> `SettingsPage_Appearance`. Each block quotes its own language's label exactly as the app renders
> it. The three sound choices are **described rather than quoted** — their option names are built by
> a converter rather than held as resx values, so quoting them would mean re-translating, which is
> the one thing this file does not do. The word for silence is taken from each language's
> `SettingsPage_AlertSoundHint`, where it already appears in quotes.
>
> Register follows each existing sheet: **vous** in French, **вы** in Russian, **du** in German,
> informal elsewhere. Product nouns stay English: RoRoRo, Roblox, Recycle.
>
> **Verifier run: NOT YET RUN.** Stated rather than assumed. If it repeats the rule-3 false positive
> it drew twelve times on v1.32 and three times on v1.32.1 — "in-app UI strings must remain
> untranslated in English", a rule that assumes an English-only interface this app has not had since
> v1.26 — it is overruled on the same ground, and that is why every quoted label above names its
> resx key: the overrule is checkable. Filed upstream as
> [translation-verification#27](https://github.com/estevanhernandez-stack-ed/translation-verification/issues/27).
>
> Sources: `docs/store/release-notes-1.33.0.0.md`, `docs/smoke-2026-10-07-alerts-cycle.md`.

---

## English

```
v1.33.0.0

Alerts go where you send them

• Untick Desktop for an alert and nothing pops up for it.
  Memory warnings used to appear on your desktop whatever you
  picked; now every alert obeys the grid, the per-account mute
  and the quiet period alike.
• "How often at most" is yours. One setting for every alert, an
  override for any single kind, and "Every time" holds nothing
  back.
• The desktop alert is RoRoRo's own window now, themed like the
  rest of the app. When it names one account, click it to jump
  to that account's row.
• "Alert sound" offers the RoRoRo chime, the Windows default,
  or silence, and "Hear it" plays your pick before you commit
  to it.
• "An account goes idle in a game" now means exactly that. An
  account parked at the Roblox home screen no longer gets an
  idle alert — it has nothing to be kicked out of.
• The three alert cards are one section: one row per alert, with
  where it goes and how often side by side. Settings calls its
  page "Language and Appearance", which is where you change the
  language.

Nothing else moved. Accounts, themes, plugins, history and every
other setting carry over untouched.
```

## Deutsch

```
v1.33.0.0

Benachrichtigungen gehen nur dorthin, wohin du sie schickst

• Nimm bei einer Benachrichtigung das Häkchen bei „Desktop“
  weg, und es erscheint nichts mehr dafür. RAM-Warnungen kamen
  bisher auf den Desktop, egal was du gewählt hattest; jetzt
  hält sich jede Benachrichtigung an die Tabelle, an die
  Stummschaltung pro Konto und an die Ruhezeit.
• „Wie oft höchstens“ gehört dir. Eine Einstellung für alle,
  eine Ausnahme für jede einzelne Art, und „Jedes Mal“ hält
  nichts zurück.
• Die Desktop-Benachrichtigung ist jetzt RoRoRos eigenes
  Fenster, im Design der übrigen App. Nennt sie genau ein
  Konto, bringt dich ein Klick direkt zu dessen Zeile.
• „Benachrichtigungston“ bietet den RoRoRo-Ton, den
  Windows-Standard oder Stumm — und „Anhören“ spielt deine Wahl
  vor, bevor du dich festlegst.
• „Ein Konto wird in einem Spiel inaktiv“ heißt jetzt genau
  das. Ein Konto, das auf der Roblox-Startseite parkt, bekommt
  keine Inaktivitäts-Warnung mehr; es kann dort nicht
  hinausgeworfen werden.
• Aus drei Karten wird ein Abschnitt: eine Zeile pro
  Benachrichtigung, Ziel und Häufigkeit nebeneinander. Die
  Seite heißt „Sprache und Darstellung“, denn dort stellst du
  die Sprache um.

Sonst hat sich nichts geändert. Konten, Themes, Plugins,
Verlauf und alle übrigen Einstellungen bleiben unverändert.
```

## Français

```
v1.33.0.0

Les alertes ne partent que là où vous les envoyez

• Décochez « Bureau » pour une alerte et plus rien n'apparaît
  pour elle. Les alertes mémoire s'affichaient sur le bureau
  quoi que vous ayez choisi ; désormais chaque alerte respecte
  le tableau, la mise en sourdine par compte et le délai de
  silence.
• « À quelle fréquence au maximum » vous appartient. Un réglage
  pour toutes, une exception pour n'importe quelle catégorie,
  et « Chaque fois » ne retient rien.
• L'alerte bureau est maintenant une fenêtre dessinée par
  RoRoRo, aux couleurs du reste de l'application. Quand elle ne
  nomme qu'un compte, un clic vous amène à sa ligne.
• « Son d'alerte » propose le son RoRoRo, le son par défaut de
  Windows ou « Silencieux », et « Écouter » joue votre choix
  avant que vous ne le validiez.
• « Un compte devient inactif dans un jeu » veut dire
  exactement cela. Un compte resté sur la page d'accueil Roblox
  ne reçoit plus d'alerte d'inactivité : il ne risque pas d'en
  être expulsé.
• Les trois cartes deviennent une seule section : une ligne par
  alerte, la destination et la fréquence côte à côte. La page
  s'appelle « Langue et apparence », car c'est là que vous
  changez de langue.

Rien d'autre n'a bougé. Comptes, thèmes, plugins, historique et
tous les autres réglages sont conservés tels quels.
```

## Русский

```
v1.33.0.0

Оповещения уходят только туда, куда вы их отправили

• Снимите «Рабочий стол» у какого-то оповещения — и для него
  больше ничего не всплывает. Предупреждения о памяти
  появлялись на рабочем столе, что бы вы ни выбрали; теперь
  каждое оповещение подчиняется таблице, отключению звука по
  аккаунту и паузе между повторами.
• «Как часто не более» теперь ваше. Одна настройка для всех,
  исключение для любого отдельного вида, а «Каждый раз» не
  задерживает ничего.
• Оповещение на рабочем столе — собственное окно RoRoRo,
  оформленное как остальное приложение. Если названо ровно
  один аккаунт, щелчок приводит вас прямо к его строке.
• «Звук оповещения» предлагает сигнал RoRoRo, стандартный звук
  Windows или «Без звука», а «Прослушать» проигрывает ваш выбор
  до того, как вы его закрепите.
• «Аккаунт простаивает в игре» теперь значит именно это.
  Аккаунт, стоящий на домашней странице Roblox, больше не
  получает оповещения о простое — оттуда его некуда выкинуть.
• Три карточки стали одним разделом: по строке на оповещение,
  адресат и частота рядом. Страница называется «Язык и
  оформление», ведь именно там меняется язык.

Больше ничего не изменилось. Аккаунты, темы, плагины, история
и все остальные настройки сохраняются без изменений.
```

## Português (Brasil)

```
v1.33.0.0

Os alertas vão só para onde você mandar

• Desmarque "Área de trabalho" em um alerta e nada mais aparece
  por causa dele. Os avisos de memória apareciam na área de
  trabalho independentemente do que você escolhesse; agora todo
  alerta obedece à tabela, ao silenciamento por conta e ao
  intervalo de silêncio.
• "Com que frequência no máximo" é sua. Um ajuste para todos,
  uma exceção para qualquer tipo isolado, e "Sempre" não segura
  nada.
• O alerta na área de trabalho agora é uma janela do próprio
  RoRoRo, no tema do resto do app. Quando ele nomeia uma única
  conta, um clique leva você à linha dela.
• "Som do alerta" oferece o som do RoRoRo, o som padrão do
  Windows ou "Silencioso", e "Ouvir" toca a sua escolha antes
  de você confirmar.
• "Uma conta fica inativa em um jogo" agora quer dizer
  exatamente isso. Uma conta parada na tela inicial do Roblox
  não recebe mais alerta de inatividade — não há de onde
  expulsá-la.
• Os três cartões viraram uma seção: uma linha por alerta, com
  o destino e a frequência lado a lado. A página se chama
  "Idioma e aparência", que é onde você troca o idioma.

Nada mais mudou. Contas, temas, plugins, histórico e todos os
outros ajustes continuam iguais.
```

## Polski

```
v1.33.0.0

Alerty trafiają tylko tam, gdzie je wyślesz

• Odznacz „Pulpit” przy jakimś alercie i nic się dla niego nie
  pojawia. Ostrzeżenia o pamięci wyskakiwały na pulpicie
  niezależnie od tego, co wybrałeś; teraz każdy alert trzyma
  się tabeli, wyciszenia dla konta i przerwy między
  powtórzeniami.
• „Jak często najwyżej” należy do ciebie. Jedno ustawienie dla
  wszystkich, wyjątek dla dowolnego rodzaju, a „Za każdym
  razem” nie wstrzymuje niczego.
• Alert na pulpicie to teraz własne okno RoRoRo, w motywie
  reszty aplikacji. Gdy wskazuje jedno konto, kliknięcie
  przenosi cię do jego wiersza.
• „Dźwięk alertu” daje do wyboru sygnał RoRoRo, domyślny dźwięk
  Windows albo „Cisza”, a „Posłuchaj” odtwarza twój wybór,
  zanim go zatwierdzisz.
• „Konto staje się bezczynne w grze” znaczy teraz dokładnie to.
  Konto stojące na stronie głównej Roblox nie dostaje już
  alertu o bezczynności — nie ma skąd go wyrzucić.
• Trzy karty stały się jedną sekcją: wiersz na alert, miejsce
  docelowe i częstotliwość obok siebie. Strona nazywa się
  „Język i wygląd”, bo właśnie tam zmieniasz język.

Nic poza tym się nie zmieniło. Konta, motywy, wtyczki, historia
i wszystkie pozostałe ustawienia pozostają bez zmian.
```

## Español

```
v1.33.0.0

Las alertas van solo a donde tú las mandes

• Desmarca "Escritorio" en una alerta y ya no aparece nada por
  ella. Los avisos de memoria salían en el escritorio
  eligieras lo que eligieras; ahora cada alerta respeta la
  tabla, el silencio por cuenta y el intervalo de silencio.
• "Con qué frecuencia como máximo" es tuya. Un ajuste para
  todas, una excepción para cualquier tipo concreto, y "Cada
  vez" no retiene nada.
• La alerta de escritorio es ahora una ventana del propio
  RoRoRo, con el tema del resto de la aplicación. Cuando nombra
  una sola cuenta, un clic te lleva a su fila.
• "Sonido de alerta" ofrece el sonido de RoRoRo, el de Windows
  por defecto o "Silencio", y "Escuchar" reproduce tu elección
  antes de que la confirmes.
• "Una cuenta queda inactiva en un juego" quiere decir
  exactamente eso. Una cuenta aparcada en la pantalla de inicio
  de Roblox ya no recibe aviso de inactividad: de ahí no hay
  nada que la expulse.
• Las tres tarjetas son una sección: una fila por alerta, con
  su destino y su frecuencia al lado. La página se llama
  "Idioma y apariencia", que es donde cambias el idioma.

No ha cambiado nada más. Cuentas, temas, plugins, historial y
todos los demás ajustes se mantienen igual.
```
