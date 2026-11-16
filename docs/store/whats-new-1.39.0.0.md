# What's new in this version — v1.39.0.0

> Paste the matching fenced block into Partner Center → your app → **Store listings** → *What's new
> in this version*: the English block for the en listing, and each translated block for its own
> language listing. (Since v1.28 the rows go in through the listing console instead; the blocks are the
> same either way.)
>
> **Written for someone on the v1.38 Store install.** Single-version delta, six fixes in five bullets,
> all from the 2026-11-16 quality and stability sweep
> (`docs/cycles/2026-11-16-quality-and-stability/board.md`, items 1, 2, 3, 4, 5 and 7). The backup
> leads because losing saved accounts is the one failure here a user cannot undo.
>
> **The backup is disclosed here on purpose.** It is a second file on the user's PC that holds
> session cookies, under the same Windows encryption as the first. `docs/PRIVACY.md` lists it.
>
> **What the fourth bullet does not say.** It says "another program", not "Task Manager". `taskkill`
> without `/F` was measured; Task Manager's End task was not, so the listing does not claim it.
>
> **Left out on purpose:** the flush, the rules for when the backup may be overwritten, the
> dispatcher, the `Superseded` flag, the message queue, and that the vault is no longer rewritten on
> every refresh. Nothing about plugin contracts.
>
> **The two closing paragraphs are v1.36's, unchanged, in every language.** They passed the listing
> verifier then, at v1.37 and at v1.38; only the headline and the five bullets are new text.
>
> Register follows each existing sheet: **vous** in French, **вы** in Russian, **du** in German,
> informal elsewhere. Product nouns stay English and undeclined: RoRoRo, Roblox, Microsoft Store.
> "Closed unexpectedly" uses each language's v1.38 wording, which is the app's own crash line. The
> tray is named as the v1.38 blocks name it.
>
> Sources: `docs/cycles/2026-11-16-quality-and-stability/board.md`, `docs/decisions.md` (entries dated
> 2026-11-16), `docs/cart-runs/2026-11-16-v139-stability/report.md`, PR #290.

---

## English

```
v1.39.0.0

Safer accounts, steadier RoRoRo

• RoRoRo now keeps a backup of your saved accounts. If the
  accounts file is ever damaged, by a power cut for example, it
  repairs itself from the backup.
• Quitting RoRoRo, or shutting down your PC with Roblox open, no
  longer makes the next start say RoRoRo closed unexpectedly.
• An account's row stays true when you launch an account that is
  already running, or when its sign-in expires in the middle of
  a game.
• When another program asks RoRoRo to close, it now closes
  instead of hiding. The X still sends it to the tray.
• The tray no longer warns about memory when no Roblox is
  running.

Quit RoRoRo before updating from the Store: right-click the tray
icon and choose Quit.

Nothing else moved. Accounts, themes, plugins, history and every
other setting carry over untouched.
```

## Deutsch

```
v1.39.0.0

Sicherere Konten, stabileres RoRoRo

• RoRoRo legt jetzt eine Sicherung deiner gespeicherten Konten
  an. Wird die Kontodatei einmal beschädigt, etwa durch einen
  Stromausfall, repariert RoRoRo sie aus der Sicherung.
• Wenn du RoRoRo beendest oder den PC herunterfährst, während
  Roblox offen ist, meldet der nächste Start nicht mehr, RoRoRo
  sei unerwartet beendet worden.
• Die Zeile eines Kontos bleibt richtig, wenn du ein Konto
  startest, das schon läuft, oder wenn seine Anmeldung mitten im
  Spiel abläuft.
• Bittet ein anderes Programm RoRoRo, sich zu schließen, dann
  schließt es sich jetzt, statt sich zu verstecken. Das X
  schickt es weiterhin in den Infobereich.
• Das Symbol im Infobereich warnt nicht mehr vor knappem
  Arbeitsspeicher, wenn kein Roblox läuft.

Beende RoRoRo, bevor du aus dem Store aktualisierst: Rechtsklick
auf das Symbol im Infobereich, dann „Beenden“.

Sonst hat sich nichts geändert. Konten, Designs, Plugins,
Verlauf und alle anderen Einstellungen bleiben, wie sie sind.
```

## Français

```
v1.39.0.0

Des comptes plus sûrs, un RoRoRo plus stable

• RoRoRo conserve désormais une copie de sauvegarde de vos
  comptes enregistrés. Si le fichier des comptes est un jour
  endommagé, par une coupure de courant par exemple, il se
  répare à partir de cette copie.
• Quitter RoRoRo, ou éteindre votre PC avec Roblox ouvert, ne
  fait plus dire au démarrage suivant que RoRoRo s'est fermé de
  façon inattendue.
• La ligne d'un compte reste exacte quand vous lancez un compte
  déjà en cours d'exécution, ou quand sa connexion expire en
  pleine partie.
• Quand un autre programme demande à RoRoRo de se fermer, il se
  ferme désormais au lieu de se masquer. Le X l'envoie toujours
  dans la zone de notification.
• L'icône de la zone de notification ne signale plus un manque
  de mémoire quand aucun Roblox n'est ouvert.

Quittez RoRoRo avant de mettre à jour depuis le Store : clic
droit sur l'icône de la zone de notification, puis « Quitter ».

Rien d'autre n'a changé. Comptes, thèmes, plugins, historique et
tous les autres réglages sont conservés tels quels.
```

