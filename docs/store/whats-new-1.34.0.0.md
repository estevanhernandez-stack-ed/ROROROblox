# What's new in this version — v1.34.0.0

> Paste the matching fenced block into Partner Center → your app → **Store listings** → *What's new
> in this version*: the English block for the en listing, and each translated block for its own
> language listing. (Since v1.28 the ten rows go in through the listing console instead; the blocks
> are the same either way.)
>
> **Written for someone on the v1.33 Store install.** Single-version delta, and a small one.
>
> **Metric alerts are deliberately left out.** The clan-facing notes lead with the change to how
> metric alerts name accounts, but this field is for Store readers, and the standing ruling on the
> listing is that a Store install arrives with no reporter plugin, so there are no metric alerts to
> describe. Promising a change to them here would imply a feature the listing does not claim.
>
> **The idle bullet is narrower than the clan notes on purpose.** The notes say a hand edit to
> `settings.json` lands within 30 seconds; that is true and useful to people who hand-edit, and noise
> to a Store reader, who changes the threshold through the picker, where it always applied at once.
>
> **UI names are the app's own translations**, read from the per-culture catalogs rather than
> re-translated: `ThemeShare_ShareButton`, `ThemeShare_ImportButton`, `ShellWindow_About` and
> `SettingsPage_SoundSilent`. "The theme builder" is phrased the way each language's
> `SettingsPage_OpenTheThemeBuilderCopy` already names it, and the presence-privacy wording, including
> each language's word for Invisible, follows `SettingsPage_IdleInvisibleHint`. Each block quotes its
> own language's labels exactly as the app renders them, in that language's quotation marks.
>
> Register follows each existing sheet: **vous** in French, **вы** in Russian, **du** in German,
> informal elsewhere. Product nouns stay English: RoRoRo, Roblox.
>
> **Verifier:** see `whats-new-1.34.0.0.translations.json`, generated from this file, and the run
> recorded in the 1.34 submission packet. Rule 3's "in-app UI strings must stay in English" false
> positive is expected again and overruled on the same ground as every release since v1.26: the
> interface is localized, and every quoted label above names its catalog key so the overrule is
> checkable.
>
> Sources: `docs/store/release-notes-1.34.0.0.md`.

---

## English

```
v1.34.0.0

Share your themes

• "Share theme" saves a theme you made as a file you can send to
  anyone. "Import theme" applies one a friend sent you, straight
  away. Both are in the theme builder.
• "About" shows the version you actually have installed.
• The idle warning always runs at one of the times its picker
  offers.
• Settings now explains three things that used to be guesswork:
  why an account with Roblox presence privacy on (Invisible)
  never gets an idle alert, what "Silent" covers, and what counts
  as activity.

Nothing else moved. Accounts, themes, plugins, history and every
other setting carry over untouched.
```

## Deutsch

```
v1.34.0.0

Teile deine Designs

• „Design teilen“ speichert ein Design, das du gebaut hast, als
  Datei, die du an jeden schicken kannst. „Design importieren“
  übernimmt eins, das dir jemand geschickt hat, sofort. Beides
  findest du im Design-Builder.
• „Über“ zeigt die Version, die du wirklich installiert hast.
• Die Inaktivitäts-Warnung läuft immer mit einer der Zeiten, die
  ihre Auswahl anbietet.
• Die Einstellungen erklären jetzt drei Dinge, bei denen man
  bisher raten musste: warum ein Konto mit eingeschaltetem
  Roblox-Anwesenheitsschutz (Unsichtbar) nie eine
  Inaktivitäts-Benachrichtigung bekommt, was „Stumm“ abdeckt und
  was als Aktivität zählt.

Sonst hat sich nichts geändert. Konten, Designs, Plugins, Verlauf
und alle anderen Einstellungen bleiben, wie sie sind.
```

## Français

