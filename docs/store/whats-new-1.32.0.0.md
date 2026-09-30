# What's new in this version — v1.32.0.0

> Paste the matching fenced block into Partner Center → your app → **Store listings** → *What's new
> in this version*: the English block for the en listing, and each translated block for its own
> language listing. (Since v1.28 the ten rows go in through the listing console instead; the blocks
> are the same either way.)
>
> **Written for someone on the v1.31 Store install.** Single-version delta.
>
> **The overnight story is not here.** The notes open on alts found kicked in the morning. On a
> public field that reads as a confession about Roblox rather than a reason. The block says what
> the option does and, first, what it never does.
>
> **"Never touches that page" is kept in every language.** A reader told that an app handles a
> verification page will assume it solves it. The block says it closes the window instead, which is
> the whole of the Store posture for this feature.
>
> **UI names are the app's own translations**, read from `Strings.<culture>.resx` rather than
> re-translated: the row menu items (`MainWindow_RejoinIfItDropsOut`,
> `MainWindow_JoinViaFriendChallengeProne`, used here without its parenthetical) and the dialog
> buttons (`FlaggedLaunchWindow_FollowAnother`, `FlaggedLaunchWindow_ItsFixed`,
> `FlaggedLaunchWindow_Cancel`), plus Launch multiple and Squad Launch.
>
> Product nouns stay English throughout: RoRoRo, Roblox.
>
> **Verifier run: DONE at `f5815c2`, both shapes, 12 pairs.** All 12 drew the same rule-3 false
> positive (keep UI labels in English), overruled because the interface is localized; no other
> finding. Spanish re-verified at `1d6a86d` after its title fix, same outcome. Details in
> `submission-packet-1.32.0.0.md` section 3.
>
> Sources: `docs/store/release-notes-1.32.0.0.md`, the 2026-09-29 entry in `docs/decisions.md`,
> PR #223.

---

## English

```
v1.32.0.0

Alts that drop out come back on their own
• Right-click an account and turn on "Rejoin if it drops out".
  If its Roblox window is open but it has been out of the game
  for 3 minutes, RoRoRo closes it and joins again. Never
  offered for your main.
• Idle kicks, failed joins and Roblox's verification page are
  all covered. RoRoRo never touches that page: it closes the
  window and joins again.
• At most 3 rejoins an hour per account. After that it turns
  itself off for that account and tells you. Your own stop
  always wins.

Join via friend now works everywhere
• An account with Join via friend ticked follows your main on
  every launch, not only in Squad Launch.
• If your main isn't in a game, RoRoRo asks: follow another
  account, "It's fixed, join directly", or Cancel.
• Launch multiple and Squad Launch ask once for the whole
  batch instead of joining those accounts directly.
```

## Français (fr)

```
v1.32.0.0

Les alts qui décrochent reviennent tout seuls
• Faites un clic droit sur un compte et activez « Rejoindre à
  nouveau s'il décroche ». Si sa fenêtre Roblox est ouverte
  mais que le compte est hors du jeu depuis 3 minutes, RoRoRo
  la ferme et rejoint à nouveau. Jamais proposé pour votre
  compte principal.
• Cela couvre les expulsions pour inactivité, les connexions
  échouées et la page de vérification de Roblox. RoRoRo ne
  touche jamais à cette page : il ferme la fenêtre et rejoint
  à nouveau.
• Au plus 3 reconnexions par heure et par compte. Au-delà,
  l'option se désactive pour ce compte et vous prévient. Quand
  vous arrêtez vous-même, c'est toujours vous qui l'emportez.

« Rejoindre via un ami » fonctionne désormais partout
• Un compte où « Rejoindre via un ami » est coché suit votre
  compte principal à chaque lancement, pas seulement dans
  Squad Launch.
• Si votre compte principal n'est pas en jeu, RoRoRo vous
  demande : suivre un autre compte, « C'est réglé, rejoindre
  directement » ou Annuler.
• « Lancer plusieurs » et Squad Launch posent la question une
  seule fois pour tout le lot, au lieu de faire rejoindre ces
  comptes directement.
```

