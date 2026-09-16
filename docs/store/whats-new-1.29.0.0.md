# What's new in this version — v1.29.0.0

> Paste the matching fenced block into Partner Center → your app → **Store listings** → *What's new
> in this version* — the English block for the en listing, and each translated block for its own
> language listing. All seven live in this file rather than seven side files, because the workflow
> is paste-per-listing and one file is what you actually work from. (Since v1.28 the ten rows go in
> through the listing console instead; the blocks are the same either way.)
>
> **Written for someone on the v1.28 Store install.** v1.28 certified and published on 2026-09-12,
> so this is a single-version delta. No catching-up to do.
>
> **The quoted strings are the real ones, with the account name left off.** A live run on
> 2026-09-15 posted `CElCPapa — ps99.diamonds` over `• CElCPapa — ps99.diamonds at 2974993`; the
> same breach now posts `CElCPapa — Diamonds went above 0` over `• CElCPapa — now 2,974,993`. The
> blocks quote the half that carries the change and drop a real player's name out of Store copy.
>
> **Every translated block says the alert sentence is still English, and that is deliberate.**
> `WebhookPayload` is Core: one payload feeds the desktop toast, both webhooks and the phone, and it
> composes in English. A Polish player who reads "alerts now say what fired" and then gets
> "Diamonds went above 0" has been told something true in a way that reads as a translation miss.
> One line costs less than the support conversation. The English block does not carry it, for
> obvious reasons.
>
> **The plugin caveat carries forward from v1.28, shortened.** The first two sections only show up
> for someone running a plugin that reports numbers, and nothing in this package reports one. The
> third section applies to every alert anyone already has. Saying which is which is the difference
> between a block that lands and a block that sends people to Settings.
>
> Product nouns stay English throughout: RoRoRo, Discord, Pushover, ntfy, plugin, webhook. So do the
> quoted alert strings, because they are what the app actually prints.
>
> Sources: `docs/superpowers/specs/2026-09-15-metric-alert-wording-design.md` (with its APPROVED
> DEVIATIONS banner, rulings C1-C3), `docs/superpowers/plans/2026-09-15-metric-alert-wording.md`,
> the 2026-09-15 entry in `docs/decisions.md`.

---

## English

```
v1.29.0.0

Metric alerts say what fired
• An alert used to read "ps99.diamonds at 2974993". It now
  reads "Diamonds went above 0" over "now 2,974,993" — the
  name you gave the rule, what you asked it to watch for, and
  numbers you can read at a glance.
• A rule without a name still falls back to the metric id, the
  way it read before.
• RoRoRo still gathers no number itself. A plugin you install
  reports it, and nothing in this package does.

One read is one alert
• A plugin reporting a number for eight accounts at once used
  to send eight alerts to every destination. That is now one:
  "8 accounts — Diamonds went above 0", a line per account.
• Two different numbers crossing their line on the same
  account in the same read now both reach you.

Alerts can't ping a channel, and a long one still posts
• Discord posts go out with mentions switched off, so nothing
  inside an alert can notify the channel it lands in.
• A long list of accounts is trimmed to fit and ends with
  "and 12 more" instead of failing to post. Every alert kind,
  not just this one.
```

## Français (fr)

```
v1.29.0.0

Les alertes métriques disent ce qui s'est passé
• Une alerte affichait « ps99.diamonds at 2974993 ». Elle
  affiche maintenant « Diamonds went above 0 » puis « now
  2,974,993 » — le nom que vous avez donné à la règle, ce que
  vous lui demandez de guetter, et des nombres lisibles.
• Une règle sans nom revient à l'identifiant de la métrique,
  comme avant.
• RoRoRo ne collecte toujours aucun nombre. Un plugin que vous
  installez le rapporte, et rien dans ce paquet ne le fait.
• La phrase de l'alerte reste en anglais dans toutes les
  langues pour l'instant.

Une lecture, une alerte
• Un plugin qui rapporte un nombre pour huit comptes d'un coup
  envoyait huit alertes à chaque destination. C'est désormais
  une seule : « 8 accounts — Diamonds went above 0 », une
  ligne par compte.
• Deux nombres différents qui franchissent leur seuil sur le
  même compte dans la même lecture vous parviennent tous deux.

Une alerte ne peut plus notifier un salon, et une longue passe
quand même
• Les messages Discord partent avec les mentions désactivées :
  rien dans une alerte ne peut notifier le salon où elle
  arrive.
• Une longue liste de comptes est raccourcie pour tenir et se
  termine par « and 12 more » au lieu d'échouer. Pour tous les
  types d'alerte.
```

