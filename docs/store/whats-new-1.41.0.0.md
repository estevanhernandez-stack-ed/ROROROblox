# What's new in this version — v1.41.0.0

> Paste the matching fenced block into Partner Center → your app → **Store listings** → *What's new
> in this version*: the English block for the en listing, and each translated block for its own
> language listing. (Since v1.28 the rows go in through the listing console instead; the blocks are the
> same either way.)
>
> **PREPARED 2026-11-26 on `cart/v141-item13`, NOT SHIPPED.** Drafted against the v1.41 cycle's spec
> (`docs/cycles/2026-11-26-roblox-feeds/spec.md`), not against a tagged build. The translations are
> hand-written from the English block and have not been through the listing verifier for this text
> (`submission-packet-1.41.0.0.md`, section 7 says what was and was not run). Re-check every bullet
> against the build that is tagged: the cycle is still being built.
>
> **Written for someone on the v1.40 Store install.** Single-version delta, five bullets. The update
> notice leads because it is the first thing a clan user sees, before a launch. The status line and
> the saved-game counts follow; the false-hold fix is its own bullet, worded as the log search allows
> (below); the status-service disclosure is the last bullet, the way v1.40's text disclosed the Store
> query in a plain clause.
>
> **The false-hold bullet says "could show", not "was showing".** The Neb log search (spec section 20)
> has not come back, so no hold line has been seen in a field log; the false hold is read from the code
> and the probe run on this machine. The sentence claims only what that supports.
>
> <!-- Neb search pending (spec §20): if the count is non-zero, change "could show" to "was showing". -->
>
> (The comment sits here and not inside the fenced English block because everything inside a fence is
> exported and pasted. Apply the change in all seven blocks: the past-tense verb in the Fixed bullet,
> "could show" in English, "pouvait" / "konnte" / "могло" / "podia" / "mogło" / "podía" in the six
> translations, becomes the "was showing" form of each.)
>
> **The quit-before-updating paragraph is v1.40's, plus one sentence.** The added sentence says what
> happens if you do not: an update that lands while RoRoRo is running can fail to install (README,
> "Quit RoRoRo before updating from the Store"). It is the dashboard task's "note the update-while-
> running failure" (`bxranna8z`).
>
> **What the bullets do not say.** The switch's label is not quoted: the bullets say "one switch in
> Settings", so a label change in the build cannot make this text wrong. No interval is quoted. No
> account is named anywhere, and the status line is not promised to appear, since it says nothing
> when Roblox reports nothing. The "What changed" link and the rollback case are in the release notes
> only.
>
> **Left out on purpose:** the compact-mode wording, the early status read after a failed launch, the
> private-server rule (a date, no count), every internal name.
>
> **Verb names.** "Launch multiple" is each language's catalog label for `MainWindow_LaunchMultiple`,
> in the quote style the v1.40 blocks use.
>
> Register follows each existing sheet: **vous** in French, **вы** in Russian, **du** in German,
> informal elsewhere. Product nouns stay English and undeclined: RoRoRo, Roblox, Microsoft Store,
> Status.io.
>
> Sources: `docs/cycles/2026-11-26-roblox-feeds/spec.md` (sections 4 to 11), `prd.md`,
> `docs/PRIVACY.md` (the `api.status.io` row).

---

## English

```
v1.41.0.0

Know what Roblox is doing before you launch

• New: RoRoRo tells you when Roblox has an update waiting, before
  you launch. The next launch installs it, and Launch multiple
  holds your other accounts until it is done. The notice clears
  by itself once the update is in.
• New: when Roblox reports a problem with its website, games,
  avatars or joining games, one line says so and links to
  Roblox's status page. When nothing is wrong, nothing shows.
• New: your saved games show how many people are playing right
  now and when each game last changed.
• Fixed: Launch multiple could show "Roblox is updating" and
  hold your other accounts when Roblox was already up to date.
• To read Roblox's status page, RoRoRo asks Status.io, a third
  party that hosts it, and sends nothing about you. One switch in
  Settings turns the status line and the player counts off.

Quit RoRoRo before updating from the Store: right-click the tray
icon and choose Quit. An update that lands while RoRoRo is
running can fail to install.

Nothing else moved. Accounts, themes, plugins, history and every
other setting carry over untouched.
```

## Deutsch

