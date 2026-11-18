# What's new in this version — v1.40.0.0

> Paste the matching fenced block into Partner Center → your app → **Store listings** → *What's new
> in this version*: the English block for the en listing, and each translated block for its own
> language listing. (Since v1.28 the rows go in through the listing console instead; the blocks are the
> same either way.)
>
> **SHIPPED 2026-11-18.** These seven blocks are the What's new text live on the Store, written by
> the listing console on 2026-11-17 and read back. Drafted 2026-11-17 on `release/1.40.0.0`; this
> banner said "PREPARED, NOT SHIPPED" until the release published.
>
> **Written for someone on the v1.39 Store install.** Single-version delta, five bullets. The update
> button leads because it is the one new thing a user can see and use. The damaged-file and Start Fresh
> items are grouped into one bullet. The smallest items (a plugin stopping a client that already
> exited, an account list left empty by a read failure saying why) are in the release notes only.
>
> **The first bullet is worded for a person updating TO this version.** They did not have the button,
> so it did not tell them about this update. The bullet says it tells you about the *next* one. It also
> discloses the new network call in a plain clause, the way v1.39's text disclosed the backup file:
> RoRoRo asks the Microsoft Store through Windows, after startup and every six hours, and sends nothing
> of its own (`docs/PRIVACY.md`, hosts table).
>
> **What the first bullet does not say.** The Store check has not been exercised on a Store-installed
> build; the bullet says what the button does and does not say it was verified. For the direct
> download it says "quit RoRoRo and open it again", never "installs when it closes": whether a staged
> update applies at exit or at the next start is unmeasured.
>
> **What the third bullet does not say.** "Another program", not "Task Manager": `taskkill` without
> `/F` was measured, End task was not by this cycle's runs. No seconds are quoted for the faster exit.
>
> **What the fourth bullet does not say.** Nothing about how the alert is delivered; it goes where the
> user already routes dropped-out alerts. The launch-failure row and the expired-account alert are
> test-verified, not live-verified, and the text claims no more than "now does X".
>
> **Start Fresh** is named exactly as each language's catalog labels the button
> (`DpapiCorruptWindow_StartFresh`), as are the two update buttons (`MainWindow_UpdateAvailable`,
> `MainWindow_UpdateQuitToUpdate`) and the About page title (`Shell_Title_About`). The text does not
> describe a healthy file having been set aside for anyone; it says the button now checks first.
>
> **Left out on purpose:** the two set-aside file names (they are in the release notes, the reviewer
> letter and `docs/PRIVACY.md`), the six-hour mechanics beyond the one clause, compact mode, History's
> 24-hour times, the plugin stop, the log lines, every internal name.
>
> **The two closing paragraphs are v1.36's, unchanged, in every language** (checked by script against
> the v1.39 blocks). They passed the listing verifier at v1.36 through v1.39; only the headline and
> the five bullets are new text. They went through the listing verifier on 2026-11-17 (packet,
> section 7): only Rule 3 findings, overruled by the owner as at v1.36 to v1.39.
>
> Register follows each existing sheet: **vous** in French, **вы** in Russian, **du** in German,
> informal elsewhere. Product nouns stay English and undeclined: RoRoRo, Roblox, Microsoft Store.
>
> Sources: `docs/cart-runs/2026-11-17-stability-part-two/report.md`,
> `docs/cart-runs/2026-11-17-update-button/report.md`,
> `docs/cart-runs/2026-11-16-data-folder-override/report.md`, `docs/decisions.md` (entries dated
> 2026-11-16 and 2026-11-17), `docs/PRIVACY.md`.

---

## English

```
v1.40.0.0

An update button, and a steadier RoRoRo

• New: an Update available button in the main window. When a
  newer RoRoRo is out it appears, and one click opens RoRoRo's
  page in the Microsoft Store. To know, RoRoRo asks the Store
  through Windows after it starts and every six hours, and sends
  nothing of its own. It starts working with the update after
  this one.
• A damaged saved-games, private-servers, history or stats file
  no longer stops RoRoRo from starting: what can be read is
  kept. A damaged accounts file is restored from its backup and
  is never read as empty. Start Fresh now checks again first and
  only sets the file aside, never deletes it.
• RoRoRo quits faster, and another program asking it to close
  now works during startup too.
• An alt whose sign-in expired now raises an alert when it drops
  out. A launch that fails shows on its row.
• The About page's shortcut list, and dates across RoRoRo,
  follow your language.

Quit RoRoRo before updating from the Store: right-click the tray
icon and choose Quit.

Nothing else moved. Accounts, themes, plugins, history and every
other setting carry over untouched.
```

## Deutsch

