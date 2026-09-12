# What's new in this version — v1.28.0.0

> Paste the matching fenced block into Partner Center → your app → **Store listings** → *What's new
> in this version* — the English block for the en listing, and each translated block for its own
> language listing. All seven live in this file rather than seven side files, because the workflow
> is paste-per-listing and one file is what you actually work from.
>
> **Written for someone on the v1.27 Store install.** Unlike the v1.27 block, this is a single-
> version delta: v1.27 reached the Store and this audience has it. No catching-up to do.
>
> **Path wording comes from the app's own resx, not from a translation of the English.** The nav
> item is **Alerts** and the switch is **Metric alerts**; both are named here in each language's
> own UI word — `Benachrichtigungen`, `Оповещения`, `Alerty o wskaźnikach`, and so on. A block that
> invents a menu name sends someone hunting for a setting that does not exist in their language.
>
> **The plugin caveat is in every block, and it is deliberate.** Metric alerts is the release's
> headline and nothing in this package reports a number — a plugin does. A block that announced the
> feature without saying so would send people to Settings to find a switch that appears to do
> nothing. Saying it costs two lines and saves the support conversation.
>
> Product nouns stay English throughout: RoRoRo, Discord, ntfy, Pushover, plugin, webhook, Enter.
>
> Sources: `docs/store/release-notes-1.28.0.0.md`, `docs/superpowers/specs/2026-09-09-external-metric-alerts-design.md`.

---

## English

```
v1.28.0.0

Alerts can watch a number
• Settings > Alerts has a Metric alerts switch. Turn it on and
  RoRoRo tells you when a number crosses a line you set — as a
  desktop toast, in your Discord channel, in your clan's, or on
  your phone, through the same routing every other alert uses.
• RoRoRo does not gather the number itself. A plugin you
  install reports it, and you decide whether to allow that when
  you install it.

Phone setup is a photo now
• Connecting ntfy meant typing a 33-character topic on a phone
  keyboard. Point the camera at the code instead.
• The code hides itself while streamer mode is on. It is the
  credential, and it reads off a paused frame far more easily
  than the text ever did.

Settings stops losing your edits
• Enter saves. Every box on the Alerts page used to hold your
  typing until you clicked somewhere else.
• Saved webhooks and phone keys have a Remove button.
• Turning streamer mode on now hides a revealed webhook or key,
  and asks before showing one while it is on.
```

## Français (fr)

```
v1.28.0.0

Les alertes peuvent surveiller un nombre
• Paramètres > Alertes contient un interrupteur Alertes
  métriques. Activez-le et RoRoRo vous prévient quand un nombre
  franchit un seuil que vous fixez — notification du bureau,
  votre salon Discord, celui de votre clan, ou votre téléphone,
  par le même routage que toutes les autres alertes.
• RoRoRo ne collecte pas ce nombre lui-même. Un plugin que vous
  installez le rapporte, et vous décidez de l'autoriser ou non
  au moment de l'installation.

La configuration du téléphone se fait en photo
• Connecter ntfy demandait de taper un sujet de 33 caractères
  au clavier du téléphone. Visez le code avec l'appareil photo.
• Le code disparaît quand le mode streamer est actif. C'est
  l'identifiant, et il se lit bien plus vite sur une image
  figée que le texte.

Les paramètres ne perdent plus vos saisies
• Entrée enregistre. Chaque champ de la page Alertes gardait
  votre texte jusqu'à ce que vous cliquiez ailleurs.
• Les webhooks et les clés de téléphone enregistrés ont un
  bouton Retirer.
• Activer le mode streamer masque désormais un webhook ou une
  clé affichés, et demande confirmation avant d'en afficher un
  quand il est actif.
```

## Deutsch (de)

```
v1.28.0.0

Benachrichtigungen können eine Zahl überwachen
• Einstellungen > Benachrichtigungen hat einen Schalter
  Metrik-Benachrichtigungen. Schalte ihn ein, und RoRoRo meldet
  sich, wenn eine Zahl eine Grenze überschreitet, die du setzt
  — als Desktop-Hinweis, in deinem Discord-Kanal, in dem deines
  Clans oder auf deinem Handy, über dieselbe Zustellung wie
  jede andere Benachrichtigung.
• RoRoRo erhebt die Zahl nicht selbst. Ein Plugin, das du
  installierst, meldet sie — und du entscheidest bei der
  Installation, ob du das erlaubst.

Die Handy-Einrichtung ist jetzt ein Foto
• Für ntfy musstest du ein Thema mit 33 Zeichen auf einer
  Handytastatur eintippen. Halte stattdessen die Kamera auf den
  Code.
• Der Code verschwindet, solange der Streamer-Modus an ist. Er
  ist die Zugangsdaten, und aus einem Standbild liest er sich
  weit leichter ab als der Text.

Einstellungen verlieren deine Eingaben nicht mehr
• Enter speichert. Jedes Feld auf der Benachrichtigungsseite
  hat deine Eingabe bisher behalten, bis du woanders geklickt
  hast.
• Gespeicherte Webhooks und Handy-Schlüssel haben einen
  Entfernen-Knopf.
• Der Streamer-Modus blendet jetzt einen sichtbaren Webhook
  oder Schlüssel aus und fragt nach, bevor er einen einblendet,
  solange er an ist.
```

## Русский (ru)