```
v1.41.0.0

Wissen, was Roblox gerade macht, bevor du startest

• Neu: RoRoRo sagt dir vor dem Start, wenn bei Roblox ein Update
  bereitliegt. Der nächste Start installiert es, und „Mehrere
  starten“ hält deine übrigen Konten zurück, bis es fertig ist.
  Der Hinweis verschwindet von selbst, sobald das Update drin ist.
• Neu: Meldet Roblox ein Problem mit seiner Website, Spielen,
  Avataren oder dem Spielbeitritt, sagt es eine Zeile und
  verlinkt auf Roblox' Statusseite. Ist alles in Ordnung, zeigt
  sich nichts.
• Neu: Deine gespeicherten Spiele zeigen, wie viele gerade
  spielen und wann sich das Spiel zuletzt geändert hat.
• Behoben: „Mehrere starten“ konnte „Roblox wird aktualisiert“
  anzeigen und deine übrigen Konten zurückhalten, obwohl Roblox
  schon aktuell war.
• Für die Statusseite fragt RoRoRo Status.io an, einen
  Drittanbieter, der sie hostet, und sendet nichts über dich.
  Ein Schalter in den Einstellungen schaltet die Statuszeile und
  die Spielerzahlen ab.

Beende RoRoRo, bevor du aus dem Store aktualisierst: Rechtsklick
auf das Symbol im Infobereich, dann „Beenden“. Ein Update, das
bei laufendem RoRoRo eintrifft, kann sich nicht installieren.

Sonst hat sich nichts geändert. Konten, Designs, Plugins,
Verlauf und alle anderen Einstellungen bleiben, wie sie sind.
```

## Français

```
v1.41.0.0

Sachez ce que fait Roblox avant de lancer

• Nouveau : RoRoRo vous prévient quand une mise à jour de Roblox
  vous attend, avant le lancement. Le prochain lancement
  l'installe, et « Lancer plusieurs » retient vos autres comptes
  jusqu'à la fin. L'avis disparaît tout seul une fois la mise à
  jour installée.
• Nouveau : quand Roblox signale un problème sur son site, ses
  jeux, ses avatars ou la connexion aux jeux, une ligne le dit
  et renvoie vers la page d'état de Roblox. Quand tout va bien,
  rien ne s'affiche.
• Nouveau : vos jeux enregistrés indiquent combien de personnes
  y jouent en ce moment et quand le jeu a changé pour la
  dernière fois.
• Corrigé : « Lancer plusieurs » pouvait afficher « Roblox se
  met à jour » et retenir vos autres comptes alors que Roblox
  était déjà à jour.
• Pour lire la page d'état de Roblox, RoRoRo interroge
  Status.io, un tiers qui l'héberge, sans rien envoyer à votre
  sujet. Un réglage dans Paramètres désactive la ligne d'état et
  les nombres de joueurs.

Quittez RoRoRo avant de mettre à jour depuis le Store : clic
droit sur l'icône de la zone de notification, puis « Quitter ».
Une mise à jour qui arrive pendant que RoRoRo tourne peut
échouer à s'installer.

Rien d'autre n'a changé. Comptes, thèmes, plugins, historique et
tous les autres réglages sont conservés tels quels.
```

## Русский

```
v1.41.0.0

Узнайте, что с Roblox, до запуска

• Новое: RoRoRo сообщает, что для Roblox ждёт обновление, ещё
  до запуска. Следующий запуск установит его, а «Запустить
  несколько» удержит остальные аккаунты, пока оно не
  завершится. Уведомление исчезает само, когда обновление
  установлено.
• Новое: когда Roblox сообщает о проблеме с сайтом, играми,
  аватарами или входом в игры, об этом говорит одна строка со
  ссылкой на страницу статуса Roblox. Если всё в порядке,
  ничего не показывается.
• Новое: сохранённые игры показывают, сколько людей играет
  прямо сейчас и когда игра обновлялась в последний раз.
• Исправлено: «Запустить несколько» могло показать «Roblox
  обновляется» и удержать остальные аккаунты, хотя Roblox уже
  был обновлён.
• Чтобы прочитать страницу статуса Roblox, RoRoRo обращается
  к Status.io, стороннему сервису, который её размещает, и
  ничего о вас не отправляет. Один переключатель в Настройках
  отключает строку статуса и счётчики игроков.

Выйдите из RoRoRo перед обновлением из Store: щёлкните правой
кнопкой по значку в трее и выберите «Выйти». Обновление,
пришедшее при работающем RoRoRo, может не установиться.

Больше ничего не изменилось. Аккаунты, темы, плагины, история и
все остальные настройки остаются как были.
```

## Português (Brasil)

