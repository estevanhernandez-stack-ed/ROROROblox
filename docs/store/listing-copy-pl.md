# Store listing — Polski (pl)

> Paste-ready dla Partner Center → Store listings → **Polski**. Drafted 2026-09-05 from
> `listing-copy.md` (v1.25.0.0 state). Product nouns stay in English (RoRoRo, Squad Launch,
> Friend Follow, Pushover, ntfy); the long description says plainly that the app's interface
> is in English. Register: ty (norma dla gier).

## Short description (≤200 chars)

```
Multi-launcher dla Windows: kilka klientów Roblox obok siebie, każdy na innym koncie. Szyfrowany sejf, Squad Launch, strażnik pamięci, alerty na telefon, status na żywo, motywy, auto-update.
```

## Long description

```
Multi-launcher dla Windows.

RoRoRo to launcher dla Windows, który uruchamia kilka klientów Roblox jednocześnie na jednym PC — każdy zalogowany na inne zapisane konto, które należy do ciebie. Dodaj konta raz przez własną stronę logowania Roblox, a potem uruchamiaj je jednym kliknięciem — do domyślnej gry, na zapisany prywatny serwer albo z dowolnego wklejonego linku do gry.

Uwaga: interfejs aplikacji jest na razie po angielsku.

Co dostajesz:
• Multi-instancja jednym kliknięciem. RoRoRo trzyma mutex singleton Roblox, więc kolejne klienty się otwierają, zamiast wyciągać pierwszy na wierzch.
• Szyfrowany sejf kont (DPAPI). Zapisane pliki cookie są szyfrowane interfejsem Windows Data Protection API i związane z twoim kontem Windows — plik skopiowany na inny PC się nie odszyfruje. Przenoszenie kont między własnymi komputerami to świadomy eksport chroniony frazą-hasłem.
• Status każdego konta na żywo. Widzisz, które konto jest w której grze, kto stoi bezczynnie i jak długo, a limit FPS ustawiasz osobno dla każdego konta — i on się trzyma.
• Squad Launch + Friend Follow. Wyślij wszystkie zaznaczone konta na ten sam prywatny serwer, dołącz za znajomym na jego serwer albo wyląduj kontami razem na jednym publicznym.
• Strażnik pamięci + Recycle. RoRoRo uczy się, ile RAM-u klient Roblox naprawdę kosztuje na twojej maszynie, i ostrzega, zanim jej zabraknie. Jedno kliknięcie zamyka ciężkiego klienta i odsyła go na ten sam serwer.
• Jedno okno narzędzi. Gry, ustawienia, historia, diagnostyka, pluginy i O programie to strony jednego okna obok listy kont — ze skrótami klawiszowymi wszędzie; F1 pokazuje listę.
• Motywy. Cztery wbudowane, w tym jeden, który nigdy nie przekazuje znaczenia samym kolorem, plus edytor własnych motywów z udostępnianiem w pliku.
• Alerty opcjonalne — pulpit, Discord albo telefon. Skieruj każdy alert w dowolną kombinację: powiadomienia na pulpicie, utworzony przez ciebie webhook Discorda albo telefon przez Pushover lub ntfy. Wylogowania altów, ostrzeżenia o pamięci, zakończenia Recycle i sygnał „wszystko gra" co dwie godziny. Świeża instalacja nie wykonuje żadnych wywołań alertów — nic nie wychodzi, dopóki sam czegoś nie skonfigurujesz.
• Zasobnik systemowy z ikoną w kolorze stanu; dwuklik uruchamia główne konto.
• System pluginów. Opcjonalne pluginy działają jako osobne procesy i nie mają żadnych uprawnień, dopóki nie nadasz każdemu z osobna.
• Automatyczne aktualizacje przez Velopack. Zdalna konfiguracja śledzi znaną wersję Roblox i nazwę mutexa, żeby zmiana po stronie Roblox nie blokowała cię na długo.
• Dostępność mierzona. Każda kontrolka przedstawia się technologiom asystującym, a kontrast jest sprawdzany na wyrenderowanych pikselach w każdym motywie.

Prywatność i bezpieczeństwo:
RoRoRo nigdy nie widzi twojego hasła do Roblox. Logowanie odbywa się w całości na stronie samego Roblox, osadzonej w ramce Microsoft Edge WebView2 — ten sam HTML, to samo połączenie HTTPS co w przeglądarce. RoRoRo przechwytuje tylko cookie sesji, które Roblox ustawia po zalogowaniu, i szyfruje je przed zapisem na dysk. Zero telemetrii. Zero analityki. Nic nie opuszcza twojej maszyny poza wywołaniami do Roblox przy starcie — tymi samymi, które Roblox.com robi z przeglądarki — oraz, wyłącznie jeśli sam je skonfigurujesz, alertami na twój webhook Discorda albo wybraną usługę push (Pushover lub ntfy).

Ważne: znaki towarowe i powiązania.
„Roblox" i logo Roblox są znakami towarowymi Roblox Corporation. RoRoRo to niezależne narzędzie zewnętrzne, niepowiązane z Roblox Corporation, nierekomendowane ani niesponsorowane przez nią. Znak towarowy służy wyłącznie opisaniu zgodności z platformą Roblox. RoRoRo uruchamia oficjalnego klienta Roblox bez modyfikacji — bez wstrzykiwania, bez hooków, bez ingerencji w proces Roblox; przed startem jedynie trzyma nazwany mutex Windows, tak aby kolejne instancje klienta widziały sprawdzenie singletona jako już zajęte.

Produkt 626 Labs.
```