## Deutsch (de)

```
v1.32.0.0

Alts, die rausfliegen, kommen von selbst zurück
• Klick mit der rechten Maustaste auf ein Konto und schalte
  „Erneut beitreten, wenn es ausfällt“ ein. Ist sein
  Roblox-Fenster offen, das Konto aber seit 3 Minuten nicht im
  Spiel, schließt RoRoRo das Fenster und tritt erneut bei. Wird
  für dein Hauptkonto nie angeboten.
• Kicks wegen Inaktivität, fehlgeschlagene Beitritte und die
  Verifizierungsseite von Roblox sind alle abgedeckt. RoRoRo
  rührt diese Seite nie an: Es schließt das Fenster und tritt
  erneut bei.
• Höchstens 3 Neubeitritte pro Stunde und Konto. Danach
  schaltet sich die Option für dieses Konto selbst ab und sagt
  dir Bescheid. Wenn du selbst stoppst, hat das immer Vorrang.

„Über Freund beitreten“ funktioniert jetzt überall
• Ein Konto mit Häkchen bei „Über Freund beitreten“ folgt bei
  jedem Start deinem Hauptkonto, nicht nur in Squad Launch.
• Ist dein Hauptkonto in keinem Spiel, fragt RoRoRo: einem
  anderen Konto folgen, „Behoben, direkt beitreten“ oder
  Abbrechen.
• „Mehrere starten“ und Squad Launch fragen einmal für die
  ganze Gruppe, statt diese Konten direkt beitreten zu lassen.
```

## Русский (ru)

```
v1.32.0.0

Вылетевшие альты возвращаются сами
• Щёлкните по аккаунту правой кнопкой и включите
  «Перезаходить, если отвалится». Если его окно Roblox
  открыто, но аккаунт уже 3 минуты вне игры, RoRoRo закрывает
  окно и заходит снова. Для основного аккаунта это никогда не
  предлагается.
• Кик за бездействие, неудачный вход, страница проверки
  Roblox — срабатывает во всех этих случаях. RoRoRo никогда
  не трогает эту страницу: он закрывает окно и заходит снова.
• Не больше 3 перезаходов в час на аккаунт. После этого опция
  сама отключается для этого аккаунта и сообщает вам. Если вы
  остановили аккаунт сами, это всегда в приоритете.

«Присоединяться через друга» теперь работает везде
• Аккаунт с включённой опцией «Присоединяться через друга»
  при каждом запуске следует за вашим основным аккаунтом, а
  не только в Squad Launch.
• Если ваш основной аккаунт не в игре, RoRoRo спрашивает:
  присоединиться к другому аккаунту, «Исправлено,
  присоединиться напрямую» или «Отмена».
• «Запустить несколько» и Squad Launch спрашивают один раз
  для всей группы, а не присоединяют эти аккаунты напрямую.
```

## Português (Brasil) (pt-BR)

```
v1.32.0.0

Alts que caem voltam sozinhos
• Clique com o botão direito em uma conta e ative "Entrar de
  novo se cair". Se a janela do Roblox dela estiver aberta,
  mas a conta estiver fora do jogo há 3 minutos, o RoRoRo fecha
  a janela e entra de novo. Nunca é oferecido para a sua conta
  principal.
• Isso cobre expulsões por inatividade, entradas que falharam
  e a página de verificação do Roblox. O RoRoRo nunca mexe
  nessa página: ele fecha a janela e entra de novo.
• No máximo 3 reentradas por hora por conta. Depois disso, a
  opção se desliga sozinha para essa conta e avisa você. Uma
  parada feita por você sempre tem prioridade.

"Entrar via amigo" agora funciona em todo lugar
• Uma conta com "Entrar via amigo" marcado segue a sua conta
  principal a cada inicialização, não só no Squad Launch.
• Se a sua conta principal não estiver em um jogo, o RoRoRo
  pergunta: seguir outra conta, "Resolvido, entrar direto" ou
  Cancelar.
• "Iniciar vários" e o Squad Launch perguntam uma vez só para
  o lote inteiro, em vez de fazer essas contas entrarem
  direto.
```

