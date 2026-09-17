# NapisyPL

**NapisyPL** to aplikacja desktopowa dla Windows, która najpierw szuka gotowych polskich napisów, a gdy ich nie ma — pobiera angielskie lub korzysta z napisów osadzonych w filmie i zapisuje polskie tłumaczenie.

> Aktualny checkpoint rozwoju trybu Enhanced, wyniki benchmarków i plan dalszych prac: [`docs/ENHANCED_STATUS_AND_PLAN.md`](docs/ENHANCED_STATUS_AND_PLAN.md).

## Jak to działa

1. Przeciągnij do okna film albo plik napisów.
2. Pozostaw włączone **Najpierw szukaj polskich napisów (QNapi)** i kliknij **Pobierz napisy / tłumacz**.
3. NapisyPL najpierw szuka napisów PL. Jeśli film ma osadzoną tekstową ścieżkę angielską, sprawdza czasy znalezionych napisów PL: zgodne zostawia bez zmian, a pewną różnicę przesunięcia lub standardowego FPS koryguje automatycznie. Gdy polski wynik jest niepewny, dla pojedynczego filmu można wybrać inną wersję w QNapi albo lokalny plik SRT. Po wyczerpaniu polskich możliwości aplikacja tłumaczy osadzone napisy EN; tylko gdy ich nie ma, szuka EN przez QNapi.

Wynik jest zapisywany obok pliku źródłowego jako `nazwa.pl.srt`. Można dodatkowo zaznaczyć eksport `nazwa.pl.txt`. Przycisk wyciągania oryginalnej ścieżki zapisuje ją jako `nazwa.srt`, czyli z tą samą nazwą bazową co film. Dla wejściowego TXT wynik jest tylko TXT.

## Menu Eksploratora i ikona w obszarze powiadomień

Kliknij prawym na film albo folder i wybierz **Szukaj napisów z SubFlow** (w Windows 11 pod „Pokaż więcej opcji”). Dla każdego filmu SubFlow sam, bez pytań:

1. szuka polskich napisów w QNapi i zapisuje je, gdy czasy pasują do filmu,
2. gdy ich nie ma, sprawdza angielską ścieżkę tekstową w filmie,
3. gdy i jej nie ma, szuka angielskich napisów w QNapi.

Okno wyszukiwania pokazuje przy każdym filmie dwa znaczniki: **PL** (gotowe polskie napisy) i **Maszynowe** (są angielskie napisy do tłumaczenia). Przycisk **Przetłumacz maszynowo** tłumaczy zaznaczone filmy tłumaczem wybranym w oknie głównym, z korektą rodzaju z głosu.

Menu włącza instalator albo **Opcje → Menu Eksploratora** (wpis tylko dla bieżącego użytkownika). Ta sama akcja jest dostępna z wiersza poleceń: `NapisyPL.exe --search "ścieżka"`, a `--register-shell` / `--unregister-shell` dodaje i usuwa wpis.

SubFlow działa w tle z ikoną w obszarze powiadomień: prawy klik daje **Szukaj napisów dla pliku…**, **Wskaż folder…**, **Otwórz SubFlow** i **Zamknij**. Zamknięcie okna chowa je do ikony; lokalny model tłumaczenia jest wtedy zwalniany z pamięci karty graficznej. Kolejne uruchomienia (np. kilka filmów zaznaczonych w Eksploratorze) trafiają do już działającej kopii.

## Opcje

- **Zapisz też plik TXT** — dodatkowo `nazwa.pl.txt`.
- **Korekta rodzaju z głosu** (domyślnie włączona) — poprawia formy typu zrobiłeś → zrobiłaś na podstawie głosów w filmie.
- **Tylko pewne rozpoznanie** (domyślnie włączone) — formy zmieniane tylko przy wyraźnym głosie.
- **Menu Eksploratora**, **Otwórz log**.

## Obsługiwane wejścia

- filmy: MKV, MP4, MOV, AVI, WebM, M4V, TS/MTS/M2TS,
- napisy: SRT, ASS, SSA, VTT,
- zwykły tekst: TXT.

Aplikacja **nie transkrybuje dźwięku**. Film bez osadzonych napisów może zostać obsłużony, jeśli QNapi znajdzie pasujący plik PL lub EN. Bitmapowe napisy PGS/VobSub/XSub są wykrywane, ale nie są bezpośrednio odczytywane, ponieważ wymagałyby OCR.

## Wyszukiwanie napisów przez QNapi

Przy pierwszym wyszukiwaniu aplikacja pobiera oficjalny pakiet QNapi 0.2.3 (~18 MB), sprawdza jego sumę SHA-256 i instaluje prywatnie w `%LocalAppData%\NapisyPL\qnapi\0.2.3\`. Istniejący plik `nazwa.pl.srt` jest zachowywany. Wyszukiwanie można wyłączyć, aby zawsze korzystać z dotychczasowego trybu tłumaczenia.

Integracja używa baz NapiProjekt i Napisy24. Wyszukiwanie angielskich napisów jest obecnie rozwiązaniem awaryjnym opartym na NapiProjekt; stary silnik OpenSubtitles w QNapi został wyłączony po zamknięciu używanego przez niego API. Szczegóły działania, prywatności i licencji opisuje [`docs/QNAPI.md`](docs/QNAPI.md).

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

Instalator (Inno Setup 6, `winget install JRSoftware.InnoSetup`):

```powershell
powershell -ExecutionPolicy Bypass -File installeruild-installer.ps1 -Version 1.0.0
```

Instaluje bez uprawnień administratora (opcjonalnie dla wszystkich użytkowników), dodaje menu Eksploratora, opcjonalnie autostart w tle i pobranie lokalnego tłumacza MADLAD dla kart AMD. Logo i ikonę generuje `dotnet run --project tools/brand -- src/NapisyPL/Assets`.

## Co można dodać później

Linux wymaga głównie dopisania instalacji/odnajdywania FFmpeg — UI i logika są wieloplatformowe dzięki Avalonia. Można też dodać całkowicie lokalny translator typu Marian/Argos, ale celowo nie jest częścią MVP: wymagałby osobnego runtime/modelu i znacząco zwiększył rozmiar aplikacji. Dla pracy lokalnej bez chmury już teraz można użyć Ollama.