```
v1.28.0.0

Оповещения умеют следить за числом
• В разделе Параметры > Оповещения появился переключатель
  «Оповещения о метриках». Включите его, и RoRoRo сообщит,
  когда число пересечёт заданную вами границу — уведомлением на
  рабочем столе, в вашем канале Discord, в канале клана или на
  телефоне, тем же маршрутом, что и все остальные оповещения.
• RoRoRo не собирает это число сам. Его сообщает plugin,
  который вы устанавливаете, и вы решаете при установке,
  разрешать ли это.

Настройка телефона теперь делается фотографией
• Чтобы подключить ntfy, приходилось вводить тему из 33
  символов с клавиатуры телефона. Наведите камеру на код.
• Код скрывается, пока включён режим стримера. Это учётные
  данные, и с остановленного кадра он читается куда легче
  текста.

Параметры больше не теряют введённое
• Enter сохраняет. Раньше каждое поле на странице оповещений
  держало ваш текст, пока вы не щёлкнете в другом месте.
• У сохранённых webhook и ключей телефона появилась кнопка
  «Удалить».
• Включение режима стримера теперь скрывает показанный webhook
  или ключ и спрашивает, прежде чем показать его, пока режим
  включён.
```

## Português (Brasil) (pt-BR)

```
v1.28.0.0

Os alertas podem vigiar um número
• Configurações > Alertas tem uma chave Alertas de métricas.
  Ative e o RoRoRo avisa quando um número cruza um limite que
  você define — como aviso na área de trabalho, no seu canal do
  Discord, no do seu clã ou no seu telefone, pelo mesmo caminho
  que todos os outros alertas.
• O RoRoRo não coleta o número sozinho. Um plugin que você
  instala informa o número, e você decide se permite isso na
  hora de instalar.

A configuração do telefone virou uma foto
• Conectar o ntfy exigia digitar um tópico de 33 caracteres no
  teclado do celular. Aponte a câmera para o código.
• O código some enquanto o modo streamer está ligado. Ele é a
  credencial, e se lê de um quadro parado muito mais fácil que
  o texto.

As configurações não perdem mais o que você digita
• Enter salva. Cada campo da página de alertas guardava o que
  você digitou até você clicar em outro lugar.
• Webhooks e chaves de telefone salvos ganharam um botão
  Remover.
• Ligar o modo streamer agora esconde um webhook ou chave à
  mostra, e pergunta antes de mostrar um enquanto ele está
  ligado.
```

## Polski (pl)

```
v1.28.0.0

Alerty mogą pilnować liczby
• Ustawienia > Alerty mają przełącznik Alerty o wskaźnikach.
  Włącz go, a RoRoRo powie ci, gdy liczba przekroczy próg,
  który ustawisz — powiadomieniem na pulpicie, na twoim kanale
  Discord, na kanale klanu albo na telefonie, tą samą drogą co
  każdy inny alert.
• RoRoRo sam tej liczby nie zbiera. Zgłasza ją plugin, który
  instalujesz, a ty decydujesz przy instalacji, czy na to
  pozwolić.

Konfiguracja telefonu to teraz zdjęcie
• Podłączenie ntfy wymagało wpisania 33-znakowego tematu na
  klawiaturze telefonu. Wyceluj w kod aparatem.
• Kod znika, gdy tryb streamera jest włączony. To są dane
  dostępowe, a z zatrzymanej klatki czyta się je o wiele
  łatwiej niż tekst.

Ustawienia nie gubią już tego, co wpiszesz
• Enter zapisuje. Każde pole na stronie alertów trzymało twój
  tekst, dopóki nie kliknąłeś gdzie indziej.
• Zapisane webhooki i klucze telefonu mają przycisk Usuń.
• Włączenie trybu streamera ukrywa teraz pokazany webhook lub
  klucz i pyta, zanim pokaże któryś, gdy tryb jest włączony.
```

## Español (es)

```
v1.28.0.0

Las alertas pueden vigilar un número
• Configuración > Alertas tiene un interruptor Alertas de
  métricas. Actívalo y RoRoRo te avisa cuando un número cruza
  un límite que tú fijas — como aviso de escritorio, en tu
  canal de Discord, en el de tu clan o en tu teléfono, por la
  misma ruta que todas las demás alertas.
• RoRoRo no recoge el número por su cuenta. Lo informa un
  plugin que instalas, y tú decides al instalarlo si lo
  permites.

Configurar el teléfono ahora es una foto
• Conectar ntfy obligaba a escribir un tema de 33 caracteres en
  el teclado del móvil. Apunta la cámara al código.
• El código desaparece mientras el modo streamer está activo.
  Es la credencial, y se lee de un fotograma detenido mucho más
  fácil que el texto.

La configuración ya no pierde lo que escribes
• Enter guarda. Cada campo de la página de alertas conservaba
  lo escrito hasta que hacías clic en otro sitio.
• Los webhooks y las claves de teléfono guardados tienen un
  botón Quitar.
• Activar el modo streamer ahora oculta un webhook o una clave
  a la vista, y pregunta antes de mostrar uno mientras está
  activo.
```

## What is deliberately not in this copy

**The smoke harness, and everything under `tools/`.** v1.28 carries a test harness that replays
sixteen metric-alert scenarios, and the walk that ticked eighteen of twenty-one manual rows. Real
work, invisible to a user, and a what's-new block is not a changelog.

**The Pushover QR codes, because they were built and removed in the same cycle.** Two codes shipped
in the QR work and were dropped after a live scan test: Pushover setup is a PC flow start to finish,
so a code sending you to your phone added a step. Only the ntfy code survives, and only that one is
announced. Announcing and then retracting a feature inside one release is worse than never
mentioning it.

**The report-policy work in the plugin contract.** `ReportMetric` is an API, not a feature anyone
turns on, and the block already says the part that matters to a user: a plugin supplies the number
and consent is asked at install.

**Anything about what the first reporting plugin will be.** One exists and is not released. A
what's-new block that promises a plugin a user cannot install is a promise with a date attached to
it that nobody agreed to.
