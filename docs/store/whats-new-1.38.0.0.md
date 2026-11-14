# What's new in this version — v1.38.0.0

> Paste the matching fenced block into Partner Center → your app → **Store listings** → *What's new
> in this version*: the English block for the en listing, and each translated block for its own
> language listing. (Since v1.28 the rows go in through the listing console instead; the blocks are the
> same either way.)
>
> **Written for someone on the v1.37 Store install.** Single-version delta, three changes, all from
> the 2026-11-10 evidence sweep (`docs/cycles/2026-11-10-growth-and-quality/board.md`, items 4 and 5,
> and the clipped first-run row the new-user walk measured). The launch errors lead because a failed
> launch is the moment a new user decides whether the app works.
>
> **The crash notice is disclosed here on purpose.** It is a new line on screen that names an email
> address, and "nothing is sent unless you send it" is the sentence that keeps the no-telemetry
> promise readable.
>
> **Left out on purpose:** which exception or status code maps to which sentence, the layout rule
> that fixed the row, the measurement tests, and the regional-format probe. Nothing about plugin
> contracts.
>
> **The two closing paragraphs are v1.36's, unchanged, in every language.** They passed the listing
> verifier then and at v1.37; only the headline and the three bullets are new text.
>
> Register follows each existing sheet: **vous** in French, **вы** in Russian, **du** in German,
> informal elsewhere. Product nouns stay English: RoRoRo, Roblox, Microsoft Store. Quoted labels are
> the app's own in that language, checked against the catalogs on 2026-11-14: Diagnostics
> (`MainWindow_Diagnostics`), Remove (`MainWindow_Remove`), and the support bundle as the Diagnostics
> page's button names it (`DiagnosticsPage_SaveSupportBundleZip`).
>
> Sources: `docs/cycles/2026-11-10-growth-and-quality/board.md`, `docs/decisions.md` (entries dated
> 2026-11-10), PRs #276, #277, #278, #279.

---

## English

```
v1.38.0.0

Clearer when something goes wrong

• A launch that fails now tells you why, in your language: no
  internet, Roblox slow to answer, Roblox asking RoRoRo to slow
  down, or Roblox having trouble. Each one says what to do next.
• If RoRoRo closed unexpectedly last time, the next start says
  so and points you to Diagnostics, where you can save a support
  bundle and send it to us. Nothing is sent unless you send it.
• On a new account's row, Remove is no longer cut off in French,
  German, Russian, Portuguese or Spanish.

Quit RoRoRo before updating from the Store: right-click the tray
icon and choose Quit.

Nothing else moved. Accounts, themes, plugins, history and every
other setting carry over untouched.
```

## Deutsch

```
v1.38.0.0

Klarer, wenn etwas schiefgeht

• Schlägt ein Start fehl, sagt RoRoRo jetzt in deiner Sprache,
  warum: kein Internet, Roblox antwortet zu langsam, Roblox
  bittet RoRoRo, langsamer zu machen, oder Roblox hat gerade
  Probleme. Jede Meldung sagt, was du als Nächstes tun kannst.
• Wurde RoRoRo beim letzten Mal unerwartet beendet, sagt es das
  beim nächsten Start und zeigt dir den Weg zu „Diagnose“. Dort
  kannst du ein Support-Bündel speichern und uns schicken.
  Gesendet wird nichts, was du nicht selbst sendest.
• In der Zeile eines neuen Kontos wird „Entfernen“ auf
  Französisch, Deutsch, Russisch, Portugiesisch und Spanisch
  nicht mehr abgeschnitten.

Beende RoRoRo, bevor du aus dem Store aktualisierst: Rechtsklick
auf das Symbol im Infobereich, dann „Beenden“.

Sonst hat sich nichts geändert. Konten, Designs, Plugins,
Verlauf und alle anderen Einstellungen bleiben, wie sie sind.
```

## Français

```
v1.38.0.0

Plus clair quand quelque chose se passe mal

• Un lancement qui échoue vous dit désormais pourquoi, dans
  votre langue : pas de connexion internet, Roblox trop lent à
  répondre, Roblox qui demande à RoRoRo de ralentir, ou Roblox
  en difficulté. Chaque message indique quoi faire ensuite.
• Si RoRoRo s'est fermé de façon inattendue la dernière fois, le
  démarrage suivant vous le dit et vous dirige vers
  « Diagnostics », où vous pouvez enregistrer un lot de support
  et nous l'envoyer. Rien n'est envoyé si vous ne l'envoyez pas.
• Sur la ligne d'un nouveau compte, « Retirer » n'est plus coupé
  en français, allemand, russe, portugais ou espagnol.

Quittez RoRoRo avant de mettre à jour depuis le Store : clic
droit sur l'icône de la zone de notification, puis « Quitter ».

Rien d'autre n'a changé. Comptes, thèmes, plugins, historique et
tous les autres réglages sont conservés tels quels.
```

