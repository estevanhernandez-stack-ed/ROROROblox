# What's new in this version — v1.30.0.0

> Paste the matching fenced block into Partner Center → your app → **Store listings** → *What's new
> in this version* — the English block for the en listing, and each translated block for its own
> language listing. All seven live in this file rather than seven side files, because the workflow
> is paste-per-listing and one file is what you actually work from. (Since v1.28 the ten rows go in
> through the listing console instead; the blocks are the same either way.)
>
> **Written for someone on the v1.29 Store install.** v1.29 certified and published on 2026-09-16,
> so this is a single-version delta. No catching-up to do.
>
> **The headline is a BEHAVIOUR change and the block says so.** Someone whose alert was talking
> every few minutes will find it goes quiet. Burying that as "improved alert handling" would leave
> them thinking the feature broke. The block leads with what they will notice.
>
> **The plugin caveat carries forward, shortened again.** The first two sections only show up for
> someone running a plugin that reports numbers. Unlike v1.28 and v1.29, a reporter now IS
> installable from inside RoRoRo — Ur Score is in `docs/store/plugins-catalog.json` — so the line
> changed from "nothing in this package reports one" to naming where one comes from. That is the
> first time this claim could be made without borrowing someone else's release date.
>
> **Every translated block keeps the English-sentence caveat**, for the reason v1.29 set out:
> `WebhookPayload` is Core and composes in English, so a reader who is told alerts changed and then
> sees an English sentence has been told something true in a way that reads as a translation miss.
>
> **The plugin-install fix leads the third section in every language**, because it is the one item
> here that a reader can act on without a plugin: anyone who tried to install one and gave up has a
> reason to try again.
>
> Product nouns stay English throughout: RoRoRo, Discord, Pushover, ntfy, plugin, webhook. So do the
> quoted alert strings, because they are what the app actually prints.
>
> Sources: `docs/store/release-notes-1.30.0.0.md`, the 2026-09-20 entry in `docs/decisions.md`,
> PRs #215, #216 and #218.

---

## English

```
v1.30.0.0

Alerts fire once, when something changes
• A rule that stayed true used to alert again every few
  minutes, for as long as it stayed true. It now alerts once,
  when the number crosses the line, and goes quiet until it
  crosses back.
• If a rule of yours was talking a lot, it will go quiet. That
  is the fix, not a fault.
• A rule needs two readings before it can say anything, and a
  restart does not re-announce a problem that was already
  happening.

Ask to be told when it comes right again
• Tick "Also tell me when it comes right again" on a rule and
  a second alert arrives when the number comes back over its
  line.
• Off unless you tick it, and it goes where that rule's alerts
  already go.

Plugins install on a slow connection
• Installing a plugin gave up after 100 seconds, so anyone
  whose connection could not pull the whole file in that time
  could never install one. It now has ten minutes, and says
  what to try if it still runs out.
• An alert about a number that belongs to no single account
  now leads with the rule's own name instead of a blank.
```

## Français (fr)

```
v1.30.0.0

Les alertes se déclenchent une fois, au changement
• Une règle qui restait vraie vous alertait de nouveau toutes
  les quelques minutes, tant qu'elle restait vraie. Elle
  alerte maintenant une seule fois, quand le nombre franchit
  le seuil, puis se tait jusqu'au franchissement inverse.
• Si une de vos règles parlait beaucoup, elle va se taire.
  C'est le correctif, pas une panne.
• Une règle a besoin de deux lectures avant de pouvoir dire
  quoi que ce soit, et un redémarrage ne réannonce pas un
  problème déjà en cours.

Demandez qu'on vous dise quand tout rentre dans l'ordre
• Cochez « Also tell me when it comes right again » sur une
  règle et une seconde alerte arrive quand le nombre repasse
  son seuil.
• Désactivé sauf si vous le cochez, et destiné aux mêmes
  endroits que les alertes de cette règle.
• La phrase de l'alerte reste en anglais dans toutes les
  langues pour l'instant.

Les plugins s'installent sur une connexion lente
• L'installation d'un plugin abandonnait après 100 secondes :
  quiconque ne pouvait pas télécharger le fichier entier dans
  ce délai n'y arrivait jamais. Elle dispose maintenant de dix
  minutes, et dit quoi essayer si le délai est dépassé.
• Une alerte portant sur un nombre qui n'appartient à aucun
  compte commence désormais par le nom de la règle au lieu
  d'un blanc.
```