## Русский

```
v1.39.0.0

Аккаунты под защитой, RoRoRo стабильнее

• RoRoRo теперь хранит резервную копию сохранённых аккаунтов.
  Если файл аккаунтов повредится, например из-за отключения
  электричества, RoRoRo восстановит его из копии.
• Если выйти из RoRoRo или выключить компьютер с открытым
  Roblox, следующий запуск больше не сообщает, что RoRoRo
  закрылся неожиданно.
• Строка аккаунта остаётся верной, когда вы запускаете уже
  работающий аккаунт или когда его вход истекает прямо во время
  игры.
• Когда другая программа просит RoRoRo закрыться, он теперь
  закрывается, а не прячется. Крестик по-прежнему отправляет
  его в трей.
• Значок в трее больше не предупреждает о нехватке памяти,
  когда Roblox не запущен.

Выйдите из RoRoRo перед обновлением из Store: щёлкните правой
кнопкой по значку в трее и выберите «Выйти».

Больше ничего не изменилось. Аккаунты, темы, плагины, история и
все остальные настройки остаются как были.
```

## Português (Brasil)

```
v1.39.0.0

Contas mais seguras, RoRoRo mais estável

• O RoRoRo agora guarda uma cópia de segurança das suas contas
  salvas. Se o arquivo de contas for danificado, por uma queda
  de energia, por exemplo, ele se recupera a partir da cópia.
• Sair do RoRoRo, ou desligar o PC com o Roblox aberto, não faz
  mais o próximo início dizer que o RoRoRo fechou de forma
  inesperada.
• A linha de uma conta continua correta quando você inicia uma
  conta que já está em execução, ou quando o login dela expira
  no meio de um jogo.
• Quando outro programa pede para o RoRoRo fechar, agora ele
  fecha em vez de se esconder. O X continua mandando o RoRoRo
  para a bandeja.
• O ícone da bandeja não avisa mais sobre falta de memória
  quando nenhum Roblox está aberto.

Saia do RoRoRo antes de atualizar pela Store: clique com o
botão direito no ícone da bandeja e escolha “Sair”.

Nada mais mudou. Contas, temas, plugins, histórico e todas as
outras configurações continuam como estavam.
```

## Polski

```
v1.39.0.0

Bezpieczniejsze konta, stabilniejszy RoRoRo

• RoRoRo przechowuje teraz kopię zapasową zapisanych kont. Jeśli
  plik kont zostanie uszkodzony, na przykład przez przerwę w
  zasilaniu, RoRoRo naprawi go z kopii.
• Gdy zakończysz RoRoRo albo wyłączysz komputer, a Roblox był
  otwarty, następne uruchomienie nie mówi już, że RoRoRo
  zamknął się niespodziewanie.
• Wiersz konta pozostaje zgodny z prawdą, gdy uruchamiasz konto,
  które już działa, albo gdy jego logowanie wygaśnie w trakcie
  gry.
• Gdy inny program poprosi RoRoRo o zamknięcie, RoRoRo teraz się
  zamyka, zamiast się ukrywać. Przycisk X nadal wysyła go do
  zasobnika.
• Ikona w zasobniku nie ostrzega już o braku pamięci, gdy żaden
  Roblox nie jest uruchomiony.

Zakończ RoRoRo przed aktualizacją ze Store: kliknij prawym
przyciskiem ikonę w zasobniku i wybierz „Zakończ”.

Nic więcej się nie zmieniło. Konta, motywy, pluginy, historia i
wszystkie inne ustawienia zostają bez zmian.
```

## Español

```
v1.39.0.0

Cuentas más seguras, RoRoRo más estable

• RoRoRo ahora guarda una copia de seguridad de tus cuentas
  guardadas. Si el archivo de cuentas se daña alguna vez, por un
  corte de luz por ejemplo, se repara a partir de la copia.
• Salir de RoRoRo, o apagar tu PC con Roblox abierto, ya no hace
  que el siguiente inicio diga que RoRoRo se cerró de forma
  inesperada.
• La fila de una cuenta sigue siendo correcta cuando inicias una
  cuenta que ya está en marcha, o cuando su inicio de sesión
  caduca en mitad de una partida.
• Cuando otro programa le pide a RoRoRo que se cierre, ahora se
  cierra en lugar de ocultarse. La X lo sigue enviando a la
  bandeja.
• El icono de la bandeja ya no avisa de falta de memoria cuando
  no hay ningún Roblox abierto.

Sal de RoRoRo antes de actualizar desde la Store: haz clic
derecho en el icono de la bandeja y elige «Salir».

Nada más cambió. Cuentas, temas, plugins, historial y todos los
demás ajustes se mantienen intactos.
```
