# What's new in this version — v1.32.1.0

> Paste the matching fenced block into Partner Center → your app → **Store listings** → *What's new
> in this version*: the English block for the en listing, and each translated block for its own
> language listing. (Since v1.28 the ten rows go in through the listing console instead; the blocks
> are the same either way.)
>
> **Written for someone on the v1.32 Store install.** Single-version delta, and a small one: this
> release is a single fix.
>
> **The defect is not described, only the behaviour.** House rule for this field — it says what the
> switch does now, not that it used to be read once at startup. The clan-facing release notes carry
> the mechanism and the apology; a Store field carries the outcome.
>
> **UI names are the app's own translations**, read from `Strings.<culture>.resx` rather than
> re-translated: `SettingsPage_WatchMemoryWhileAccountsAre_2`, `SettingsPage_MemoryToKeepFreeMb`,
> `SettingsPage_WarnWhenOneAccountPasses_2` and `SettingsPage_WarnThisFarAheadMinutes`. Each block
> quotes its own language's label exactly as the app renders it, including the trailing period on
> the checkbox label.
>
> Register follows each existing sheet: **vous** in French, **вы** in Russian, **du** in German,
> informal elsewhere. Product nouns stay English: RoRoRo, Roblox.
>
> **Verifier run: DONE at `442b521`, both shapes, all six pairs judged.** Three approve (fr, de,
> pt-br) and three revise (ru, pl, es) — and all three revisions are **the same rule-3 false positive
> v1.32 drew twelve times**: "in-app UI strings must remain untranslated in English". The rule
> assumes an English-only interface. RoRoRo's interface has been localized into exactly these six
> languages since v1.26, so a Spanish listing that quotes "Memory to keep free (MB)" would name a
> label no Spanish user can find, while "Memoria que mantener libre (MB)" is what their app actually
> says. **Overruled on the same ground as v1.32, and the suggested fixes are deliberately not
> applied.**
>
> The split is itself evidence it is an artifact rather than a finding: the identical construction
> was approved in French, German and Portuguese and flagged in Russian, Polish and Spanish. A real
> rule violation would not land on half the set.
>
> **No other issue was raised in any language** — no mistranslation, no register slip, no cap
> problem. Details in `submission-packet-1.32.1.0.md` section 3.
>
> Sources: `docs/store/release-notes-1.32.1.0.md`, PR #225.

---

## English

```
v1.32.1.0

Memory watching responds right away

• Turn "Watch memory while accounts are running." off and
  RoRoRo stops watching straight away. Turn it back on and it
  starts again. Neither one waits for a restart.
• The numbers under it are live too. Change "Memory to keep
  free (MB)", "Warn when one account passes this many
  megabytes" or "Warn this far ahead (minutes)" and the very
  next check uses the new figure.
• Turn watching off and the memory readouts and the tray
  warning clear, instead of staying on screen showing the last
  thing they saw.

Nothing else moved. Accounts, themes, plugins, alerts, history
and settings all carry over untouched.
```

## Deutsch

```
v1.32.1.0

Die Speicherüberwachung reagiert sofort

• Schalte „Speicher überwachen, während Konten laufen.“ aus,
  und RoRoRo hört sofort auf zu überwachen. Schalte es wieder
  ein, und es überwacht wieder. Kein Neustart nötig.
• Die Zahlen darunter gelten ebenfalls sofort. Ändere
  „Freizuhaltender Speicher (MB)“, „Warnen, wenn ein Konto so
  viele Megabyte überschreitet“ oder „So weit im Voraus warnen
  (Minuten)“, und schon die nächste Prüfung nutzt den neuen
  Wert.
• Schaltest du die Überwachung aus, werden die Speicheranzeigen
  und die Warnung im Infobereich zurückgesetzt, statt mit ihrem
  letzten Stand stehen zu bleiben.

Sonst hat sich nichts geändert. Konten, Themes, Plugins,
Alarme, Verlauf und Einstellungen bleiben unverändert.
```

## Français