```
v1.40.0.0

Eine Update-Schaltfläche und ein stabileres RoRoRo

• Neu: die Schaltfläche „Update verfügbar“ im Hauptfenster. Sie
  erscheint, sobald ein neueres RoRoRo bereitsteht, und ein
  Klick öffnet die Seite von RoRoRo im Microsoft Store. Dafür
  fragt RoRoRo den Store über Windows, nach dem Start und dann
  alle sechs Stunden, und sendet dabei nichts Eigenes. Sie
  funktioniert ab dem Update nach diesem.
• Eine beschädigte Datei mit gespeicherten Spielen, privaten
  Servern, Verlauf oder Statistik hält RoRoRo nicht mehr vom
  Start ab: Lesbares bleibt erhalten. Eine beschädigte
  Kontodatei wird aus der Sicherung wiederhergestellt und nie
  als leer gelesen. „Neu beginnen“ prüft jetzt erst noch einmal
  und legt die Datei nur beiseite, ohne sie je zu löschen.
• RoRoRo beendet sich schneller, und wenn ein anderes Programm
  es zum Schließen auffordert, klappt das jetzt auch beim Start.
• Ein Konto mit abgelaufener Anmeldung löst jetzt eine Meldung
  aus, wenn es herausfällt. Ein fehlgeschlagener Start erscheint
  in seiner Zeile.
• Die Kürzel-Liste auf der Seite „Info“ und Datumsangaben in
  ganz RoRoRo folgen deiner Sprache.

Beende RoRoRo, bevor du aus dem Store aktualisierst: Rechtsklick
auf das Symbol im Infobereich, dann „Beenden“.

Sonst hat sich nichts geändert. Konten, Designs, Plugins,
Verlauf und alle anderen Einstellungen bleiben, wie sie sind.
```

## Français

```
v1.40.0.0

Un bouton de mise à jour, un RoRoRo plus stable

• Nouveau : le bouton « Mise à jour disponible » dans la fenêtre
  principale. Il apparaît dès qu'un RoRoRo plus récent est
  disponible, et un clic ouvre la page de RoRoRo dans le
  Microsoft Store. Pour le savoir, RoRoRo interroge le Store via
  Windows, après son démarrage puis toutes les six heures, sans
  rien envoyer de lui-même. Il fonctionnera à partir de la mise
  à jour suivante.
• Un fichier endommagé de jeux enregistrés, de serveurs privés,
  d'historique ou de statistiques n'empêche plus RoRoRo de
  démarrer : ce qui est lisible est conservé. Un fichier de
  comptes endommagé est restauré depuis sa sauvegarde et n'est
  jamais lu comme vide. « Repartir de zéro » revérifie d'abord
  et ne fait que mettre le fichier de côté, sans le supprimer.
• RoRoRo se ferme plus vite, et un autre programme peut aussi le
  fermer pendant le démarrage.
• Un compte dont la connexion a expiré déclenche une alerte en
  quittant sa partie. Un lancement qui échoue s'affiche sur sa
  ligne.
• La liste des raccourcis de « À propos » et les dates dans tout
  RoRoRo suivent votre langue.

Quittez RoRoRo avant de mettre à jour depuis le Store : clic
droit sur l'icône de la zone de notification, puis « Quitter ».

Rien d'autre n'a changé. Comptes, thèmes, plugins, historique et
tous les autres réglages sont conservés tels quels.
```

## Русский

```
v1.40.0.0

Кнопка обновления и более стабильный RoRoRo

• Новое: кнопка «Доступно обновление» в главном окне. Она
  появляется, когда выходит более новая версия RoRoRo, и один
  щелчок открывает страницу RoRoRo в Microsoft Store. Чтобы
  узнать об этом, RoRoRo спрашивает Store через Windows после
  запуска и затем каждые шесть часов и ничего своего не
  отправляет. Кнопка начнёт работать со следующего обновления.
• Повреждённый файл сохранённых игр, приватных серверов, истории
  или статистики больше не мешает RoRoRo запуститься: всё
  читаемое сохраняется. Повреждённый файл аккаунтов
  восстанавливается из резервной копии и никогда не считается
  пустым. «Начать заново» теперь сначала проверяет ещё раз и
  лишь откладывает файл в сторону, никогда его не удаляя.
• RoRoRo закрывается быстрее, а просьба другой программы закрыть
  его теперь работает и во время запуска.
• Если у аккаунта истёк вход и он выпал из игры, приходит
  оповещение. Неудачный запуск отображается в его строке.
• Список сочетаний клавиш на странице «О программе» и даты во
  всём RoRoRo следуют вашему языку.

Выйдите из RoRoRo перед обновлением из Store: щёлкните правой
кнопкой по значку в трее и выберите «Выйти».

Больше ничего не изменилось. Аккаунты, темы, плагины, история и
все остальные настройки остаются как были.
```

