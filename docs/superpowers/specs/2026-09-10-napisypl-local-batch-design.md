# NapisyPL v2 — batch folderów, logi i lokalny translator

## Cel

Rozszerzyć działające MVP NapisyPL o trzy rzeczy:

1. tłumaczenie wielu plików z jednego folderu,
2. czytelny postęp i logi diagnostyczne także dla wolnych modeli lokalnych,
3. lokalny translator EN→PL działający offline, niezależnie od chmurowych API.

Zakres pozostaje Windows-first. Brak transkrypcji audio i brak OCR napisów obrazkowych.

## Decyzja architektoniczna dla lokalnego tłumaczenia

Argos Translate jest biblioteką Pythonową korzystającą z CTranslate2. Zamiast osadzać Python bezpośrednio w procesie Avalonia, NapisyPL uruchamia osobny helper `NapisyPL.LocalTranslator.exe`.

Helper:
- jest pobierany przy pierwszym wyborze providera `Lokalny (Argos)`,
- zawiera własny runtime i zależności potrzebne do Argos/CTranslate2,
- działa jako proces potomny bez osobnego okna,
- pozostaje uruchomiony przez cały job/folder, aby model był ładowany do RAM tylko raz,
- komunikuje się z NapisyPL przez JSON Lines na stdin/stdout,
- raportuje gotowość, postęp, wynik segmentu i błędy,
- jest zamykany po zakończeniu/anulowaniu zadania albo zamknięciu aplikacji.

