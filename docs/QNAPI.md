# Integracja QNapi

NapisyPL używa QNapi jako osobnego procesu do pobierania napisów dla plików wideo. Kod QNapi nie jest kopiowany ani linkowany z aplikacją. Przy pierwszym użyciu aplikacja pobiera przypięty pakiet portable QNapi 0.2.3 do:

`%LocalAppData%\NapisyPL\qnapi\0.2.3\`

Źródłem jest [oficjalny pakiet portable QNapi 0.2.3](https://github.com/QNapi/qnapi/releases/download/0.2.3/QNapi-0.2.3-portable.zip). Pobieranie jest akceptowane tylko wtedy, gdy SHA-256 wynosi:

`03CF7BF82565DE8B31745B1862F751EC9EE789F8E50AF41D361EE9246F99F827`

Ta suma jest również opublikowana w [weryfikacji pakietu Chocolatey](https://community.chocolatey.org/packages/qnapi.portable/0.2.3). Instalacja odbywa się w katalogu tymczasowym, a gotowy, sprawdzony katalog jest podmieniany atomowo. Repozytorium NapisyPL nie przechowuje binariów QNapi.

## Sposób uruchomienia

NapisyPL uruchamia `qnapi.exe` z oficjalnego pakietu portable w jego nieinteraktywnym trybie cichym. Nie używa przełącznika `-c`, ponieważ windowsowy pakiet 0.2.3 nie zawiera osobnego `qnapic.exe`. Każde wyszukiwanie podaje ten sam język jako główny i zapasowy, więc prywatne ustawienia użytkownika nie mogą zmienić wyniku:

```text
qnapi.exe -q -d -l pl -lb pl -f SRT -e npl<losowy_id> <film>
qnapi.exe -q -d -l en -lb en -f SRT -e npl<losowy_id> <film>
```

Losowe, alfanumeryczne rozszerzenie izoluje plik wyjściowy. QNapi nie dotyka istniejącego `.srt` ani `.pl.srt`. NapisyPL odczytuje utworzony plik, sprawdza strukturę i czasy SRT, a następnie usuwa plik tymczasowy. Brak takiego pliku oznacza brak wyniku, ponieważ [kod CLI QNapi nie przekazuje zwykłych błędów pojedynczego pliku w końcowym kodzie wyjścia](https://github.com/QNapi/qnapi/blob/master/cli/src/clisubtitlesdownloader.cpp).

Gdy automatycznie znaleziony polski plik nie pasuje pewnie do czasów osadzonej angielskiej ścieżki, dla pojedynczego filmu aplikacja pokazuje krótkie okno wyboru. Pozwala ono uruchomić QNapi bez `-q`, aby wybrać alternatywny wynik w jego własnym oknie, albo wskazać lokalny plik `.srt`. Oba źródła są ponownie sprawdzane względem czasów filmu; lokalny plik nie jest zmieniany. Tłumaczenie angielskich napisów jest ostatnią alternatywą. Przetwarzanie folderu pozostaje nieinteraktywne.

Obok programu znajduje się kontrolowany `qnapi.ini`. Włącza konwersję do UTF-8/SRT, tryb bez okien i wyszukiwanie do pierwszego wyniku. Wyłącza stary silnik OpenSubtitles XML-RPC. Pozostają silniki NapiProjekt oraz Napisy24. Interaktywne uruchomienie nie zmienia wspólnego `qnapi.ini`; różni się wyłącznie brakiem `-q` i `CreateNoWindow=false`. Także ono używa prywatnego losowego rozszerzenia pliku wyjściowego.

## Dlaczego OpenSubtitles jest wyłączony

QNapi 0.2.3 używa starego adresu `http://api.opensubtitles.org/xml-rpc`, co widać w [silniku OpenSubtitles](https://github.com/QNapi/qnapi/blob/master/libqnapi/src/engines/opensubtitlesdownloadengine.cpp). OpenSubtitles ogłosił w styczniu 2026 r. [ostateczne wyłączenie XML-RPC dla wszystkich aplikacji zewnętrznych](https://forum.opensubtitles.com/t/opensubtitles-org-api-final-shutdown-notice-for-non-vip-users/5045). Pozostawienie go w kolejce powodowałoby opóźnienia i błędy przed przejściem do Napisy24.

NapiProjekt obsługuje zarówno `PL`, jak i `ENG`; mapowanie języka i protokół są widoczne w [silniku NapiProjekt](https://github.com/QNapi/qnapi/blob/master/libqnapi/src/engines/napiprojektdownloadengine.cpp). [Silnik Napisy24](https://github.com/QNapi/qnapi/blob/master/libqnapi/src/engines/napisy24downloadengine.cpp) zwraca wyłącznie polskie napisy. Dlatego angielski wynik awaryjny pochodzi obecnie tylko z NapiProjekt.

## Ograniczenia

- QNapi 0.2.3 wydano 19 maja 2017 r.; [ostatnia zmiana w gałęzi głównej](https://github.com/QNapi/qnapi/commit/d4e0378a601838a96b7ee25ff48a8eaf18388fcd) pochodzi z 25 lipca 2022 r.
- NapiProjekt i Napisy24 są starymi, nieudokumentowanymi publicznie integracjami. Zmiana po stronie serwera może przerwać pobieranie bez aktualizacji aplikacji.
- Oba silniki w QNapi używają adresów HTTP, a nie HTTPS. Zapytanie zawiera skrót filmu, jego rozmiar, nazwę pliku i parametry klienta; transport nie jest szyfrowany.
- Angielskie napisy nie mają obecnie drugiego sprawnego dostawcy. Pełne zastępstwo wymaga integracji z nowym REST API OpenSubtitles.com i klucza aplikacji.
- Automatyczne QNapi jest uruchamiane z limitem 90 sekund. Wybór interaktywny ma limit 5 minut, aby użytkownik mógł wybrać wynik. Anulowanie lub przekroczenie limitu kończy całe drzewo procesu i usuwa prywatny plik wyjściowy.

## Licencja i źródła

QNapi jest objęty [GNU GPL w wersji 2 lub nowszej](https://github.com/QNapi/qnapi/blob/master/doc/LICENSE), zgodnie także z [jego stroną podręcznika](https://github.com/QNapi/qnapi/blob/master/doc/man/qnapi.1). Pobrany pakiet zachowuje własne pliki licencyjne i zależności. NapisyPL komunikuje się z nim wyłącznie przez argumenty procesu i plik wyjściowy.

Dokumentacja kodu, na której opiera się adapter:

- [opcje CLI i kody języków](https://github.com/QNapi/qnapi/blob/master/doc/man/qnapi.1),
- [wybór języka głównego i zapasowego](https://github.com/QNapi/qnapi/blob/master/cli/src/clisubtitlesdownloader.cpp),
- [tworzenie docelowej nazwy pliku](https://github.com/QNapi/qnapi/blob/master/libqnapi/src/subtitlematcher.cpp),
- [tryb portable przez `qnapi.ini`](https://github.com/QNapi/qnapi/blob/master/libqnapi/src/libqnapi.cpp),
- [rejestr silników](https://github.com/QNapi/qnapi/blob/master/libqnapi/src/engines/subtitledownloadenginesregistry.cpp).