## Polski (pl)

```
v1.32.0.0

Konta, które wypadną, wracają same
• Kliknij konto prawym przyciskiem i włącz „Dołącz ponownie,
  jeśli wypadnie”. Jeśli jego okno Roblox jest otwarte, ale
  konto od 3 minut jest poza grą, RoRoRo zamyka okno i dołącza
  ponownie. Opcja nigdy nie jest proponowana dla twojego
  głównego konta.
• Obejmuje to wyrzucenie za bezczynność, nieudane dołączenie
  i stronę weryfikacji Roblox. RoRoRo nigdy nie dotyka tej
  strony: zamyka okno i dołącza ponownie.
• Najwyżej 3 ponowne dołączenia na godzinę na konto. Potem
  opcja sama wyłącza się dla tego konta i daje ci znać. Twoje
  własne zatrzymanie zawsze ma pierwszeństwo.

„Dołącz przez znajomego” działa teraz wszędzie
• Konto z zaznaczonym „Dołącz przez znajomego” podąża za
  twoim głównym kontem przy każdym uruchomieniu, nie tylko w
  Squad Launch.
• Jeśli twoje główne konto nie jest w grze, RoRoRo pyta:
  dołącz do innego konta, „Naprawione, dołącz bezpośrednio”
  albo Anuluj.
• „Uruchom wiele” i Squad Launch pytają raz o całą grupę,
  zamiast dołączać te konta bezpośrednio.
```

## Español (es)

```
v1.32.0.0

Los alts que se desconectan vuelven solos
• Haz clic derecho en una cuenta y activa "Volver a unirse si
  se desconecta". Si su ventana de Roblox está abierta pero la
  cuenta lleva 3 minutos fuera del juego, RoRoRo la cierra y
  vuelve a unirse. Nunca se ofrece para tu cuenta principal.
• Cubre las expulsiones por inactividad, los intentos fallidos
  de unirse y la página de verificación de Roblox. RoRoRo nunca
  toca esa página: cierra la ventana y vuelve a unirse.
• Como máximo 3 reingresos por hora y por cuenta. Después, la
  opción se desactiva sola para esa cuenta y te avisa. Si
  detienes la cuenta tú, eso siempre tiene prioridad.

"Unirse mediante un amigo" ahora funciona en todas partes
• Una cuenta con "Unirse mediante un amigo" marcado sigue a tu
  cuenta principal en cada inicio, no solo en Squad Launch.
• Si tu cuenta principal no está en un juego, RoRoRo te
  pregunta: seguir a otra cuenta, "Solucionado, unirse
  directamente" o Cancelar.
• "Iniciar varias" y Squad Launch preguntan una sola vez por
  todo el lote, en lugar de que esas cuentas se unan
  directamente.
```

## What is deliberately not in this copy

**The overnight story.** See the preamble: alts found kicked in the morning is the release notes'
reason, and on a public field it reads as a confession about Roblox rather than a feature.

**The plugin reason code.** A contract detail for plugin authors; the contract package's own notes
carry it, and a Store reader only ever sees its effect.

**The known issues.** The late recovery from a mid-game verification page, the leftover Roblox
processes, the dialog an unattended rejoin can stop at, and the carried-forward four. They belong
in the release notes, where they can be read in full; one line each on a Store field reads as a
warning label with no room for the why.

**Launch multiple waiting for your main, and the private-link rule.** That a batch starts everyone
else first and sends the ticked accounts after your main lands, and that a private-server link
follows your main only when your main is in that same server. True and in the notes; mechanics a
Store reader meets in the dialog itself, and one sentence too many for a public field.

**The rest of the stop rules.** The 3-failed-relaunches trip, the alert channels, the no-guessing
wait on a rate-limited or expired session, and a Join via friend account waiting for your main.
"Turns itself off and tells you" and "your own stop always wins" carry the promise; the conditions
are the notes' job.