```
v1.32.1.0

La surveillance de la mémoire réagit immédiatement

• Décochez « Surveiller la mémoire pendant que les comptes sont
  en cours d'exécution. » et RoRoRo arrête aussitôt de
  surveiller. Recochez-la et il recommence. Aucun redémarrage
  dans un sens comme dans l'autre.
• Les valeurs en dessous s'appliquent aussi tout de suite.
  Modifiez « Mémoire à garder libre (Mo) », « Avertir quand un
  compte dépasse ce nombre de mégaoctets » ou « Avertir ce laps
  de temps à l'avance (minutes) » : la vérification suivante
  utilise déjà la nouvelle valeur.
• Quand vous désactivez la surveillance, les indicateurs de
  mémoire et l'avertissement dans la zone de notification
  s'effacent au lieu de rester figés sur leur dernier état.

Rien d'autre n'a bougé. Comptes, thèmes, plugins, alertes,
historique et paramètres sont conservés tels quels.
```

## Русский

```
v1.32.1.0

Слежение за памятью срабатывает сразу

• Выключите «Следить за памятью, пока запущены аккаунты.» —
  и RoRoRo сразу перестаёт следить. Включите обратно — и
  слежение возобновляется. Перезапуск не нужен ни в том, ни в
  другом случае.
• Числа под этой настройкой тоже действуют сразу. Измените
  «Сколько памяти оставлять свободной (МБ)», «Предупреждать,
  когда один аккаунт превышает столько мегабайт» или
  «Предупреждать заранее за (минуты)» — и уже следующая
  проверка возьмёт новое значение.
• Когда слежение выключено, показатели памяти и предупреждение
  в трее очищаются, а не остаются с последним, что видели.

Больше ничего не изменилось. Аккаунты, темы, плагины,
оповещения, история и настройки сохраняются без изменений.
```

## Português (Brasil)

```
v1.32.1.0

O monitoramento de memória responde na hora

• Desmarque "Monitorar a memória enquanto as contas estão em
  execução." e o RoRoRo para de monitorar na mesma hora. Marque
  de novo e ele volta a monitorar. Nenhum dos dois espera você
  reiniciar o app.
• Os números abaixo também valem na hora. Mude "Memória a
  manter livre (MB)", "Avisar quando uma conta passar desta
  quantidade de megabytes" ou "Avisar com esta antecedência
  (minutos)" e a próxima verificação já usa o valor novo.
• Ao desligar o monitoramento, os indicadores de memória e o
  aviso na área de notificação são limpos, em vez de ficarem
  parados no último estado.

Nada mais mudou. Contas, temas, plugins, alertas, histórico e
configurações continuam iguais.
```

## Polski

```
v1.32.1.0

Obserwowanie pamięci działa od razu

• Wyłącz „Obserwuj pamięć, gdy konta są uruchomione.”, a RoRoRo
  natychmiast przestaje obserwować. Włącz z powrotem — i znowu
  obserwuje. W żadną stronę nie trzeba uruchamiać aplikacji
  ponownie.
• Liczby poniżej też działają od razu. Zmień „Pamięć do
  zachowania wolnej (MB)”, „Ostrzegaj, gdy jedno konto
  przekroczy tyle megabajtów” albo „Ostrzegaj z tym
  wyprzedzeniem (minuty)” — już następne sprawdzenie użyje nowej
  wartości.
• Po wyłączeniu obserwowania wskaźniki pamięci i ostrzeżenie w
  zasobniku są czyszczone, zamiast zostawać z ostatnim
  odczytem.

Nic poza tym się nie zmieniło. Konta, motywy, wtyczki, alerty,
historia i ustawienia pozostają bez zmian.
```

## Español

```
v1.32.1.0

La vigilancia de memoria responde al momento

• Desmarca "Vigilar la memoria mientras las cuentas están en
  ejecución." y RoRoRo deja de vigilar al instante. Vuelve a
  marcarla y empieza otra vez. Ninguna de las dos cosas espera
  a que reinicies.
• Los números de debajo también se aplican al momento. Cambia
  "Memoria que mantener libre (MB)", "Avisar cuando una cuenta
  supere estos megabytes" o "Avisar con esta antelación
  (minutos)" y la siguiente comprobación ya usa el valor nuevo.
• Al desactivar la vigilancia, los indicadores de memoria y el
  aviso del área de notificación se limpian en lugar de
  quedarse con lo último que vieron.

No ha cambiado nada más. Cuentas, temas, plugins, alertas,
historial y ajustes se mantienen igual.
```