## Deutsch (de)

```
v1.29.0.0

Metrik-Benachrichtigungen sagen, was passiert ist
• Eine Benachrichtigung las sich als „ps99.diamonds at
  2974993". Jetzt steht dort „Diamonds went above 0" und
  darunter „now 2,974,993" — der Name, den du der Regel
  gegeben hast, wonach sie suchen soll, und lesbare Zahlen.
• Eine Regel ohne Namen fällt weiter auf die Metrik-ID zurück,
  wie bisher.
• RoRoRo erhebt weiterhin keine Zahl selbst. Ein Plugin, das
  du installierst, meldet sie, und in diesem Paket tut das
  nichts.
• Der Satz der Benachrichtigung ist vorerst in jeder Sprache
  englisch.

Ein Lesevorgang, eine Benachrichtigung
• Ein Plugin, das eine Zahl für acht Konten auf einmal meldet,
  schickte acht Benachrichtigungen an jedes Ziel. Das ist
  jetzt eine: „8 accounts — Diamonds went above 0", eine Zeile
  pro Konto.
• Zwei verschiedene Zahlen, die im selben Lesevorgang auf
  demselben Konto ihre Grenze überschreiten, erreichen dich
  jetzt beide.

Keine Benachrichtigung kann einen Kanal anpingen, und eine
lange kommt trotzdem an
• Discord-Posts gehen mit abgeschalteten Erwähnungen raus:
  nichts in einer Benachrichtigung kann den Kanal anpingen, in
  dem sie landet.
• Eine lange Kontoliste wird passend gekürzt und endet mit
  „and 12 more", statt an der Länge zu scheitern. Für jede Art
  von Benachrichtigung.
```

## Русский (ru)

```
v1.29.0.0

Оповещения о метриках говорят, что произошло
• Раньше оповещение читалось как «ps99.diamonds at 2974993».
  Теперь — «Diamonds went above 0», а ниже «now 2,974,993»:
  название, которое вы дали правилу, то, за чем вы просили
  следить, и числа, читаемые с первого взгляда.
• Правило без названия по-прежнему возвращается к
  идентификатору метрики, как раньше.
• RoRoRo по-прежнему не собирает число сам. Его сообщает
  plugin, который вы устанавливаете, и в этом пакете такого
  нет.
• Сама фраза оповещения пока на английском во всех языках.

Одно чтение — одно оповещение
• Plugin, сообщающий число сразу по восьми аккаунтам, слал
  восемь оповещений на каждый адрес. Теперь это одно:
  «8 accounts — Diamonds went above 0», по строке на аккаунт.
• Два разных числа, перешедших свою границу на одном аккаунте
  в одном чтении, теперь доходят оба.

Оповещение не может упомянуть канал, а длинное всё равно
доходит
• Сообщения в Discord уходят с отключёнными упоминаниями:
  ничто внутри оповещения не может пингануть канал, куда оно
  приходит.
• Длинный список аккаунтов сокращается по размеру и
  заканчивается «and 12 more», вместо того чтобы не дойти.
  Для оповещений любого вида.
```

## Português (Brasil) (pt-BR)

```
v1.29.0.0

Os alertas de métrica dizem o que aconteceu
• Um alerta aparecia como "ps99.diamonds at 2974993". Agora
  aparece "Diamonds went above 0" e abaixo "now 2,974,993" —
  o nome que você deu à regra, o que você mandou vigiar, e
  números que dá para ler de relance.
• Uma regra sem nome continua voltando ao identificador da
  métrica, como era antes.
• O RoRoRo continua não coletando número nenhum. Quem informa
  é um plugin que você instala, e nada neste pacote faz isso.
• A frase do alerta ainda sai em inglês em todos os idiomas.

Uma leitura, um alerta
• Um plugin que informa um número de oito contas de uma vez
  mandava oito alertas para cada destino. Agora é um só:
  "8 accounts — Diamonds went above 0", uma linha por conta.
• Dois números diferentes cruzando o limite na mesma conta e
  na mesma leitura agora chegam os dois.

Nenhum alerta marca o canal, e um alerta longo ainda chega
• As mensagens do Discord saem com as menções desligadas:
  nada dentro de um alerta marca o canal onde ele cai.
• Uma lista longa de contas é cortada para caber e termina com
  "and 12 more", em vez de falhar por tamanho. Vale para todo
  tipo de alerta.
```