## Product features (17 entries, ≤200 chars each)

```
Launcher multi-instancji dla Roblox na Windows — jednym kliknięciem
Szyfrowany sejf kont DPAPI z eksportem chronionym frazą-hasłem
Status każdego konta na żywo — gra, czas bezczynności i limit FPS na konto
Strażnik pamięci uczący się prawdziwego kosztu RAM każdego klienta, plus Recycle jednym kliknięciem na ten sam serwer
Squad Launch i Friend Follow — ten sam prywatny serwer albo wspólny publiczny
Dołączanie z linku z dowolnego URL roblox.com, z zapisanymi prywatnymi serwerami na konto
Jedno okno narzędzi: gry, ustawienia, historia i więcej, ze skrótami klawiszowymi
Cztery wbudowane motywy plus edytor własnych
Opcjonalne alerty Discord na utworzony przez ciebie webhook
Zasobnik z ikoną w kolorze stanu i dwuklikiem uruchamiającym główne konto
System pluginów ze zgodą na każdą zdolność i izolacją w osobnych procesach
Automatyczne aktualizacje odporne na zmiany po stronie Roblox
Start z Windows, jeśli chcesz — jeden przełącznik, a lista Autostartu Windows ma ostatnie słowo
Discord Join uruchamia RoRoRo nawet zamknięte — i zawsze pyta, zanim cokolwiek wystartuje
Alerty na telefon przez Pushover lub ntfy — alt wypada i telefon wibruje, nawet bez Discorda
Sygnał „wszystko gra" co dwie godziny, póki konta działają — cisza znaczy: sprawdź PC
Alerty wachlarzem — pulpit, kanały Discord i telefon w dowolnej kombinacji, na każdy alert
```

## What's new in this version (v1.25.0.0, ≤1500 chars)

```
v1.25.0.0

Twój telefon może teraz wibrować
• Settings > Alerts: skieruj dowolny alert na telefon przez
  Pushover lub ntfy. Alt wypada — telefon o tym wie, nawet przy
  zamkniętym Discordzie. Jednorazowa konfiguracja, a Test my phone
  udowadnia, że działa. Klucze zostają zaszyfrowane na twoim PC;
  nic nie jest wysyłane poza momentem alertu, i tylko do wybranej
  przez ciebie usługi.

Alerty idą wszędzie, gdzie zaznaczysz
• Pulpit, twój kanał Discord, kanał klanu, telefon — dowolna
  kombinacja na każdy alert. Stare ustawienia przeniesione
  automatycznie.

Dwa nowe alerty, domyślnie wyłączone
• Zakończony Recycle podaje, ile pamięci odzyskał. Sygnały
  działania mówią „4h up — 6 accounts in" co dwie godziny — sygnał,
  który nie dociera, znaczy, że PC albo aplikacja padły.

Roblox zostaje w oknie
• Jeśli awaria albo Alt+Enter zostawiły Roblox w trybie
  pełnoekranowym, RoRoRo czyści to przed każdym startem. Przełącznik
  w Settings > Startup, domyślnie włączony.

Wracasz do starszej wersji?
• Jeśli po skonfigurowaniu nowego routingu alertów wrócisz do
  starszej wersji, ustaw routing tam od nowa — starsze wersje po
  cichu pomijają nieznane opcje.
```

## Copyright (single line)

```
© 2026 626 Labs LLC. Wszelkie prawa zastrzeżone. „Roblox" jest znakiem towarowym Roblox Corporation. RoRoRo nie jest powiązane z Roblox Corporation, rekomendowane ani sponsorowane przez nią.
```

## Trademark info

```
„Roblox" i logo Roblox są znakami towarowymi Roblox Corporation. RORORO to niezależne narzędzie zewnętrzne, niepowiązane z Roblox Corporation, nierekomendowane ani niesponsorowane przez nią. Znak towarowy służy wyłącznie opisaniu zgodności z platformą Roblox. RORORO uruchamia oficjalnego klienta Roblox bez modyfikacji.
```