## Português (Brasil)

```
v1.40.0.0

Um botão de atualização e um RoRoRo mais estável

• Novo: o botão “Atualização disponível” na janela principal.
  Ele aparece quando sai um RoRoRo mais novo, e um clique abre a
  página do RoRoRo na Microsoft Store. Para saber, o RoRoRo
  pergunta à Store pelo Windows, depois de iniciar e a cada seis
  horas, e não envia nada por conta própria. Ele começa a
  funcionar a partir da próxima atualização.
• Um arquivo danificado de jogos salvos, servidores privados,
  histórico ou estatísticas não impede mais o RoRoRo de iniciar:
  o que dá para ler é mantido. Um arquivo de contas danificado é
  restaurado a partir da cópia de segurança e nunca é lido como
  vazio. “Começar do zero” agora confere de novo antes e só
  coloca o arquivo de lado, sem nunca apagá-lo.
• O RoRoRo fecha mais rápido, e o pedido de outro programa para
  fechá-lo agora também funciona durante a inicialização.
• Uma conta com o login expirado agora gera um alerta quando sai
  do jogo. Um início que falha aparece na linha da conta.
• A lista de atalhos da página “Sobre” e as datas em todo o
  RoRoRo seguem o seu idioma.

Saia do RoRoRo antes de atualizar pela Store: clique com o
botão direito no ícone da bandeja e escolha “Sair”.

Nada mais mudou. Contas, temas, plugins, histórico e todas as
outras configurações continuam como estavam.
```

## Polski

```
v1.40.0.0

Przycisk aktualizacji i stabilniejszy RoRoRo

• Nowość: przycisk „Dostępna aktualizacja” w głównym oknie.
  Pojawia się, gdy wyjdzie nowszy RoRoRo, a jedno kliknięcie
  otwiera stronę RoRoRo w Microsoft Store. Żeby to wiedzieć,
  RoRoRo pyta Store za pośrednictwem Windows, po uruchomieniu i
  potem co sześć godzin, i sam niczego nie wysyła. Zacznie
  działać od następnej aktualizacji.
• Uszkodzony plik zapisanych gier, prywatnych serwerów, historii
  lub statystyk nie blokuje już uruchomienia RoRoRo: to, co da
  się odczytać, zostaje. Uszkodzony plik kont jest odtwarzany z
  kopii zapasowej i nigdy nie jest odczytywany jako pusty.
  „Zacznij od nowa” najpierw sprawdza jeszcze raz i tylko
  odkłada plik na bok, nigdy go nie usuwa.
• RoRoRo zamyka się szybciej, a prośba innego programu o
  zamknięcie działa teraz także podczas uruchamiania.
• Konto z wygasłym logowaniem wywołuje teraz alert, gdy wypada z
  gry. Nieudane uruchomienie pojawia się w jego wierszu.
• Lista skrótów na stronie „O programie” i daty w całym RoRoRo
  zależą od twojego języka.

Zakończ RoRoRo przed aktualizacją ze Store: kliknij prawym
przyciskiem ikonę w zasobniku i wybierz „Zakończ”.

Nic więcej się nie zmieniło. Konta, motywy, pluginy, historia i
wszystkie inne ustawienia zostają bez zmian.
```

## Español

```
v1.40.0.0

Un botón de actualización y un RoRoRo más estable

• Nuevo: el botón «Actualización disponible» en la ventana
  principal. Aparece cuando sale un RoRoRo más nuevo, y un clic
  abre la página de RoRoRo en la Microsoft Store. Para saberlo,
  RoRoRo pregunta a la Store a través de Windows, tras arrancar
  y cada seis horas, y no envía nada por su cuenta. Empezará a
  funcionar a partir de la próxima actualización.
• Un archivo dañado de juegos guardados, servidores privados,
  historial o estadísticas ya no impide que RoRoRo arranque: lo
  que se puede leer se conserva. Un archivo de cuentas dañado se
  restaura desde su copia de seguridad y nunca se lee como
  vacío. «Empezar de cero» ahora vuelve a comprobar primero y
  solo aparta el archivo, sin borrarlo nunca.
• RoRoRo se cierra más rápido, y la petición de otro programa
  para cerrarlo ahora también funciona durante el arranque.
• Una cuenta con el inicio de sesión caducado ahora genera un
  aviso cuando se cae de la partida. Un inicio que falla se
  muestra en su fila.
• La lista de atajos de la página «Acerca de» y las fechas en
  todo RoRoRo siguen tu idioma.

Sal de RoRoRo antes de actualizar desde la Store: haz clic
derecho en el icono de la bandeja y elige «Salir».

Nada más cambió. Cuentas, temas, plugins, historial y todos los
demás ajustes se mantienen intactos.
```