## Deutsch (de)

```
v1.30.0.0

Benachrichtigungen kommen einmal, beim Wechsel
• Eine Regel, die wahr blieb, meldete sich alle paar Minuten
  erneut, solange sie wahr blieb. Jetzt meldet sie sich
  einmal, wenn die Zahl die Grenze überschreitet, und bleibt
  still, bis sie zurückgeht.
• Wenn eine deiner Regeln viel geredet hat, wird sie still.
  Das ist die Korrektur, kein Fehler.
• Eine Regel braucht zwei Messwerte, bevor sie etwas sagen
  kann, und ein Neustart meldet ein bereits laufendes Problem
  nicht erneut.

Lass dir sagen, wenn es wieder in Ordnung ist
• Setze bei einer Regel das Häkchen „Also tell me when it
  comes right again", und eine zweite Benachrichtigung kommt,
  sobald die Zahl ihre Grenze wieder überschreitet.
• Aus, bis du es setzt, und sie geht dorthin, wohin die
  Benachrichtigungen dieser Regel ohnehin gehen.
• Der Satz der Benachrichtigung ist vorerst in jeder Sprache
  englisch.

Plugins lassen sich mit langsamer Verbindung installieren
• Die Installation eines Plugins gab nach 100 Sekunden auf:
  wer die ganze Datei in dieser Zeit nicht laden konnte, schaffte
  es nie. Jetzt sind es zehn Minuten, und sie sagt, was du
  versuchen kannst, wenn die Zeit doch nicht reicht.
• Eine Benachrichtigung über eine Zahl, die zu keinem Konto
  gehört, beginnt jetzt mit dem Namen der Regel statt mit
  einer Lücke.
```

## Русский (ru)

```
v1.30.0.0

Оповещения приходят один раз — в момент изменения
• Правило, остававшееся верным, оповещало заново каждые
  несколько минут, пока оставалось верным. Теперь оно
  оповещает один раз, когда число пересекает границу, и
  молчит до обратного пересечения.
• Если какое-то ваше правило говорило много, оно замолчит.
  Это и есть исправление, а не поломка.
• Правилу нужны два показания, прежде чем оно сможет что-то
  сказать, а перезапуск не объявляет заново уже идущую
  проблему.

Попросите сообщить, когда всё вернётся в норму
• Отметьте у правила «Also tell me when it comes right again»,
  и второе оповещение придёт, когда число вернётся за свою
  границу.
• Выключено, пока вы не отметите, и уходит туда же, куда уже
  уходят оповещения этого правила.
• Сама фраза оповещения пока на английском во всех языках.

Plugin устанавливается и на медленном соединении
• Установка plugin сдавалась через 100 секунд: тот, чьё
  соединение не успевало вытянуть весь файл за это время, не
  мог установить его никогда. Теперь есть десять минут, и
  сообщение подскажет, что делать, если и их не хватило.
• Оповещение о числе, которое не принадлежит ни одному
  аккаунту, теперь начинается с названия правила, а не с
  пустого места.
```

## Português (Brasil) (pt-BR)

```
v1.30.0.0

Os alertas disparam uma vez, quando algo muda
• Uma regra que continuava verdadeira alertava de novo a cada
  poucos minutos, enquanto continuasse verdadeira. Agora ela
  alerta uma vez, quando o número cruza o limite, e fica
  quieta até cruzar de volta.
• Se alguma regra sua falava muito, ela vai ficar quieta. É a
  correção, não um defeito.
• Uma regra precisa de duas leituras antes de poder dizer
  qualquer coisa, e reiniciar não anuncia de novo um problema
  que já estava acontecendo.

Peça para avisarem quando voltar ao normal
• Marque "Also tell me when it comes right again" numa regra e
  um segundo alerta chega quando o número volta a cruzar o
  limite.
• Desligado até você marcar, e vai para os mesmos lugares que
  os alertas dessa regra já vão.
• A frase do alerta ainda sai em inglês em todos os idiomas.

Plugin instala em conexão lenta
• A instalação de um plugin desistia depois de 100 segundos:
  quem não conseguia puxar o arquivo inteiro nesse tempo nunca
  instalava. Agora são dez minutos, e ele diz o que tentar se
  ainda assim estourar.
• Um alerta sobre um número que não pertence a nenhuma conta
  agora começa com o nome da regra, em vez de um espaço vazio.
```