## Русский

```
v1.38.0.0

Понятнее, когда что-то идёт не так

• Если запуск не удался, RoRoRo теперь объясняет причину на
  вашем языке: нет интернета, Roblox слишком долго отвечает,
  Roblox просит RoRoRo сбавить темп или у Roblox сейчас
  неполадки. Каждое сообщение подсказывает, что делать дальше.
• Если в прошлый раз RoRoRo закрылся неожиданно, при следующем
  запуске он сообщит об этом и направит в раздел «Диагностика»,
  где можно сохранить пакет поддержки и отправить его нам.
  Ничего не отправляется, пока вы не отправите сами.
• В строке нового аккаунта кнопка «Удалить» больше не
  обрезается на французском, немецком, русском, португальском
  и испанском.

Выйдите из RoRoRo перед обновлением из Store: щёлкните правой
кнопкой по значку в трее и выберите «Выйти».

Больше ничего не изменилось. Аккаунты, темы, плагины, история и
все остальные настройки остаются как были.
```

## Português (Brasil)

```
v1.38.0.0

Mais claro quando algo dá errado

• Quando o RoRoRo não consegue iniciar uma conta, agora ele diz
  o motivo, no seu idioma: sem internet, o Roblox demorou para
  responder, o Roblox pediu para o RoRoRo ir mais devagar, ou o
  Roblox está com problemas. Cada mensagem diz o que fazer em
  seguida.
• Se o RoRoRo fechou de forma inesperada da última vez, o
  próximo início avisa e leva você a “Diagnóstico”, onde dá para
  salvar um pacote de suporte e enviar para nós. Nada é enviado
  se você não enviar.
• Na linha de uma conta nova, “Remover” não fica mais cortado em
  francês, alemão, russo, português e espanhol.

Saia do RoRoRo antes de atualizar pela Store: clique com o
botão direito no ícone da bandeja e escolha “Sair”.

Nada mais mudou. Contas, temas, plugins, histórico e todas as
outras configurações continuam como estavam.
```

## Polski

```
v1.38.0.0

Jaśniej, gdy coś pójdzie nie tak

• Gdy uruchomienie się nie uda, RoRoRo mówi teraz dlaczego, w
  twoim języku: brak internetu, Roblox odpowiada zbyt wolno,
  Roblox prosi RoRoRo, żeby zwolnił, albo Roblox ma teraz
  problemy. Każdy komunikat podpowiada, co zrobić dalej.
• Jeśli RoRoRo zamknął się ostatnio niespodziewanie, przy
  następnym uruchomieniu powie o tym i wskaże sekcję
  „Diagnostyka”, gdzie możesz zapisać pakiet pomocy i wysłać go
  do nas. Nic nie zostanie wysłane, jeśli tego nie wyślesz.
• W wierszu nowego konta przycisk „Usuń” nie jest już ucinany
  po francusku, niemiecku, rosyjsku, portugalsku i hiszpańsku.

Zakończ RoRoRo przed aktualizacją ze Store: kliknij prawym
przyciskiem ikonę w zasobniku i wybierz „Zakończ”.

Nic więcej się nie zmieniło. Konta, motywy, pluginy, historia i
wszystkie inne ustawienia zostają bez zmian.
```

## Español

```
v1.38.0.0

Más claro cuando algo sale mal

• Cuando un inicio falla, RoRoRo ahora te dice por qué, en tu
  idioma: sin internet, Roblox tarda demasiado en responder,
  Roblox le pide a RoRoRo que vaya más despacio, o Roblox tiene
  problemas. Cada mensaje te dice qué hacer después.
• Si RoRoRo se cerró de forma inesperada la última vez, el
  siguiente inicio te lo dice y te lleva a «Diagnóstico», donde
  puedes guardar un paquete de soporte y enviárnoslo. No se
  envía nada si no lo envías tú.
• En la fila de una cuenta nueva, «Quitar» ya no se corta en
  francés, alemán, ruso, portugués y español.

Sal de RoRoRo antes de actualizar desde la Store: haz clic
derecho en el icono de la bandeja y elige «Salir».

Nada más cambió. Cuentas, temas, plugins, historial y todos los
demás ajustes se mantienen intactos.
```