```
v1.41.0.0

Saiba o que o Roblox está fazendo antes de iniciar

• Novo: o RoRoRo avisa quando o Roblox tem uma atualização
  esperando, antes de você iniciar. O próximo início a instala,
  e “Iniciar vários” segura as outras contas até terminar. O
  aviso some sozinho quando a atualização termina.
• Novo: quando o Roblox informa um problema no site, nos jogos,
  nos avatares ou na entrada em jogos, uma linha avisa e leva à
  página de status do Roblox. Quando não há problema, nada
  aparece.
• Novo: seus jogos salvos mostram quantas pessoas estão jogando
  agora e quando o jogo mudou pela última vez.
• Corrigido: “Iniciar vários” podia mostrar “O Roblox está
  atualizando” e segurar as outras contas mesmo com o Roblox já
  atualizado.
• Para ler a página de status do Roblox, o RoRoRo consulta o
  Status.io, um terceiro que a hospeda, e não envia nada sobre
  você. Um interruptor em Configurações desliga a linha de
  status e as contagens de jogadores.

Saia do RoRoRo antes de atualizar pela Store: clique com o
botão direito no ícone da bandeja e escolha “Sair”. Uma
atualização que chega com o RoRoRo aberto pode falhar ao
instalar.

Nada mais mudou. Contas, temas, plugins, histórico e todas as
outras configurações continuam como estavam.
```

## Polski

```
v1.41.0.0

Wiedz, co robi Roblox, zanim uruchomisz

• Nowość: RoRoRo mówi, że na Roblox czeka aktualizacja, jeszcze
  przed uruchomieniem. Następne uruchomienie ją zainstaluje, a
  „Uruchom wiele” wstrzyma pozostałe konta do jej zakończenia.
  Powiadomienie znika samo, gdy aktualizacja jest już
  zainstalowana.
• Nowość: gdy Roblox zgłasza problem ze swoją stroną, grami,
  awatarami lub dołączaniem do gier, mówi o tym jedna linia z
  odnośnikiem do strony statusu Roblox. Gdy wszystko gra,
  nic się nie pokazuje.
• Nowość: zapisane gry pokazują, ile osób gra teraz i kiedy
  gra ostatnio się zmieniła.
• Naprawiono: „Uruchom wiele” mogło pokazać „Roblox się
  aktualizuje” i wstrzymać pozostałe konta, choć Roblox był już
  aktualny.
• Żeby odczytać stronę statusu Roblox, RoRoRo pyta Status.io,
  podmiot trzeci, który ją hostuje, i nie wysyła nic o tobie.
  Jeden przełącznik w Ustawieniach wyłącza linię statusu i
  liczby graczy.

Zakończ RoRoRo przed aktualizacją ze Store: kliknij prawym
przyciskiem ikonę w zasobniku i wybierz „Zakończ”. Aktualizacja,
która nadejdzie przy uruchomionym RoRoRo, może się nie
zainstalować.

Nic więcej się nie zmieniło. Konta, motywy, pluginy, historia i
wszystkie inne ustawienia zostają bez zmian.
```

## Español

```
v1.41.0.0

Sabe qué hace Roblox antes de iniciar

• Nuevo: RoRoRo te avisa cuando Roblox tiene una actualización
  esperando, antes de iniciar. El próximo inicio la instalará, y
  «Iniciar varias» retiene las demás cuentas hasta que termine.
  El aviso desaparece solo cuando la actualización ya está.
• Nuevo: cuando Roblox informa de un problema con su web, sus
  juegos, los avatares o la entrada a juegos, una línea lo dice
  y enlaza a la página de estado de Roblox. Si todo va bien, no
  se muestra nada.
• Nuevo: tus juegos guardados muestran cuánta gente está
  jugando ahora y cuándo cambió el juego por última vez.
• Corregido: «Iniciar varias» podía mostrar «Roblox se está
  actualizando» y retener las demás cuentas aunque Roblox ya
  estuviera al día.
• Para leer la página de estado de Roblox, RoRoRo consulta a
  Status.io, un tercero que la aloja, y no envía nada sobre ti.
  Un interruptor en Ajustes apaga la línea de estado y los
  recuentos de jugadores.

Sal de RoRoRo antes de actualizar desde la Store: haz clic
derecho en el icono de la bandeja y elige «Salir». Una
actualización que llega con RoRoRo abierto puede fallar al
instalarse.

Nada más cambió. Cuentas, temas, plugins, historial y todos los
demás ajustes se mantienen intactos.
```
