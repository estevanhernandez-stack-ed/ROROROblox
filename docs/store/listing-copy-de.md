# Store listing — Deutsch (de)

> Paste-ready für Partner Center → Store listings → **Deutsch**. Drafted 2026-09-05 from
> `listing-copy.md` (v1.25.0.0 state). Product nouns stay in English (RoRoRo, Squad Launch,
> Friend Follow, Pushover, ntfy); the long description says plainly that the app's interface
> is in English. Register: du (Gaming-Store-Norm).

## Short description (≤200 chars)

```
Multi-Launcher für Windows: mehrere Roblox-Clients nebeneinander, jeder mit eigenem Konto. Verschlüsselter Tresor, Squad Launch, RAM-Wächter, Handy-Alarme, Live-Status, Themes, Auto-Update.
```

## Long description

```
Multi-Launcher für Windows.

RoRoRo ist ein Windows-Launcher, der mehrere Roblox-Clients gleichzeitig auf einem PC laufen lässt — jeder angemeldet mit einem anderen gespeicherten Konto, das dir gehört. Füge deine Konten einmal über Robloxs eigene Login-Seite hinzu und starte sie dann mit einem Klick: ins Standardspiel, auf einen gespeicherten privaten Server oder auf jeden eingefügten Spiellink.

Hinweis: Die Oberfläche der App ist derzeit auf Englisch.

Was du bekommst:
• Multi-Instanz mit einem Klick. RoRoRo hält den Roblox-Singleton-Mutex, sodass zusätzliche Clients sich öffnen, statt den ersten in den Vordergrund zu holen.
• Verschlüsselter Konten-Tresor (DPAPI). Gespeicherte Cookies werden mit der Windows-Datenschutz-API verschlüsselt und an dein Windows-Konto gebunden — eine auf einen anderen PC kopierte Tresordatei lässt sich nicht entschlüsseln. Konten zwischen deinen eigenen PCs bewegst du über einen bewussten, passphrasengeschützten Export.
• Live-Status für jedes Konto. Sieh, welches Konto in welchem Spiel ist, wer wie lange inaktiv ist, und setze FPS-Limits pro Konto, die halten.
• Squad Launch + Friend Follow. Schicke alle ausgewählten Konten in denselben privaten Server, folge einem Freund in seinen, oder lande mit deinen Konten gemeinsam auf einem öffentlichen Server.
• RAM-Wächter + Recycle. RoRoRo lernt, was ein Roblox-Client auf deiner Maschine wirklich an RAM kostet, und warnt, bevor er ausgeht. Ein Klick schließt einen schweren Client und bringt ihn zurück in denselben Server.
• Ein Werkzeugfenster. Spiele, Einstellungen, Verlauf, Diagnose, Plugins und Über sind Seiten eines Fensters neben deinen Konten — mit Tastenkürzeln überall; F1 zeigt die Liste.
• Themes. Vier eingebaute, darunter eines, das Bedeutung nie allein über Farbe transportiert, plus ein Editor für eigene Themes aus zehn Farben, teilbar als Datei.
• Optionale Alarme — Desktop, Discord oder Handy. Leite jeden Alarm an jede Kombination: Desktop-Benachrichtigungen, einen selbst erstellten Discord-Webhook oder dein Handy über Pushover oder ntfy. Verbindungsabbrüche, RAM-Warnungen, Recycle-Abschlüsse und ein „Alles gut“-Zeichen alle zwei Stunden. Eine frische Installation macht keinerlei Alarm-Aufrufe — nichts sendet, bevor du etwas einrichtest.
• Infobereich mit statusfarbenem Symbol — der Multi-Instanz-Status auf einen Blick; Doppelklick startet dein Hauptkonto.
• Plugin-System. Optionale Plugins laufen als getrennte Prozesse und haben keine Berechtigungen, bis du sie einzeln erteilst.
• Auto-Update über Velopack. Eine entfernte Konfiguration verfolgt die bekannte Roblox-Version und den Mutex-Namen, damit eine Umbenennung auf Roblox-Seite dich nicht lange ausbremst.
• Barrierefreiheit, gemessen. Jedes Steuerelement meldet seinen Namen an Hilfstechnologien, und der Kontrast wird in jedem Theme an gerenderten Pixeln geprüft.

Datenschutz und Sicherheit:
Dein Roblox-Passwort sieht RoRoRo nie. Die Anmeldung passiert vollständig auf Robloxs eigener Seite, eingebettet in einen Microsoft-Edge-WebView2-Rahmen — dasselbe HTML, dieselbe HTTPS-Verbindung wie in deinem Browser. RoRoRo erfasst nur das Sitzungscookie, das Roblox nach erfolgreicher Anmeldung setzt, und verschlüsselt es vor dem Schreiben auf die Festplatte. Keine Telemetrie. Keine Analyse. Nichts verlässt deine Maschine außer den Roblox-Aufrufen beim Start — denselben, die Roblox.com aus deinem Browser macht — und, nur wenn du sie selbst einrichtest, Alarmen an deinen eigenen Discord-Webhook oder den gewählten Push-Dienst (Pushover oder ntfy).

Wichtig: Marken- und Zugehörigkeitshinweis.
„Roblox" und das Roblox-Logo sind Marken der Roblox Corporation. RoRoRo ist ein unabhängiges Drittanbieter-Werkzeug, nicht mit der Roblox Corporation verbunden, von ihr befürwortet oder gesponsert. Der Markenbegriff wird ausschließlich zur Beschreibung der Kompatibilität mit der Roblox-Plattform verwendet. RoRoRo startet den offiziellen Roblox-Client unverändert — keine Injektion, keine Hooks, keine Veränderung des Roblox-Prozesses; es hält lediglich vor dem Start einen benannten Windows-Mutex, damit weitere Client-Instanzen die Singleton-Prüfung als bereits vergeben sehen.

Ein Produkt von 626 Labs.
```