## Polski (pl)

```
v1.30.0.0

Alerty odzywają się raz, przy zmianie
• Reguła, która pozostawała prawdziwa, alarmowała ponownie co
  kilka minut, dopóki pozostawała prawdziwa. Teraz alarmuje
  raz, gdy liczba przekracza próg, i milknie aż do powrotu.
• Jeśli któraś twoja reguła dużo mówiła, teraz zamilknie. To
  jest poprawka, nie awaria.
• Reguła potrzebuje dwóch odczytów, zanim cokolwiek powie, a
  restart nie ogłasza ponownie problemu, który już trwał.

Poproś, żeby dać ci znać, gdy wróci do normy
• Zaznacz przy regule „Also tell me when it comes right
  again", a drugi alert przyjdzie, gdy liczba wróci za swój
  próg.
• Wyłączone, dopóki nie zaznaczysz, i idzie tam, gdzie i tak
  idą alerty tej reguły.
• Samo zdanie alertu jest na razie po angielsku we wszystkich
  językach.

Plugin instaluje się na wolnym łączu
• Instalacja plugina poddawała się po 100 sekundach: kto nie
  zdążył pobrać całego pliku w tym czasie, nie zainstalował go
  nigdy. Teraz jest dziesięć minut, a jeśli i tak się skończą,
  komunikat podpowie, co zrobić.
• Alert o liczbie, która nie należy do żadnego konta, zaczyna
  się teraz od nazwy reguły, a nie od pustego miejsca.
```

## Español (es)

```
v1.30.0.0

Las alertas saltan una vez, cuando algo cambia
• Una regla que seguía siendo cierta volvía a avisar cada
  pocos minutos, mientras siguiera siéndolo. Ahora avisa una
  vez, cuando el número cruza el límite, y se calla hasta que
  lo cruza de vuelta.
• Si alguna regla tuya hablaba mucho, se va a callar. Es el
  arreglo, no una avería.
• Una regla necesita dos lecturas antes de poder decir nada, y
  reiniciar no vuelve a anunciar un problema que ya estaba
  pasando.

Pide que te avisen cuando vuelva a estar bien
• Marca "Also tell me when it comes right again" en una regla
  y llega una segunda alerta cuando el número vuelve a cruzar
  su límite.
• Desactivado hasta que lo marques, y va a los mismos sitios a
  los que ya van las alertas de esa regla.
• La frase de la alerta está en inglés en todos los idiomas de
  momento.

Los plugins se instalan con conexión lenta
• Instalar un plugin se rendía a los 100 segundos: quien no
  pudiera descargar el archivo entero en ese tiempo no lo
  instalaba nunca. Ahora tiene diez minutos, y dice qué probar
  si aun así se acaba.
• Una alerta sobre un número que no pertenece a ninguna cuenta
  empieza ahora por el nombre de la regla, en vez de por un
  hueco.
```

## What is deliberately not in this copy

**The recovery alert's opt-in field name, `tellMeWhenItRecovers`.** The block says there is a tick
and what ticking it does. The field in the rules file is for someone hand-editing it, which is not
a Store reader.

**Why the restart is quiet.** The notes explain that the history a rule compares against lives in
memory and the alternative — every unhappy rule re-announcing on every launch — is worse. The
what's-new block states the behaviour without the reasoning, because a Store reader needs to know
what to expect, not why it was chosen.

**The batcher's group key, the shared title builder, and every other name from the diff.** Naming
a mechanism in a public field is changelog voice.

**The test counts.** 2,329 unit tests and 27 harness tests are real work and invisible to a user.

**The clan-facing framing.** The release notes say "a stat sitting under its floor for three hours
could buzz your phone thirty times" because the clan will recognise it. A Store reader has no such
history with this app, and the number reads as an admission rather than a fix.
