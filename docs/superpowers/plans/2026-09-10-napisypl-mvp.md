# NapisyPL MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Zbudować prostą aplikację Windows, która wykrywa/wyciąga angielskie napisy, tłumaczy je EN→PL przez wybranego providera i zapisuje UTF-8 SRT/TXT.

**Architecture:** Avalonia jest cienkim UI nad usługami domenowymi. FFmpeg/ffprobe są zarządzane lokalnie przez `FfmpegManager`; timestampy i struktura SRT pozostają poza providerami tłumaczeń, które dostają wyłącznie ponumerowane segmenty tekstu.

**Tech Stack:** .NET 10, C# 14, Avalonia 12.1.2, System.Text.Json, HttpClient, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-10-napisypl-design.md`

## Global Constraints

- Windows x64 jest platformą pierwszego wydania; architektura nie może blokować Linuxa.
- Tylko EN → PL.
- Brak Whisper/OCR.
- Tekstowe napisy normalizujemy do SRT; bitmapowe ścieżki tylko wykrywamy i odrzucamy.
- Wyniki zapisujemy jako UTF-8.
- Timestampy nigdy nie są wysyłane do LLM.

---

### Task 1: Szkielet aplikacji i modele

**Files:** `NapisyPL.sln`, `src/NapisyPL/NapisyPL.csproj`, `Program.cs`, `App.axaml`, `App.axaml.cs`, `Models/SubtitleCue.cs`, `Models/SubtitleTrack.cs`, `Models/TranslationSegment.cs`.

- [ ] Utworzyć rozwiązanie .NET 10 i aplikację Avalonia 12.1.2.
- [ ] Dodać małe modele bez zależności od UI.
- [ ] Dodać projekt testowy xUnit i sprawdzić `dotnet test`.

### Task 2: Parser i writer SRT

**Files:** `Services/SrtParser.cs`, `Services/SubtitleWriter.cs`, `tests/NapisyPL.Tests/SrtParserTests.cs`.

**Interfaces:** `IReadOnlyList<SubtitleCue> Parse(string)`, `string BuildSrt(IEnumerable<SubtitleCue>)`, `Task WriteSrtAsync(...)`, `Task WriteTxtAsync(...)`.

- [ ] Najpierw testy multiline, LF/CRLF, zachowania timingów i polskich znaków.
- [ ] Zaimplementować parser oraz writer.
- [ ] Uruchomić testy.

### Task 3: FFmpeg manager, probe i ekstrakcja

**Files:** `Services/FfmpegManager.cs`, `Services/ProcessRunner.cs`, `Services/MediaProbeService.cs`, `Services/SubtitleExtractionService.cs`, `tests/NapisyPL.Tests/MediaProbeServiceTests.cs`.

**Interfaces:** `EnsureAvailableAsync`, `ProbeAsync`, `ExtractToSrtAsync`, `ConvertSubtitleFileToSrtAsync`.

- [ ] Dodać parser JSON ffprobe z testami na eng/en, brak language i bitmapowe kodeki.
- [ ] Dodać automatyczne pobieranie Windows ZIP z buildem FFmpeg do LocalAppData.
- [ ] Dodać bezpieczne uruchamianie procesów bez shell quoting.
- [ ] Dodać ekstrakcję wybranej ścieżki przez `-map 0:<streamIndex>` i `-c:s srt`.

### Task 4: Warstwa tłumaczeń

**Files:** `Translation/ITranslationProvider.cs`, `Translation/TranslationCoordinator.cs`, `Translation/Providers/DeepLProvider.cs`, `GeminiProvider.cs`, `OpenAiCompatibleProvider.cs`, `AnthropicProvider.cs`, `Translation/ProviderFactory.cs`, testy koordynatora.

**Interfaces:** `Task<IReadOnlyDictionary<int,string>> TranslateAsync(IReadOnlyList<TranslationSegment>, CancellationToken)`.

- [ ] Dodać batching i walidację kompletności ID.
- [ ] Dodać DeepL jako natywne API text translation.
- [ ] Dodać Gemini generateContent z rygorystycznym JSON output.
- [ ] Dodać OpenAI-compatible `/chat/completions` dla OpenAI/Ollama/LM Studio.
- [ ] Dodać Anthropic `/v1/messages`.

### Task 5: Pipeline wejście → wynik

**Files:** `Services/TranslationPipeline.cs`, `Settings/AppSettings.cs`, `Settings/SettingsStore.cs`.

- [ ] Rozróżnić video / SRT / ASS-SSA-VTT / TXT.
- [ ] Dla video znaleźć ścieżki i użyć wybranej.
- [ ] Dla SRT parsować bez FFmpeg, dla ASS/SSA/VTT konwertować przez FFmpeg, dla TXT tłumaczyć linie tekstowe.
- [ ] Zapisać `.pl.srt` i opcjonalny `.pl.txt` obok źródła.

### Task 6: Jednoekranowy UI Avalonia

**Files:** `MainWindow.axaml`, `MainWindow.axaml.cs`, `Styles.axaml`.

- [ ] Duży drop-zone + wybór pliku.
- [ ] Lista ścieżek tylko gdy potrzebna.
- [ ] Provider, model, Base URL, API key i checkbox zapamiętania.
- [ ] Przycisk `Tłumacz na polski`, progress i status.
- [ ] Po sukcesie przycisk otwarcia folderu.
- [ ] Stany disabled/busy/error bez modalnego chaosu.

### Task 7: README, CI i Windows publish

**Files:** `README.md`, `.github/workflows/ci.yml`, `.gitignore`.

- [ ] Opisać trzy kroki użycia i konfigurację providerów.
- [ ] Dodać `dotnet test` na Windows i Ubuntu (logika) oraz publish `win-x64` jako self-contained single-file gdzie to możliwe.
- [ ] Zweryfikować, że projekt kompiluje się na czystym runnerze.