## Product features (17 entries, ≤200 chars each)

```
Multi-Instanz-Launcher für Roblox auf Windows, mit einem Klick
DPAPI-verschlüsselter Konten-Tresor mit passphrasengeschütztem Export
Live-Status je Konto — aktuelles Spiel, Inaktivitätszeit und FPS-Limits pro Konto
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
Handy-Alarme über Pushover oder ntfy — ein Alt fliegt raus und dein Handy vibriert, auch ohne Discord
Alle zwei Stunden ein „Alles gut“-Zeichen, solange deine Konten laufen — Stille heißt: nachsehen
Alarme fächern auf — Desktop, Discord-Kanäle und Handy in jeder Kombination, je Alarm
```

## What's new in this version (v1.25.0.0, ≤1500 chars)

```
v1.25.0.0

Dein Handy kann jetzt vibrieren
• Settings > Alerts: Leite jeden Alarm über Pushover oder ntfy
  an dein Handy. Ein Alt fliegt raus — dein Handy weiß es, auch bei
  geschlossenem Discord. Einmalige Einrichtung, und Test my phone
  beweist, dass es funktioniert. Deine Schlüssel bleiben
  verschlüsselt auf deinem PC; gesendet wird nur, wenn ein Alarm
  ausgelöst wird, und nur an den Dienst, den du gewählt hast.

Alarme gehen überallhin, wo du Haken setzt
• Desktop, dein Discord-Kanal, der Clan-Kanal, dein Handy — jede
  Kombination je Alarm. Dein altes Routing wurde übernommen.

Zwei neue Alarme, standardmäßig aus
• Ein abgeschlossenes Recycle meldet die freigeräumte RAM-Menge.
  Statuszeichen melden alle zwei Stunden „4h up — 6 accounts in" —
  ein ausbleibendes Zeichen heißt: PC oder App sind gestorben —
  der eine Ausfall, den nichts direkt melden kann.

Roblox bleibt im Fenstermodus
• Hat ein Absturz oder Alt+Eingabe Roblox im Vollbild hinterlassen,
  räumt RoRoRo das vor jedem Start weg. Schalter in Settings > Startup, standardmäßig an.

Zurück zu einer älteren Version?
• Wenn du nach dem Einrichten des neuen Alarm-Routings zurückgehst,
  stelle das Routing dort neu ein — ältere Versionen überspringen
  unbekannte Optionen stillschweigend.
```

## Copyright (single line)

```
© 2026 626 Labs LLC. Alle Rechte vorbehalten. „Roblox" ist eine Marke der Roblox Corporation. RoRoRo ist nicht mit der Roblox Corporation verbunden, von ihr befürwortet oder gesponsert.
```

## Trademark info

```
„Roblox" und das Roblox-Logo sind Marken der Roblox Corporation. RORORO ist ein unabhängiges Drittanbieter-Werkzeug, nicht mit der Roblox Corporation verbunden, von ihr befürwortet oder gesponsert. Der Markenbegriff wird ausschließlich zur Beschreibung der Kompatibilität mit der Roblox-Plattform verwendet. RORORO startet den offiziellen Roblox-Client unverändert.
```