## Polski (pl)

```
v1.29.0.0

Alerty o wskaźnikach mówią, co się stało
• Alert wyglądał tak: „ps99.diamonds at 2974993". Teraz czytasz
  „Diamonds went above 0", a pod spodem „now 2,974,993" —
  nazwę, którą nadałeś regule, to, czego kazałeś pilnować, i
  liczby czytelne na pierwszy rzut oka.
• Reguła bez nazwy nadal wraca do identyfikatora wskaźnika,
  tak jak dotąd.
• RoRoRo nadal sam nie zbiera żadnej liczby. Zgłasza ją plugin,
  który instalujesz, a w tej paczce takiego nie ma.
• Samo zdanie alertu jest na razie po angielsku we wszystkich
  językach.

Jeden odczyt to jeden alert
• Plugin zgłaszający liczbę dla ośmiu kont naraz wysyłał osiem
  alertów na każdy adres. Teraz jest jeden: „8 accounts —
  Diamonds went above 0", po jednej linii na konto.
• Dwie różne liczby przekraczające próg na tym samym koncie w
  jednym odczycie docierają teraz obie.

Alert nie oznaczy kanału, a długi i tak dojdzie
• Posty na Discordzie wychodzą z wyłączonymi wzmiankami: nic
  wewnątrz alertu nie pinguje kanału, na który trafia.
• Długa lista kont jest przycinana, żeby się zmieściła, i
  kończy się „and 12 more", zamiast nie dojść przez długość.
  Dotyczy każdego rodzaju alertu.
```

## Español (es)

```
v1.29.0.0

Las alertas de métricas dicen qué ha pasado
• Una alerta se leía "ps99.diamonds at 2974993". Ahora se lee
  "Diamonds went above 0" y debajo "now 2,974,993": el nombre
  que le diste a la regla, lo que le pediste que vigilara, y
  números que se leen de un vistazo.
• Una regla sin nombre sigue volviendo al identificador de la
  métrica, como antes.
• RoRoRo sigue sin recoger ningún número. Lo informa un plugin
  que instalas, y nada en este paquete lo hace.
• La frase de la alerta está en inglés en todos los idiomas de
  momento.

Una lectura, una alerta
• Un plugin que informa un número de ocho cuentas a la vez
  enviaba ocho alertas a cada destino. Ahora es una:
  "8 accounts — Diamonds went above 0", una línea por cuenta.
• Dos números distintos que cruzan su límite en la misma
  cuenta y en la misma lectura ahora llegan los dos.

Una alerta no puede mencionar un canal, y una larga llega
igual
• Los mensajes de Discord salen con las menciones desactivadas:
  nada dentro de una alerta hace ping en el canal donde cae.
• Una lista larga de cuentas se recorta para caber y termina en
  "and 12 more", en lugar de fallar por longitud. Para
  cualquier tipo de alerta.
```

## What is deliberately not in this copy

**The five-second grouping window, and the delay it buys.** Grouping works by holding a metric
breach for five seconds from the first one, so every metric alert now lands up to five seconds
later than it did. That is a real cost and it is written into `docs/decisions.md` and the clan
release notes. It is not a what's-new line: the feature it sits on has a five-minute cooldown, and
"your alert is five seconds slower" is a sentence that makes a reader worry about something they
will never notice.

**The length envelopes — 250 characters on a Pushover title, 63 and 255 on a desktop toast, 992 on
a webhook body.** The block says a long list is trimmed and says what the trim looks like. The
numbers behind it are plumbing, and they differ per destination in ways a Store reader cannot act
on.

**`allowed_mentions`, `MetricBreachBatcher`, and every other name from the diff.** The block says
alerts can't ping a channel and that one read is one alert. Naming the mechanism in a public field
is changelog voice.

**Which plugin reports a number.** Same standard as v1.28, and it still holds even though the
ground has shifted: a real reporter now exists and was what produced the live run this release
fixes. It is not in `docs/store/plugins-catalog.json`, so a Store user cannot install one from
inside RoRoRo, and a what's-new block that implied otherwise would be a promise with someone
else's release date attached.

**The smoke harness, the scenario count, and the test counts.** Seventeen scenarios and 2,311 unit
tests are real work and invisible to a user. A what's-new block is not a changelog.