```
v1.34.0.0

Partagez vos thèmes

• « Partager le thème » enregistre un thème que vous avez créé
  dans un fichier à envoyer à qui vous voulez. « Importer un
  thème » applique aussitôt celui qu'on vous a envoyé. Les deux se
  trouvent dans le créateur de thèmes.
• « À propos » affiche la version réellement installée.
• L'alerte d'inactivité utilise toujours l'une des durées
  proposées par son sélecteur.
• Les paramètres expliquent désormais trois points qu'il fallait
  deviner : pourquoi un compte dont la confidentialité de présence
  Roblox est activée (Invisible) ne reçoit jamais d'alerte
  d'inactivité, ce que couvre « Silencieux » et ce qui compte
  comme activité.

Rien d'autre n'a changé. Comptes, thèmes, plugins, historique et
tous les autres réglages sont conservés tels quels.
```

## Русский

```
v1.34.0.0

Делитесь своими темами

• «Поделиться темой» сохраняет созданную вами тему в файл,
  который можно отправить кому угодно. «Импортировать тему» сразу
  применяет тему, которую прислали вам. Обе кнопки — в
  конструкторе тем.
• «О программе» показывает версию, которая у вас действительно
  установлена.
• Оповещение о простое всегда работает с одним из сроков, которые
  предлагает его список.
• Настройки теперь объясняют три вещи, о которых раньше
  приходилось догадываться: почему аккаунт с включённой
  приватностью присутствия Roblox (Невидимка) никогда не получает
  оповещение о простое, что охватывает «Без звука» и что
  считается активностью.

Больше ничего не изменилось. Аккаунты, темы, плагины, история и
все остальные настройки остаются как были.
```

## Português (Brasil)

```
v1.34.0.0

Compartilhe seus temas

• “Compartilhar tema” salva um tema que você criou em um arquivo
  para mandar para quem quiser. “Importar tema” aplica na hora um
  tema que alguém te mandou. Os dois ficam no criador de temas.
• “Sobre” mostra a versão que você realmente tem instalada.
• O alerta de inatividade sempre usa um dos tempos que o seletor
  oferece.
• As configurações agora explicam três coisas que antes eram no
  chute: por que uma conta com a privacidade de presença do Roblox
  ativada (Invisível) nunca recebe alerta de inatividade, o que o
  “Silencioso” cobre e o que conta como atividade.

Nada mais mudou. Contas, temas, plugins, histórico e todas as
outras configurações continuam como estavam.
```

## Polski

```
v1.34.0.0

Udostępniaj swoje motywy

• „Udostępnij motyw” zapisuje stworzony przez ciebie motyw do
  pliku, który możesz wysłać komu chcesz. „Importuj motyw” od razu
  stosuje motyw, który ktoś ci przysłał. Oba znajdziesz w kreatorze
  motywów.
• „O programie” pokazuje wersję, którą naprawdę masz
  zainstalowaną.
• Alert o bezczynności zawsze działa z jednym z czasów, które
  oferuje jego lista.
• Ustawienia wyjaśniają teraz trzy rzeczy, które wcześniej
  trzeba było zgadywać: dlaczego konto z włączoną prywatnością
  obecności Roblox (Niewidoczny) nigdy nie dostaje alertu
  o bezczynności, co obejmuje „Cisza” i co liczy się jako
  aktywność.

Nic więcej się nie zmieniło. Konta, motywy, pluginy, historia
i wszystkie inne ustawienia zostają bez zmian.
```

## Español

```
v1.34.0.0

Comparte tus temas

• «Compartir tema» guarda un tema que hayas creado en un archivo
  que puedes mandar a quien quieras. «Importar tema» aplica al
  momento el que te hayan enviado. Los dos están en el creador de
  temas.
• «Acerca de» muestra la versión que de verdad tienes instalada.
• El aviso de inactividad siempre usa uno de los tiempos que
  ofrece su selector.
• La configuración ahora explica tres cosas que antes había que
  adivinar: por qué una cuenta con la privacidad de presencia de
  Roblox activada (Invisible) nunca recibe un aviso de
  inactividad, qué cubre «Silencio» y qué cuenta como actividad.

Nada más cambió. Cuentas, temas, plugins, historial y todos los
demás ajustes se mantienen intactos.
```
