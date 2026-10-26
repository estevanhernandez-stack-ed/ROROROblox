# What's new in this version — v1.35.0.0

> Paste the matching fenced block into Partner Center → your app → **Store listings** → *What's new
> in this version*: the English block for the en listing, and each translated block for its own
> language listing. (Since v1.28 the ten rows go in through the listing console instead; the blocks
> are the same either way.)
>
> **Written for someone on the v1.34 Store install.** Single-version delta. The plugin bullet leads
> because Store installs are the ones whose plugins stopped starting; the clan notes lead with
> launches because the farm hit that first.
>
> **Left out on purpose:** the plugin-author contract changes (0.13.0/0.14.0) and the named-server
> launch, which are for plugin authors and agents, not Store readers; the smaller logs, which no Store
> reader sees. The consent-line rewording is not mentioned: same permission, new words.
>
> **The restart line promises the request, not the result.** Windows restarts after a crash only when
> Windows Error Reporting is on, and nobody has watched a real crash or Store-update restart yet
> (release notes, Known issues). "RoRoRo asks Windows" is what is true.
>
> **UI names are the app's own translations**, read from the per-culture catalogs rather than
> re-translated: the Plugins page (`ShellWindow_Plugins` or the page's own heading key), its Restart
> button, and the leftover dialog's title `LeftoverProcessesWindow_LeftoverRobloxProcesses`. Each
> block quotes its own language's labels exactly as the app renders them, in that language's
> quotation marks.
>
> Register follows each existing sheet: **vous** in French, **вы** in Russian, **du** in German,
> informal elsewhere. Product nouns stay English: RoRoRo, Roblox, Microsoft Store.
>
> **Verifier:** see `whats-new-1.35.0.0.translations.json`, generated from this file. Rule 3's
> "in-app UI strings must stay in English" false positive is expected and overruled on the same ground
> as every release since v1.26: the interface is localized, and every quoted label above names its
> catalog key.
>
> **Verifier run, 2026-10-26, at a868e52, both shapes, 42 pairs.** 13 revise verdicts. 12 are the Rule 3
> false positive above (quoted labels: `ShellWindow_Plugins`, `PluginsPage_Restart`,
> `LeftoverProcessesWindow_LeftoverRobloxProcesses`), overruled. One was real and is fixed: Russian
> `whatsNew.plugins` read "при установке из Microsoft Store" (while installing from the Store, the
> act) instead of Store installs; it now reads "в версии из Microsoft Store". The verifier's own
> suggestion, "на установках", was not taken: correct in meaning, clumsy as Russian.
>
> Sources: `docs/store/release-notes-1.35.0.0.md`.

---

## English

```
v1.35.0.0

Plugins start again

• Plugins start on Microsoft Store installs again. They were
  stopping within a second of starting.
• Each plugin on the Plugins page says why it stopped, and
  "Restart" only appears when it can help.
• Every launch opens in its own Roblox window again, with its
  name, instead of being picked up by a leftover Roblox process
  in the tray.
• A brief flicker of the Windows display no longer closes RoRoRo.
• RoRoRo asks Windows to bring it back if it closes unexpectedly
  or the Store updates it. When it comes back with games open,
  they carry on without the "Leftover Roblox processes" dialog
  waiting for a click.

Nothing else moved. Accounts, themes, plugins, history and every
other setting carry over untouched.
```

## Deutsch

```
v1.35.0.0

Plugins starten wieder

• Plugins starten auf Microsoft-Store-Installationen wieder. Sie
  wurden innerhalb einer Sekunde nach dem Start beendet.
• Jedes Plugin auf der Seite „Plugins“ sagt, warum es gestoppt
  ist, und „Neu starten“ erscheint nur, wenn es helfen kann.
• Jeder Start öffnet sich wieder in einem eigenen
  Roblox-Fenster, mit seinem Namen, statt von einem übrig
  gebliebenen Roblox-Prozess im Infobereich übernommen zu
  werden.
• Ein kurzes Flackern der Windows-Anzeige schließt RoRoRo nicht
  mehr.
• RoRoRo bittet Windows, es wieder zu starten, wenn es
  unerwartet schließt oder der Store es aktualisiert. Kommt es
  mit offenen Spielen zurück, laufen diese weiter, ohne dass der
  Dialog „Übrig gebliebene Roblox-Prozesse“ auf einen Klick
  wartet.

Sonst hat sich nichts geändert. Konten, Designs, Plugins,
Verlauf und alle anderen Einstellungen bleiben, wie sie sind.
```

## Français

```
v1.35.0.0

Les plugins démarrent de nouveau

• Les plugins démarrent de nouveau sur les installations
  Microsoft Store. Ils s'arrêtaient moins d'une seconde après
  leur démarrage.
• Sur la page « Plugins », chaque plugin indique pourquoi il
  s'est arrêté, et « Redémarrer » n'apparaît que s'il peut
  servir.
• Chaque lancement s'ouvre de nouveau dans sa propre fenêtre
  Roblox, avec son nom, au lieu d'être repris par un processus
  Roblox résiduel dans la zone de notification.
• Un bref scintillement de l'affichage Windows ne ferme plus
  RoRoRo.
• RoRoRo demande à Windows de le relancer s'il se ferme de façon
  inattendue ou si le Store le met à jour. Quand il revient avec
  des jeux ouverts, ils continuent sans que la boîte de dialogue
  « Processus Roblox résiduels » attende un clic.

Rien d'autre n'a changé. Comptes, thèmes, plugins, historique et
tous les autres réglages sont conservés tels quels.
```

## Русский

```
v1.35.0.0

Плагины снова запускаются

• Плагины снова запускаются в версии из Microsoft Store.
  Раньше они останавливались в течение секунды после запуска.
• На странице «Плагины» каждый плагин сообщает, почему он
  остановился, а «Перезапустить» появляется, только когда это
  может помочь.
• Каждый запуск снова открывается в своём окне Roblox, с
  собственным именем, а не перехватывается оставшимся процессом
  Roblox в трее.
• Кратковременное мерцание экрана Windows больше не закрывает
  RoRoRo.
• RoRoRo просит Windows запустить его снова, если он неожиданно
  закрылся или его обновляет Store. Когда он возвращается при
  открытых играх, они продолжаются без того, чтобы диалог
  «Оставшиеся процессы Roblox» ждал щелчка.

Больше ничего не изменилось. Аккаунты, темы, плагины, история и
все остальные настройки остаются как были.
```

## Português (Brasil)

```
v1.35.0.0

Os plugins voltam a iniciar

• Os plugins voltam a iniciar nas instalações da Microsoft
  Store. Eles paravam menos de um segundo depois de iniciar.
• Cada plugin na página “Plugins” diz por que parou, e
  “Reiniciar” só aparece quando pode ajudar.
• Cada inicialização volta a abrir na própria janela do Roblox,
  com o nome dela, em vez de ser pega por um processo do Roblox
  que sobrou na bandeja.
• Uma oscilação rápida da tela do Windows não fecha mais o
  RoRoRo.
• O RoRoRo pede ao Windows para abri-lo de novo se ele fechar de
  forma inesperada ou a Store o atualizar. Quando volta com
  jogos abertos, eles continuam sem o diálogo “Processos do
  Roblox sobrando” esperando um clique.

Nada mais mudou. Contas, temas, plugins, histórico e todas as
outras configurações continuam como estavam.
```

## Polski

```
v1.35.0.0

Pluginy znów się uruchamiają

• Pluginy znów się uruchamiają w instalacjach z Microsoft Store.
  Zatrzymywały się w ciągu sekundy od startu.
• Na stronie „Wtyczki” każdy plugin podaje, dlaczego się
  zatrzymał, a „Uruchom ponownie” pojawia się tylko wtedy, gdy
  może pomóc.
• Każde uruchomienie znów otwiera się we własnym oknie Roblox, z
  własną nazwą, zamiast być przejmowane przez pozostały proces
  Roblox w zasobniku.
• Krótkie migotanie ekranu Windows nie zamyka już RoRoRo.
• RoRoRo prosi Windows, żeby uruchomił go ponownie, jeśli
  zamknie się nieoczekiwanie albo zaktualizuje go Store. Gdy
  wraca przy otwartych grach, działają one dalej, bez okna
  „Pozostałe procesy Roblox” czekającego na kliknięcie.

Nic więcej się nie zmieniło. Konta, motywy, pluginy, historia i
wszystkie inne ustawienia zostają bez zmian.
```

## Español

```
v1.35.0.0

Los plugins vuelven a arrancar

• Los plugins vuelven a arrancar en las instalaciones de
  Microsoft Store. Se detenían a menos de un segundo de empezar.
• En la página «Complementos», cada plugin indica por qué se
  detuvo, y «Reiniciar» solo aparece cuando puede ayudar.
• Cada inicio vuelve a abrirse en su propia ventana de Roblox,
  con su nombre, en lugar de que lo recoja un proceso de Roblox
  residual de la bandeja.
• Un parpadeo breve de la pantalla de Windows ya no cierra
  RoRoRo.
• RoRoRo le pide a Windows que lo vuelva a abrir si se cierra de
  forma inesperada o si la Store lo actualiza. Cuando vuelve con
  juegos abiertos, estos siguen sin que el diálogo «Procesos de
  Roblox residuales» espere un clic.

Nada más cambió. Cuentas, temas, plugins, historial y todos los
demás ajustes se mantienen intactos.
```
