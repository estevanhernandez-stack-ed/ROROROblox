# Store listing — Polski (pl)

> Paste-ready dla Partner Center → Store listings → **Polski**. Refreshed 2026-09-08 for v1.27.0.0 (what's-new + language claims). Drafted 2026-09-05 from
> `listing-copy.md` (v1.25.0.0 state). Product nouns stay in English (RoRoRo, Squad Launch,
> Friend Follow, Pushover, ntfy); the app UI is now localized to Polish (v1.26.0.0). Register: ty (norma dla gier).

## Short description (≤200 chars)

```
Multi-launcher dla Windows, teraz w 6 językach: kilka klientów Roblox obok siebie, każdy na innym koncie. Szyfrowany sejf, Squad Launch, strażnik pamięci, alerty na telefon, status na żywo, motywy.
```

## Long description

```
Multi-launcher dla Windows.

RoRoRo to launcher dla Windows, który uruchamia kilka klientów Roblox jednocześnie na jednym PC — każdy zalogowany na inne zapisane konto, które należy do ciebie. Dodaj konta raz przez własną stronę logowania Roblox, a potem uruchamiaj je jednym kliknięciem — do domyślnej gry, na zapisany prywatny serwer albo z dowolnego wklejonego linku do gry.

Co dostajesz:
• Multi-instancja jednym kliknięciem. RoRoRo trzyma mutex singleton Roblox, więc kolejne klienty się otwierają, zamiast wyciągać pierwszy na wierzch.
• Szyfrowany sejf kont (DPAPI). Zapisane pliki cookie są szyfrowane interfejsem Windows Data Protection API i związane z twoim kontem Windows — plik skopiowany na inny PC się nie odszyfruje. Przenoszenie kont między własnymi komputerami to świadomy eksport chroniony frazą-hasłem.
• Status każdego konta na żywo. Widzisz, które konto jest w której grze, kto stoi bezczynnie i jak długo, a limit FPS ustawiasz osobno dla każdego konta — i on się trzyma.
• Squad Launch + Friend Follow. Wyślij wszystkie zaznaczone konta na ten sam prywatny serwer, dołącz za znajomym na jego serwer albo wyląduj kontami razem na jednym publicznym.
• Strażnik pamięci + Recycle. RoRoRo uczy się, ile RAM-u klient Roblox naprawdę kosztuje na twojej maszynie, i ostrzega, zanim jej zabraknie. Jedno kliknięcie zamyka ciężkiego klienta i odsyła go na ten sam serwer.
• Jedno okno narzędzi. Gry, ustawienia, historia, diagnostyka, pluginy i O programie to strony jednego okna obok listy kont — ze skrótami klawiszowymi wszędzie; F1 pokazuje listę.
• Motywy. Cztery wbudowane, w tym jeden, który nigdy nie przekazuje znaczenia samym kolorem, plus edytor własnych motywów z dziesięciu kolorów, z udostępnianiem w pliku.
• Alerty opcjonalne — pulpit, Discord albo telefon. Skieruj każdy alert w dowolną kombinację: powiadomienia na pulpicie, utworzony przez ciebie webhook Discorda albo telefon przez Pushover lub ntfy. Wylogowania altów, ostrzeżenia o pamięci, zakończenia Recycle i sygnał „wszystko gra" co dwie godziny. Świeża instalacja nie wykonuje żadnych wywołań alertów — nic nie wychodzi, dopóki sam czegoś nie skonfigurujesz.
• Zasobnik systemowy. Ikona w kolorze stanu rzutem oka pokazuje stan multi-instancji; dwuklik uruchamia główne konto.
• System pluginów. Opcjonalne pluginy działają jako osobne procesy i nie mają żadnych uprawnień, dopóki nie nadasz każdemu z osobna.
• Automatyczne aktualizacje przez Velopack. Zdalna konfiguracja śledzi znaną wersję Roblox i nazwę mutexa, żeby zmiana po stronie Roblox nie blokowała cię na długo.
• Dostępność mierzona. Każda kontrolka przedstawia się technologiom asystującym, a kontrast jest sprawdzany na wyrenderowanych pikselach w każdym motywie.
• Mówi w twoim języku. Cała aplikacja — każde menu, ustawienie, podpowiedź i okno, a także komunikaty, które RoRoRo wypisuje podczas pracy — jest przetłumaczona na francuski, niemiecki, rosyjski, portugalski (brazylijski), polski i hiszpański, zgodnie z językiem Windows lub twoim wyborem w Ustawieniach. Zmiana działa natychmiast, a lista pokazuje tylko w pełni przetłumaczone języki, więc nigdy nie trafisz na ekran w połowie po angielsku.

Prywatność i bezpieczeństwo:
RoRoRo nigdy nie widzi twojego hasła do Roblox. Logowanie odbywa się w całości na stronie samego Roblox, osadzonej w ramce Microsoft Edge WebView2 — ten sam HTML, to samo połączenie HTTPS co w przeglądarce. RoRoRo przechwytuje tylko cookie sesji, które Roblox ustawia po zalogowaniu, i szyfruje je przed zapisem na dysk. Zero telemetrii. Zero analityki. Nic nie opuszcza twojej maszyny poza wywołaniami do Roblox przy starcie — tymi samymi, które Roblox.com robi z przeglądarki — oraz, wyłącznie jeśli sam je skonfigurujesz, alertami na twój webhook Discorda albo wybraną usługę push (Pushover lub ntfy).

Ważne: znaki towarowe i powiązania.
„Roblox" i logo Roblox są znakami towarowymi Roblox Corporation. RoRoRo to niezależne narzędzie zewnętrzne, niepowiązane z Roblox Corporation, nierekomendowane ani niesponsorowane przez nią. Znak towarowy służy wyłącznie opisaniu zgodności z platformą Roblox. RoRoRo uruchamia oficjalnego klienta Roblox bez modyfikacji — bez wstrzykiwania, bez hooków, bez ingerencji w proces Roblox; przed startem jedynie trzyma nazwany mutex Windows, tak aby kolejne instancje klienta widziały sprawdzenie singletona jako już zajęte.

Produkt 626 Labs.
```

## Product features (18 entries, ≤200 chars each)

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
Sześć języków — cała aplikacja, ekrany i komunikaty: francuski, niemiecki, rosyjski, portugalski (brazylijski), polski, hiszpański. Natychmiastowa zmiana, wg Windows lub twojego wyboru
```

## What's new in this version (v1.27.0.0, ≤1500 chars)

```
v1.27.0.0

RoRoRo mówi w Twoim języku
• Cała aplikacja — menu, ustawienia, każde okno i komunikaty,
  które RoRoRo wypisuje podczas pracy — jest przetłumaczona na
  francuski, niemiecki, rosyjski, portugalski (Brazylia),
  polski i hiszpański. Jeśli Windows jest ustawiony na jeden z
  nich, RoRoRo sam go podchwyci.

Zmieniaj kiedy chcesz, natychmiast
• Ustawienia > Wygląd pokazują tylko języki, na które RoRoRo
  jest w pełni przetłumaczone — nigdy nie trafisz na ekran
  przetłumaczony w połowie. Wybierz język, a aplikacja
  przełączy się od razu, bez restartu.

W środku nadal angielski
• Angielski pozostaje domyślny, a nazwy funkcji — Squad
  Launch, Recycle, RoRoRo — czyta się tak samo w każdym
  języku. Twoje konta, motywy i ustawienia się nie zmieniają.
```

## Copyright (single line)

```
© 2026 626 Labs LLC. Wszelkie prawa zastrzeżone. „Roblox" jest znakiem towarowym Roblox Corporation. RoRoRo nie jest powiązane z Roblox Corporation, rekomendowane ani sponsorowane przez nią.
```

## Trademark info

```
„Roblox" i logo Roblox są znakami towarowymi Roblox Corporation. RORORO to niezależne narzędzie zewnętrzne, niepowiązane z Roblox Corporation, nierekomendowane ani niesponsorowane przez nią. Znak towarowy służy wyłącznie opisaniu zgodności z platformą Roblox. RORORO uruchamia oficjalnego klienta Roblox bez modyfikacji.
```
