# NapisyPL

**NapisyPL** to mała aplikacja desktopowa dla Windows, która robi jedną rzecz: bierze angielskie napisy i zapisuje ich polskie tłumaczenie.

## Jak to działa

1. Przeciągnij do okna film albo plik napisów.
2. Dla filmu NapisyPL wykryje osadzone ścieżki napisów i automatycznie wybierze angielską ścieżkę tekstową, jeśli jest oznaczona jako `eng`/`en`.
3. Wybierz tłumacza, wpisz klucz API (jeśli jest potrzebny) i kliknij **Tłumacz na polski**.

Wynik jest zapisywany obok pliku źródłowego jako `nazwa.pl.srt`. Można dodatkowo zaznaczyć eksport `nazwa.pl.txt`. Dla wejściowego TXT wynik jest tylko TXT.

## Obsługiwane wejścia

- filmy: MKV, MP4, MOV, AVI, WebM, M4V, TS/MTS/M2TS,
- napisy: SRT, ASS, SSA, VTT,
- zwykły tekst: TXT.

Aplikacja **nie transkrybuje dźwięku**. Jeśli film nie zawiera ścieżki napisów, niczego nie generuje. Bitmapowe napisy PGS/VobSub/XSub są wykrywane, ale w pierwszej wersji nie są obsługiwane, ponieważ wymagałyby OCR.

ASS/SSA/VTT są normalizowane do SRT. Zaawansowane style ASS nie są przenoszone — priorytetem jest tekst i timing.

## Tłumacze

### Gemini

Domyślnie: `gemini-3.8-flash` przez Gemini Interactions API. Wymaga klucza Gemini API.

### DeepL

Dedykowany translator zamiast LLM. Domyślnie używa `https://api-free.deepl.com`; dla płatnego DeepL API można podać `https://api.deepl.com`.

### OpenAI / Ollama

Jeden provider zgodny z API `/v1/chat/completions`.

- OpenAI: domyślnie `https://api.openai.com/v1` i wymaga klucza API.
- Ollama: ustaw Base URL `http://localhost:11434/v1`, wpisz nazwę lokalnie zainstalowanego modelu i zostaw klucz pusty.
- LM Studio: zwykle `http://localhost:1234/v1`; klucz może pozostać pusty.

### Claude

Domyślnie: `claude-sonnet-5` przez Anthropic Messages API. Wymaga klucza Claude API.

Klucze API **nie są zapisywane na dysku**. Napisy są dzielone na paczki, a provider dostaje wyłącznie identyfikator segmentu i jego tekst — timestampy SRT pozostają lokalnie, więc model nie może ich przypadkowo zmienić.

## FFmpeg

Do filmów oraz konwersji ASS/SSA/VTT potrzebne są `ffmpeg` i `ffprobe`. NapisyPL pobiera przy pierwszym użyciu Windows build FFmpeg do:

`%LocalAppData%\NapisyPL\ffmpeg\`

Kolejne operacje korzystają z lokalnej kopii. Pliki SRT i TXT nie wymagają FFmpeg do samego tłumaczenia.

FFmpeg jest osobnym projektem i podlega własnej licencji. Aplikacja nie przechowuje binariów FFmpeg w tym repozytorium.

## Kodowanie

Wynikowe SRT/TXT są zawsze zapisywane jako UTF-8 bez BOM, więc polskie znaki są zachowane poprawnie.

## Budowanie

Wymagany jest .NET 10 SDK.

```powershell
dotnet restore NapisyPL.sln
dotnet test NapisyPL.sln -c Release
dotnet run --project src/NapisyPL/NapisyPL.csproj
```

Publikacja samodzielnej wersji Windows x64:

```powershell
dotnet publish src/NapisyPL/NapisyPL.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/win-x64
```

GitHub Actions uruchamia testy i tworzy artefakt `NapisyPL-win-x64`.

## Co można dodać później

Linux wymaga głównie dopisania instalacji/odnajdywania FFmpeg — UI i logika są wieloplatformowe dzięki Avalonia. Można też dodać całkowicie lokalny translator typu Marian/Argos, ale celowo nie jest częścią MVP: wymagałby osobnego runtime/modelu i znacząco zwiększył rozmiar aplikacji. Dla pracy lokalnej bez chmury już teraz można użyć Ollama.
