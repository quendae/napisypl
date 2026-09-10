# NapisyPL — Design

## Cel

NapisyPL to prosta aplikacja desktopowa Windows do wyciągania tekstowych ścieżek napisów z plików wideo albo wczytywania zewnętrznych plików napisów, tłumaczenia angielskiego tekstu na polski oraz zapisu wyniku jako UTF-8 SRT i opcjonalnie TXT.

## Zakres MVP

- Windows x64 jako pierwszy wspierany system.
- .NET 10 LTS + Avalonia UI, bez zależności od Windows-only UI frameworków, aby późniejszy port na Linux nie wymagał przepisywania aplikacji.
- Wejście: pliki wideo (m.in. MKV/MP4/MOV/AVI/WebM) oraz zewnętrzne napisy SRT/ASS/SSA/VTT; TXT jako prosty tekst bez timestampów.
- Wideo: ffprobe wykrywa ścieżki napisów. Ścieżka `eng`/`en` jest wybierana automatycznie, jeśli istnieje. Przy kilku możliwych ścieżkach użytkownik może wybrać inną.
- Obsługiwane są tekstowe kodeki napisów. Bitmapowe PGS/VobSub/XSub są wykrywane i oznaczone jako niewspierane w MVP.
- Brak transkrypcji audio. Jeśli wideo nie zawiera napisów, aplikacja kończy operację czytelnym komunikatem.
- Tłumaczenie tylko EN → PL.
- Providerzy MVP: DeepL, Gemini, OpenAI-compatible (OpenAI, Ollama, LM Studio i podobne), Anthropic Claude.
- Wyjście: `<nazwa>.pl.srt` i opcjonalnie `<nazwa>.pl.txt`, zawsze UTF-8.

## UX

Główne okno jest jedynym ekranem roboczym. Użytkownik przeciąga plik albo wybiera go przyciskiem. Po analizie widzi nazwę pliku i, dla wideo, listę ścieżek napisów. Niżej wybiera providera, wpisuje klucz API (jeśli wymagany), model oraz opcjonalny Base URL. Kliknięcie `Tłumacz na polski` uruchamia cały pipeline. Pasek postępu i jednozdaniowy status pokazują aktualny etap. Po sukcesie aplikacja pokazuje ścieżkę pliku wynikowego i przycisk otwarcia folderu.

Ustawienia providera są zapisywane w `%LocalAppData%/NapisyPL/settings.json`. Klucz API jest zapisywany tylko wtedy, gdy użytkownik zaznaczy `Zapamiętaj klucz`; w przeciwnym razie żyje tylko w pamięci procesu.

## Architektura

`FfmpegManager` odpowiada za znalezienie lokalnego FFmpeg/ffprobe i pobranie Windows build przy pierwszym użyciu. Pliki narzędzi trafiają do `%LocalAppData%/NapisyPL/ffmpeg/`.

`MediaProbeService` uruchamia ffprobe i zwraca neutralny model `SubtitleTrack`. Nie zna UI ani providerów.

`SubtitleExtractionService` wyciąga wybraną tekstową ścieżkę z wideo do tymczasowego SRT. Zewnętrzne ASS/SSA/VTT są konwertowane przez FFmpeg do tymczasowego SRT; SRT jest czytany bez konwersji. TXT przechodzi osobną ścieżką tekstową.

`SrtParser` zamienia SRT na listę `SubtitleCue { Index, Start, End, Text }`. Timestampy nigdy nie są wysyłane do LLM. Dzięki temu provider nie może uszkodzić struktury czasowej.

`ITranslationProvider` ma jeden kontrakt: przyjmuje ponumerowane segmenty tekstu i zwraca mapę `id → przetłumaczony tekst`. `TranslationCoordinator` dzieli materiał na paczki, zachowuje kolejność, waliduje kompletność odpowiedzi i raportuje postęp.

Providerzy LLM otrzymują JSON z ID segmentów oraz instrukcję zwrotu wyłącznie poprawnego JSON. Odpowiedź jest walidowana; brakujące ID powodują błąd danej paczki zamiast cichego uszkodzenia wyniku. DeepL używa natywnego endpointu tłumaczeniowego bez promptu.

`SubtitleWriter` składa przetłumaczony SRT z oryginalnymi timestampami i zapisuje go w UTF-8. TXT zawiera tylko przetłumaczone kwestie w kolejności.

## FFmpeg

Aplikacja najpierw szuka `ffmpeg.exe` i `ffprobe.exe` w swoim katalogu danych. Jeśli ich nie ma, pobiera Windows ZIP z wcześniej zdefiniowanego źródła buildów wskazywanego przez ffmpeg.org, rozpakowuje wymagane EXE i zapamiętuje lokalną kopię. Po pierwszym pobraniu ekstrakcja działa offline.

Downloader używa katalogu tymczasowego, timeoutu, sprawdzania statusu HTTP i atomowego podmienienia plików. Awaria pobierania nie usuwa działającej starszej kopii.

## Formaty i ograniczenia

Tekstowe napisy są normalizowane do SRT. Zaawansowane style ASS/SSA nie są zachowywane w MVP; priorytetem jest poprawny tekst i timing. Podstawowe tagi w treści są pozostawiane możliwie bez zmian. Bitmapowe napisy nie są OCR-owane.

TXT jako wejście nie ma timestampów, więc wynik jest wyłącznie TXT.

## Błędy

Błędy są klasyfikowane jako: brak napisów, bitmapowe napisy, FFmpeg niedostępny/pobranie nieudane, błędny klucz/API, timeout/rate limit, niepoprawna odpowiedź modelu, nieobsługiwany format, błąd zapisu. UI pokazuje krótki komunikat, a szczegóły techniczne trafiają do `%LocalAppData%/NapisyPL/logs/`.

## Testy

- Parser SRT: multiline, CRLF/LF, polskie znaki, tagi, puste linie.
- Writer SRT: zachowanie indeksów i timestampów, UTF-8.
- ffprobe JSON: wykrywanie języka, tytułu, kodeku i bitmapowych ścieżek.
- TranslationCoordinator: batching, zachowanie ID i progress.
- Provider HTTP: testy bez prawdziwych kluczy tam, gdzie ma to sens.
- Smoke build na `windows-latest` w GitHub Actions.

## Poza MVP

Transkrypcja Whisper, OCR PGS/VobSub, edytor napisów, auto-synchronizacja, tłumaczenie innych języków, osadzanie napisów z powrotem do wideo oraz lokalny model Marian/Argos są celowo poza pierwszą wersją.