Model EN→PL jest pobierany osobno z oficjalnego indeksu Argos przy pierwszym użyciu i przechowywany w `%LocalAppData%\NapisyPL\local-translator\models\`. Aktualny oficjalny indeks udostępnia bezpośredni model English→Polish; kod nie powinien przywiązywać logiki do jednej wersji modelu, tylko wybierać pakiet `from_code=en`, `to_code=pl` z indeksu i zapisywać zainstalowaną wersję.

## Provider `Lokalny (Argos)`

Nowy provider implementuje ten sam logiczny kontrakt co pozostałe translatory, ale nie używa HTTP.

UI pokazuje:
- nazwę `Lokalny (Argos)` / `Offline`,
- informację, czy runtime i model są zainstalowane,
- przycisk `Pobierz translator` przy pierwszym użyciu,
- status pobierania runtime/modelu,
- brak pól API key, model i Base URL.

Po instalacji provider działa całkowicie offline.

W v2 używamy CPU jako domyślnego urządzenia. Nie wymagamy CUDA. GPU można rozważyć później po benchmarkach jakości i wydajności.

## Protokół helpera

NapisyPL uruchamia helper z przekierowanym stdin/stdout/stderr.

Przykładowe komunikaty wejściowe:

```json
{"type":"load","source":"en","target":"pl","modelPath":"..."}
{"type":"translate","jobId":"...","segments":[{"id":1,"text":"Hello"}]}
{"type":"shutdown"}
```

Przykładowe komunikaty wyjściowe:

```json
{"type":"ready","model":"en-pl","version":"..."}
{"type":"progress","jobId":"...","completed":12,"total":40}
{"type":"segment","jobId":"...","id":12,"text":"..."}
{"type":"complete","jobId":"..."}
{"type":"error","jobId":"...","message":"..."}
```

Helper nie zapisuje tekstu napisów do logów.

## Batch folderu

Główny ekran dostaje drugi sposób wejścia: `Wybierz folder`. Drag & drop folderu również ma działać.

Skanowanie v2 jest domyślnie nierekurencyjne: tylko bezpośrednie pliki w wybranym folderze. Rekurencję zostawiamy na później, aby zachować przewidywalność.

Obsługiwane pliki są takie jak w MVP: filmy oraz SRT/ASS/SSA/VTT/TXT.

Pliki wynikowe `*.pl.srt` i `*.pl.txt` nie są ponownie dodawane do kolejki.

Przetwarzanie jest sekwencyjne. To jest celowe:
- lokalny model pozostaje raz załadowany w RAM,
- chmurowe API nie dostają nagłego równoległego burstu,
- log i progress są czytelne,
- anulowanie zachowuje jednoznaczny stan.

Dla każdego pliku:
- jeśli wynik `.pl.srt` / `.pl.txt` już istnieje, plik jest domyślnie pomijany,
- film bez ścieżki napisów jest pomijany z powodem,
- film z wyłącznie napisami bitmapowymi jest pomijany z powodem,
- przy wielu tekstowych ścieżkach wybór odbywa się tak jak w MVP: preferuj `eng`/`en`, potem pierwszą tekstową,
- awaria pojedynczego pliku nie kończy całego folderu; trafia do podsumowania i kolejka idzie dalej.

Po zakończeniu pokazujemy: przetłumaczone / pominięte / błędy.

## Progress

Obecny progress jest oparty o zakończone batch-e, więc wolny LLM może długo pokazywać 0%.

V2 rozdziela postęp na trzy poziomy:

- `Folder`: plik N / wszystkich plików,
- `Plik`: segment N / wszystkich segmentów albo batch N / wszystkich batchy,
- `Aktualne wywołanie`: licznik czasu od rozpoczęcia requestu/batcha.

Dla providerów HTTP nie udajemy progresu tokenowego, jeśli API nie streamuje danych. UI pokazuje wtedy aktywny timer i numer batcha, zamiast zamrożonego paska.

Dla lokalnego helpera progress może rosnąć per przetłumaczony segment, bo helper kontroluje inferencję po swojej stronie.

## Logowanie diagnostyczne

Nowa usługa `AppLogger` zapisuje logi do `%LocalAppData%\NapisyPL\logs\napisypl-YYYY-MM-DD.log`.

Logujemy:
- start/koniec aplikacji,
- wersję NapisyPL,
- provider i model (jeśli dotyczy),
- start/koniec pliku,
- liczbę segmentów i znaków,
- numer i rozmiar batcha,
- czas batcha/requestu,
- status HTTP / kategorię błędu,
- usage/tokeny tylko jeśli provider zwraca je w odpowiedzi,
- pobieranie i wersję FFmpeg,
- pobieranie i wersję local translatora/modelu,
- start/stop/crash helpera,
- podsumowanie folderu.

Nigdy nie logujemy:
- API keys,
- Authorization/x-api-key headers,
- pełnych promptów,
- pełnej treści napisów.

UI dostaje przycisk `Otwórz log`.

## Batching providerów LLM

Pozostaje batching, ale jego parametry przenosimy do jednej konfiguracji. Domyślnie zmniejszamy paczki dla local/OpenAI-compatible z 40 segmentów do bardziej responsywnej wartości, np. 20 segmentów / maks. 6000 znaków. DeepL może zachować większe paczki.

Celem jest lepszy time-to-first-progress i łatwiejsza diagnostyka, nie maksymalizacja throughput za wszelką cenę.

Nie dodajemy w v2 token-streamingu odpowiedzi LLM do parsera SRT; to może być osobna optymalizacja później.

## Zarządzanie pobieraniem local translatora

Nowy `LocalTranslatorManager` odpowiada za:
- sprawdzenie stanu instalacji,
- pobranie wersjonowanego helpera Windows x64,
- pobranie modelu EN→PL,
- instalację do katalogu tymczasowego i atomowe przeniesienie do katalogu docelowego,
- zapis manifestu z wersją,
- usunięcie niepełnej instalacji po błędzie,
- możliwość `Przeinstaluj translator` w ustawieniach.

Pierwsza wersja helpera będzie publikowana razem z repozytorium/release NapisyPL, a aplikacja pobierze jego wersjonowany ZIP. Model jest pobierany ze źródła Argos.

## Benchmark jakości

Dodajemy prosty sposób porównania tego samego pliku na różnych providerach bez przebudowy GUI na rozbudowane narzędzie testowe.

Do repo trafia niewielki zestaw testowy EN z krótkimi kwestiami, slangiem, dialogiem wieloliniowym, nazwami własnymi i tagami formatowania.

Benchmark developerski zapisuje:
- provider,
- model,
- liczbę segmentów,
- liczbę znaków,
- całkowity czas,
- segmenty/sekundę,
- znaki/sekundę.

Ocena jakości Argos vs DeepL/Gemini jest na początku manualna — ważniejsza jest rzeczywista czytelność napisów niż pojedynczy automatyczny score.

## Testy

Nowe testy obejmują:
- planowanie kolejki folderu,
- pomijanie `.pl.srt` / `.pl.txt`,
- kontynuację po błędzie pojedynczego pliku,
- agregację progressu folder/pliki/segmenty,
- redakcję sekretów w logach,
- komunikację JSONL helpera,
- crash/restart helpera,
- anulowanie procesu helpera,
- brak ponownego pobrania poprawnie zainstalowanego modelu,
- niepełne/zepsute pobranie modelu,
- integracyjny smoke test helpera z małym tekstem EN→PL na Windows CI, jeśli rozmiar zależności pozwoli robić go w rozsądnym czasie; w przeciwnym razie osobny workflow/manual test.

Dotychczasowe testy MVP pozostają obowiązkowe.

## UI

Nie tworzymy nowego ekranu. Główne okno zachowuje prostotę.

Strefa wejścia otrzymuje:
- `Wybierz plik`,
- `Wybierz folder`.

W trybie folderu pojawia się mała kolejka/podsumowanie oraz progress:

```text
Folder: 7 / 12
Aktualnie: Episode07.mkv
Segment: 381 / 742
Partia: 10 / 19 · 00:07
```

Dla local translatora zamiast pól API pokazujemy stan `Offline · model EN→PL gotowy` albo przycisk instalacji.

## Poza zakresem v2

- Whisper / transkrypcja audio,
- OCR PGS/VobSub,
- tłumaczenie języków innych niż EN→PL,
- rekursywne foldery,
- równoległe tłumaczenie wielu plików,
- GPU jako wymaganie,
- automatyczne metryki jakości BLEU/COMET jako kryterium akceptacji,
- osadzanie przetłumaczonych napisów z powrotem do filmu.

## Kryteria akceptacji

V2 jest gotowe do testów użytkownika, gdy:

1. cały folder obsługiwanych plików można dodać jednym działaniem,
2. kolejka przechodzi przez wszystkie pliki i nie zatrzymuje się na pojedynczym błędzie,
3. progress nie wygląda na zawieszony podczas długiego requestu,
4. log pozwala ustalić, który provider/batch był wolny i ile trwał,
5. `Lokalny (Argos)` może zostać zainstalowany z poziomu aplikacji,
6. po instalacji tłumaczenie EN→PL działa bez internetu,
7. model jest ładowany raz na serię plików,
8. istniejące testy MVP nadal przechodzą,
9. Windows x64 publish przechodzi w GitHub Actions.
