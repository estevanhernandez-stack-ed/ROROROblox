# Store listing — Deutsch (de)

> Paste-ready für Partner Center → Store listings → **Deutsch**. Refreshed 2026-09-23 for v1.31.0.0 (feature entries for Known Roblox issues and per-account frame-rate caps, 18 → 20; privacy sentence; tools-window page list). Refreshed 2026-09-08 for v1.27.0.0 (what's-new + language claims). Drafted 2026-09-05 from
> `listing-copy.md`, refreshed for v1.26.0.0. Product nouns stay in English (RoRoRo, Squad
> Launch, Friend Follow, Pushover, ntfy); the interface is now localized to six languages, so
> the old English-only caveat is gone. Register: du (Gaming-Store-Norm).

## Short description (≤200 chars)

```
Multi-Launcher für Windows, jetzt in 6 Sprachen: mehrere Roblox-Clients nebeneinander, jeder mit eigenem Konto. Verschlüsselter Tresor, Squad Launch, RAM-Wächter, Handy-Alarme, Live-Status, Themes.
```

## Long description

```
Multi-Launcher für Windows.

RoRoRo ist ein Windows-Launcher, der mehrere Roblox-Clients gleichzeitig auf einem PC laufen lässt — jeder angemeldet mit einem anderen gespeicherten Konto, das dir gehört. Füge deine Konten einmal über Robloxs eigene Login-Seite hinzu und starte sie dann mit einem Klick: ins Standardspiel, auf einen gespeicherten privaten Server oder auf jeden eingefügten Spiellink.

Was du bekommst:
• Multi-Instanz mit einem Klick. RoRoRo hält den Roblox-Singleton-Mutex, sodass zusätzliche Clients sich öffnen, statt den ersten in den Vordergrund zu holen.
• Verschlüsselter Konten-Tresor (DPAPI). Gespeicherte Cookies werden mit der Windows-Datenschutz-API verschlüsselt und an dein Windows-Konto gebunden — eine auf einen anderen PC kopierte Tresordatei lässt sich nicht entschlüsseln. Konten zwischen deinen eigenen PCs bewegst du über einen bewussten, passphrasengeschützten Export.
• Live-Status für jedes Konto. Sieh, welches Konto in welchem Spiel ist, wer wie lange inaktiv ist, und setze FPS-Limits pro Konto, die halten.
• Squad Launch + Friend Follow. Schicke alle ausgewählten Konten in denselben privaten Server, folge einem Freund in seinen, oder lande mit deinen Konten gemeinsam auf einem öffentlichen Server.
• RAM-Wächter + Recycle. RoRoRo lernt, was ein Roblox-Client auf deiner Maschine wirklich an RAM kostet, und warnt, bevor er ausgeht. Ein Klick schließt einen schweren Client und bringt ihn zurück in denselben Server.
• Ein Werkzeugfenster. Spiele, Einstellungen, Verlauf, Diagnose, Plugins, Bekannte Roblox-Probleme und Über sind Seiten eines Fensters neben deinen Konten — mit Tastenkürzeln überall; F1 zeigt die Liste.
• Themes. Vier eingebaute, darunter eines, das Bedeutung nie allein über Farbe transportiert, plus ein Editor für eigene Themes aus zehn Farben, teilbar als Datei.
• Optionale Alarme — Desktop, Discord oder Handy. Leite jeden Alarm an jede Kombination: Desktop-Benachrichtigungen, einen selbst erstellten Discord-Webhook oder dein Handy über Pushover oder ntfy. Verbindungsabbrüche, RAM-Warnungen, Recycle-Abschlüsse und ein „Alles gut“-Zeichen alle zwei Stunden. Eine frische Installation macht keinerlei Alarm-Aufrufe — nichts sendet, bevor du etwas einrichtest.
• Infobereich mit statusfarbenem Symbol — der Multi-Instanz-Status auf einen Blick; Doppelklick startet dein Hauptkonto.
• Plugin-System. Optionale Plugins laufen als getrennte Prozesse und haben keine Berechtigungen, bis du sie einzeln erteilst.
• Auto-Update über Velopack. Eine entfernte Konfiguration verfolgt die bekannte Roblox-Version und den Mutex-Namen, damit eine Umbenennung auf Roblox-Seite dich nicht lange ausbremst.
• Barrierefreiheit, gemessen. Jedes Steuerelement meldet seinen Namen an Hilfstechnologien, und der Kontrast wird in jedem Theme an gerenderten Pixeln geprüft.
• Spricht deine Sprache. Die gesamte App — jedes Menü, jede Einstellung, jeder Tooltip und jedes Fenster sowie die Meldungen, die RoRoRo während der Nutzung schreibt — ist auf Französisch, Deutsch, Russisch, Portugiesisch (Brasilien), Polnisch und Spanisch übersetzt, je nach Windows-Sprache oder deiner Wahl in den Einstellungen. Der Wechsel wirkt sofort, und die Auswahl bietet nur vollständig übersetzte Sprachen an, damit du nie auf einem halb englischen Bildschirm landest.

Datenschutz und Sicherheit:
Dein Roblox-Passwort sieht RoRoRo nie. Die Anmeldung passiert vollständig auf Robloxs eigener Seite, eingebettet in einen Microsoft-Edge-WebView2-Rahmen — dasselbe HTML, dieselbe HTTPS-Verbindung wie in deinem Browser. RoRoRo erfasst nur das Sitzungscookie, das Roblox nach erfolgreicher Anmeldung setzt, und verschlüsselt es vor dem Schreiben auf die Festplatte. Keine Telemetrie. Keine Analyse. Nichts über dich verlässt deine Maschine außer den Roblox-Aufrufen beim Start — denselben, die Roblox.com aus deinem Browser macht — und, nur wenn du sie selbst einrichtest, Alarmen an deinen eigenen Discord-Webhook oder den gewählten Push-Dienst (Pushover oder ntfy). RoRoRo lädt außerdem seine eigenen signierten Dateien aus seinen GitHub-Releases herunter — Updates, die Roblox-Kompatibilitätseinstellungen und die Liste bekannter Roblox-Probleme — und sendet dafür nichts über dich.

Wichtig: Marken- und Zugehörigkeitshinweis.
„Roblox" und das Roblox-Logo sind Marken der Roblox Corporation. RoRoRo ist ein unabhängiges Drittanbieter-Werkzeug, nicht mit der Roblox Corporation verbunden, von ihr befürwortet oder gesponsert. Der Markenbegriff wird ausschließlich zur Beschreibung der Kompatibilität mit der Roblox-Plattform verwendet. RoRoRo startet den offiziellen Roblox-Client unverändert — keine Injektion, keine Hooks, keine Veränderung des Roblox-Prozesses; es hält lediglich vor dem Start einen benannten Windows-Mutex, damit weitere Client-Instanzen die Singleton-Prüfung als bereits vergeben sehen.

Ein Produkt von 626 Labs.
```

## Product features (20 entries, ≤200 chars each)

```
Multi-Instanz-Launcher für Roblox auf Windows, mit einem Klick
DPAPI-verschlüsselter Konten-Tresor mit passphrasengeschütztem Export
Live-Status je Konto — aktuelles Spiel, Inaktivitätszeit und FPS-Limits pro Konto
FPS-Limits pro Konto — je Client angehoben, gesenkt oder ganz aufgehoben
RAM-Wächter, der die echten Kosten jedes Clients lernt, plus Ein-Klick-Recycle zurück in denselben Server
Squad Launch und Friend Follow — derselbe private Server, oder gemeinsam auf einen öffentlichen
Beitritt per Link von jeder roblox.com-URL, mit gespeicherten privaten Servern je Konto
Ein Werkzeugfenster für Spiele, Einstellungen, Verlauf und mehr, mit Tastenkürzeln überall
Vier eingebaute Themes plus ein Editor für eigene
Optionale Discord-Alarme an einen selbst erstellten Webhook
Infobereich mit statusfarbenem Symbol und Doppelklick-Start des Hauptkontos
Plugin-System mit Zustimmung je Berechtigung und Isolation in eigenen Prozessen
Auto-Update, das weiterläuft, wenn Roblox sich darunter ändert
Start mit Windows, wenn du willst — ein Schalter, und Windows' eigene Autostart-Liste behält die Kontrolle
Discord Join startet RoRoRo auch geschlossen — und fragt immer, bevor irgendetwas startet
Handy-Alarme über Pushover oder ntfy — ein Alt fliegt raus und dein Handy vibriert, auch wenn Discord geschlossen ist
Lebenszeichen — alle zwei Stunden ein „Alles gut“, solange deine Konten laufen, also heißt Stille: etwas stimmt nicht
Alarme fächern auf — Desktop, Discord-Kanäle und Handy in jeder Kombination, je Alarm
In sechs Sprachen — die ganze App, Bildschirme wie Meldungen: Französisch, Deutsch, Russisch, Portugiesisch (Brasilien), Polnisch, Spanisch. Sofortiger Wechsel, gemäß Windows oder deiner Wahl
Bekannte Roblox-Probleme — Probleme auf Roblox-Seite, was zu tun ist und welche RoRoRo-Funktion hilft, aktuell gehalten ohne App-Update
```

## What's new in this version (v1.31.0.0, ≤1500 chars)

```
v1.31.0.0

Bekannte Roblox-Probleme
• Eine neue Seite listet Probleme in Roblox selbst: was
  passiert, was du tun kannst und welche RoRoRo-Funktion
  hilft, mit einer Schaltfläche, die dich direkt hinführt.
  Öffne sie über das Menü Werkzeuge oder drück Strg+7 im
  Werkzeugfenster.
• Die ersten zwei: das Roblox-Fenster, das beim Verschieben
  oder bei einer Größenänderung einfriert, und Roblox, das mit
  der Zeit immer mehr Arbeitsspeicher braucht.

Ein Hinweis für die ernsten Fälle
• Ein ernstes Problem zeigt einen einzigen Hinweis oben im
  Hauptfenster. Schließ ihn, und er bleibt zu; nur ein neues
  Problem bringt ihn zurück.
• Beim ersten Öffnen dieser Version siehst du einen, zum
  einfrierenden Fenster. Das ist die Funktion bei der Arbeit,
  kein neues Problem.

Die Liste bleibt von selbst aktuell
• RoRoRo sucht beim Start und alle vier Stunden nach einer
  neuen Liste, am selben Ort, von dem es schon seine
  Roblox-Kompatibilitätseinstellungen holt. Die Liste ist
  signiert, und nichts über dich wird gesendet.
• Die Seite ist in deiner Sprache; die Problembeschreibungen
  sind vorerst auf Englisch.
```

## Copyright (single line)

```
© 2026 626 Labs LLC. Alle Rechte vorbehalten. „Roblox" ist eine Marke der Roblox Corporation. RoRoRo ist nicht mit der Roblox Corporation verbunden, von ihr befürwortet oder gesponsert.
```

## Trademark info

```
„Roblox" und das Roblox-Logo sind Marken der Roblox Corporation. RORORO ist ein unabhängiges Drittanbieter-Werkzeug, nicht mit der Roblox Corporation verbunden, von ihr befürwortet oder gesponsert. Der Markenbegriff wird ausschließlich zur Beschreibung der Kompatibilität mit der Roblox-Plattform verwendet. RORORO startet den offiziellen Roblox-Client unverändert.
```

## Screenshot captions (one per image, in file-number order)

Partner Center takes one caption per screenshot. These are in the order the files sort:
`01-accounts-running.png`, `02-themes.png`, `03-about.png`, `04-games.png`, `05-diagnostics.png`, `06-history.png`, `07-plugins.png`, `08-theme-builder.png`, `09-compact.png`, `10-multi-instance.png`.

```
Drei Konten laufen gleichzeitig, jedes mit eigenem Speicherverbrauch und eigener Schaltfläche zum Stoppen. Cookies werden pro Benutzer mit Windows DPAPI verschlüsselt und verlassen den Rechner nie.
Vier eingebaute Themes. Flatline transportiert überhaupt keine Bedeutung über Farbe, sodass bei Farbenblindheit, schlechtem Panel oder direkter Sonne nichts verloren geht.
Multi-Instanz-Launcher für Windows. Hält den Roblox-Singleton-Mutex, damit der nächste Client startet, statt mit dem ersten zu kämpfen. Eine saubere Neuimplementierung, kein Fork.
Speichere die Spiele und privaten Server, die du wirklich spielst, und wähle vor dem Start pro Konto ein anderes.
Die Diagnose zeigt, was RoRoRo gerade sieht: Versionen, Zustand und wo die Logs liegen — für den Tag, an dem du etwas melden musst.
Jeder Start wird aufgezeichnet, du siehst also, welches Konto was gespielt hat und wie lange.
Plugins laufen als eigene Prozesse und fragen vorher. Du erteilst jede Berechtigung namentlich und kannst sie später widerrufen.
Baue ein Theme aus zehn Farben, und es erscheint in der Auswahl. Es ist eine JSON-Datei, du kannst sie also weitergeben.
Der Kompaktmodus zeigt nur, was läuft. Hefte ihn in eine Ecke des Bildschirms und spiel weiter.
Acht Roblox-Clients, acht Konten, ein PC. Jeder Fenstertitel trägt das Konto, das darin angemeldet ist — du weißt immer, welches welches ist.
```

## Keywords (max 7, 40 chars each, 21 words total — one per box)

```
roblox
multi-instanz
multi-account
launcher
kontoverwaltung
zweitaccounts
multibox
```
