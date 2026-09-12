# Enhanced mode — status i plan kontynuacji

Aktualizacja: 2026-09-12, po benchmarku SubFlow 3.1.16.

## Stan repo / punkt wznowienia

- Repo: `quendae/napisypl`
- Branch roboczy: `feature/local-translator-batch-v2`
- Draft PR: `#2` — **nie merge'ować bez wyraźnej decyzji użytkownika**.
- Ostatni commit kodu przed tym checkpointem: `19b3987310db5bdc844518721c30c84be0757f5c` (`fix: enforce speaker gender edit direction`).
- Ostatni CI dla tego commita: run `34692842299` — success.
- Testy .NET: `154/154` pass.
- Local Translator workflow: run `34692842288` — success.
- Windows x64 artifact digest: `sha256:ea46d115fd01d87c6ab5368fe898062f88f972ca2e1bd1009a2e8a2a82675e87`.

Ten dokument jest checkpointem do dalszej pracy. Jeśli nowa sesja/agent nie ma historii rozmowy, zacząć od tego pliku, PR #2 i wymienionych niżej plików kodu.

## Obecna architektura Enhanced

Praktyczny pipeline:

`speaker diarization → acoustic speaker-gender evidence → base translator (najczęściej Argos) → candidate selection → local Qwen targeted review → deterministic safety guards → SRT`

Aktualne założenia:

- Bazowy translator Argos EN→PL jest traktowany jako stabilny baseline.
- Enhanced nie ma ponownie tłumaczyć całego pliku Qwenem; Qwen służy tylko do małych korekt rodzaju/liczby/adresata.
- Domyślny reviewer: `Qwen3.5-9B`, lokalnie przez `llama.cpp`, preferowany backend Vulkan.
- GPT-OSS-20B był wolniejszy i problematyczny z formatem odpowiedzi; nie jest obecnie preferowany.
- Qwen3-1.7B był za słaby semantycznie i został tylko historycznym/dev baseline'em.
- Runtime Qwena jest preloadowany równolegle z audio/Argos.
- Argos baseline ma persistent cache w `%LocalAppData%\SubFlow\cache\translations`.
- Cache przechowuje tylko czysty baseline Argosa, nie wynik po review; dzięki temu reviewer jest uruchamiany ponownie na każdym benchmarku.

## Speaker-gender classifier — aktualna polityka

Model acoustic tagging: SherpaOnnx audio tagging (`k2-fsa/sherpa-onnx-zipformer-small-audio-tagging-2024-04-15`).

Aggregator:

- min łącznego czasu: `1.5s`
- min combined speech evidence: `0.03`
- min normalized winner confidence: `0.75`
- min per-sample direction confidence: `0.65`
- spójność kierunku: około `2/3` próbek

Osobna polityka dopuszczenia evidence do automatycznej speaker correction (`SpeakerGenderReviewEligibility`):

- Gender != `Unknown`
- Confidence >= `0.85`
- **SampleCount >= 2**

To ostatnie ograniczenie zostało dodane po wykryciu jednopróbkowego `SPEAKER_13:female:0.94`, który nie powinien samodzielnie autoryzować edycji.

## Safety stack po 3.1.16

### 3.1.14 — reviewer zaczął reagować

Prompt dostał jawne per-candidate `candidateSpeakerGender` i instrukcję, że neutralny angielski source nie jest powodem do abstencji, jeśli mamy mocne acoustic evidence.

Efekt: Qwen przestał zwracać wyłącznie `[]`, ale ujawnił błędne propozycje.

### 3.1.15 — evidence eligibility + diagnostyka

Dodano:

- min 2 próbki dla speaker correction,
- twardy evidence-aware `SurgicalGenderContextGuard`,
- privacy-safe diagnostykę `speakerCandidateEvidence`, np. `141:SPEAKER_43:male:984:3:true`,
- `eligibleSpeakerCandidateCount` i `eligibleSpeakerCandidateIds`.

To naprawiło błędny cue 437 (`Myślałam` → bazowe `Myślałem`), ponieważ jego speaker jest `SPEAKER_01:unknown`, więc `eligible=false`.

### 3.1.16 — deterministic gender-direction guard

W 3.1.15 cue 141 nadal był błędnie zmieniany `Chciałem → Chciałam`, mimo że diagnostyka pokazała:

`141:SPEAKER_43:male:984:3:true`

Przyczyną była luka: guard sprawdzał, czy evidence jest wiarygodne, ale nie czy **kierunek odmiany** zgadza się z płcią speakera.

3.1.16 dodał `SpeakerGenderEditDirectionGuard`:

- jeśli można rozpoznać masculine→feminine, edycja jest dozwolona tylko dla `Female`,
- jeśli można rozpoznać feminine→masculine, edycja jest dozwolona tylko dla `Male`,
- sprzeczny kierunek jest odrzucany,
- nieznany kierunek zachowuje dotychczasowe zachowanie; nie rozszerzamy blokady na formy, których jeszcze deterministycznie nie umiemy sklasyfikować.

TDD 3.1.16:

- RED: `152 pass / 2 fail` — dokładnie dwa nowe przypadki kierunku,
- GREEN: `154/154 pass`.

## Referencyjny benchmark — Better Call Saul S01E02

Plik używany do wszystkich ostatnich porównań:

`Better.Call.Saul.S01E02.1080p.x264.EAC3-SURGE.mkv`

Tryb:

- base: Argos EN→PL
- Enhanced: ON
- reviewer: Qwen3.5-9B
- backend: Vulkan

Czysty referencyjny baseline SRT:

- 471 cue
- 35,527 bytes
- SHA-256: `8bd73dffe19cc0fb09bbe63cbcf983f615e05ffedeef3cf3501cbffaa4758407`

### Historia regresji

3.1.14 finalny SRT różnił się od baseline'u w 2 cue:

- cue 141: `Chciałem...` → **błędne** `Chciałam...`
- cue 437: `Myślałem...` → **błędne** `Myślałam...`

3.1.15:

- cue 437 naprawione przez eligibility guard,
- cue 141 nadal błędne.

3.1.16, benchmark 2026-09-12 około 14:25 lokalnego czasu:

- reviewer preload: ~`4869 ms`
- diarization cache: `hit`, 641 segmentów
- audio_extract_gender: ~`2945 ms`
- speaker_gender: ~`2994 ms`
- speakerCount: `60`
- knownGenderCount: `8`
- Argos translation cache: `hit`, 471 segmentów, ~`9 ms`
- candidateCount: `100`
- knownSpeakerCandidateCount: `10`
- eligibleSpeakerCandidateCount: `9`
- eligible speaker cue IDs: `99,141,144,145,147,149,168,238,412`
- knownAddresseeCandidateCount: `1` (cue `414`)
- gender review: ~`45359 ms`
- final `completed=0`

Najważniejsze mapowania:

- cue 141 → `SPEAKER_43:male:984:3:true`
- cue 144/145/147/149 → ten sam `SPEAKER_43`, male, eligible
- cue 437 → `SPEAKER_01:unknown:0:3:false`
- cue 12 → `SPEAKER_13:female:940:1:false`

W window 7 (`141,144,145,147,149`) Qwen nadal zaproponował **5 edycji**, ale w 3.1.16 wszystkie zostały odrzucone:

- `proposed=5`
- `completed=0`
- `dropped=5`
- `dropContextGuard=2`
- `dropApplyGuard=3`
- `dropApplyInflection=3`

Finalny 3.1.16 SRT jest **byte-for-byte identyczny z czystym baseline'em Argosa**:

`SHA-256 8bd73dffe19cc0fb09bbe63cbcf983f615e05ffedeef3cf3501cbffaa4758407`

Czyli obecny safety stack zapobiegł obu wcześniej zaobserwowanym regresjom: cue 141 pozostało `Chciałem...`, cue 437 pozostało `Myślałem...`.

## Co obecnie wiemy

1. **Diarization + mapping nie były przyczyną błędu 141.** Cue 141 prawidłowo trafia do `SPEAKER_43`, a classifier daje mocne male evidence.
2. **Reviewer nadal bywa semantycznie zły.** W window 7 potrafi proponować edycje mimo jawnego male evidence.
3. **Deterministic guards są konieczne i działają.** Bez nich 3.1.14/3.1.15 psuły poprawny Argos baseline.
4. **Na referencyjnym odcinku nie udowodniliśmy jeszcze dodatniej wartości Enhanced.** 3.1.16 jest bezpieczny na tym benchmarku, ale kończy z `completed=0`; recall/realne poprawki nie są jeszcze zmierzone.
5. Classifier jest konserwatywny: większość speakerów pozostaje `Unknown`; nie obniżać ponownie progów bez nowego, kontrolowanego benchmarku.
6. Obecne `dropContextGuard` agreguje kilka powodów. Nie widać wprost w logu, czy konkretną edycję zatrzymał direction mismatch, ineligible evidence, target mismatch itd.
7. Speaker-gender analysis nadal jest wykonywane ponownie przy każdym runie (~3 s klasyfikacji + extraction), mimo że diarization i Argos baseline są już cachowane.

## Plan dalszych działań

### P0 — zachować 3.1.16 jako safety baseline

Nie luzować obecnych guardów i nie obniżać classifier thresholds. Każda dalsza zmiana review ma przechodzić regression benchmark BCS S01E02 i nie może ponownie zmieniać cue 141/437 w złą stronę.

### P1 — dokładniejsze reason codes dla odrzuconych edycji

Następny mały TDD safety/diagnostic pass:

- rozbić `dropContextGuard` na privacy-safe powody, co najmniej:
  - `ineligible_speaker_evidence`
  - `speaker_gender_direction_mismatch`
  - `target_mismatch`
  - `missing_or_invalid_addressee`
- logować cue ID + reason code, **bez tekstu napisów**.

Cel: wiedzieć dokładnie, które z 5 propozycji window 7 są błędnym kierunkiem, a które odpadają z innych powodów.

### P2 — osobny micro-review lane dla known/eligible speaker candidates

Zamiast dalej zwiększać agresywność ogólnego promptu:

1. Wykryć deterministycznie kandydatów, gdzie polska forma w baseline wygląda na gender-coded i jest sprzeczna z eligible speaker evidence.
2. Tylko takie przypadki wysyłać do bardzo wąskiego requestu Qwena (najlepiej 1 candidate/request lub mała, jednoznaczna paczka).
3. W promptcie podać wymagany docelowy gender wprost.
4. Nadal przepuścić wynik przez obecne context + direction + inflection guards.
5. Nie zmieniać baseline'u, jeśli deterministyczny pre-check nie wykazuje sprzeczności.

To powinno ograniczyć halucynacyjne propozycje typu `male → female` oraz zmniejszyć liczbę niepotrzebnych 20-window requestów.

### P3 — golden benchmark corpus

BCS S01E02 jest dobrym regression testem dla false positives, ale nie wystarcza do mierzenia jakości.

Przygotować mały, ręcznie oznaczony zestaw kilkudziesięciu cue z co najmniej:

- poprawnym masculine baseline,
- poprawnym feminine baseline,
- celowo błędnym masculine→feminine,
- celowo błędnym feminine→masculine,
- speaker-target,
- addressee-target,
- third-person forms, które nie mogą być mylone ze speakerem,
- neutralnymi formami, których nie należy zmieniać.

Mierzyć osobno:

- false positive rate,
- true corrections,
- rejected correct proposals,
- coverage classifiera.

Priorytet: **precision przed recall**. Jeden błędny automatyczny gender edit jest gorszy niż pozostawienie poprawnego/bazowego tłumaczenia bez zmiany.

### P4 — cache speaker-gender evidence

Po ustabilizowaniu correctness można dodać cache wyników acoustic gender evidence, keyed przynajmniej przez:

- identyfikację pliku/audio,
- diarization/model revision,
- classifier model revision,
- sampling/threshold schema version.

Potencjalna oszczędność na obecnym benchmarku: kilka sekund na każdy kolejny run. Nie robić tego przed P1/P2, żeby optymalizacja nie utrudniła diagnostyki correctness.

### P5 — addressee lane później

Addressee correction zostawić bardziej konserwatywną niż speaker correction. Obecnie wymaga wysokiego confidence i resolved probableAddressee. Najpierw ustabilizować speaker micro-review i benchmark corpus.

## Ważne pliki

- `src/NapisyPL.Core/Services/EnhancedTranslationPipeline.cs`
- `src/NapisyPL.Core/Services/EnhancedTranslationCache.cs`
- `src/NapisyPL.Core/ContextResolution/LocalTargetedGenderReviewService.cs`
- `src/NapisyPL.Core/ContextResolution/TargetedGenderReviewProtocol.cs`
- `src/NapisyPL.Core/ContextResolution/SurgicalGenderEditProtocol.cs`
- `src/NapisyPL.Core/ContextResolution/SpeakerGenderEvidence.cs`
- `src/NapisyPL.Core/ContextResolution/SpeakerVoiceGenderService.cs`
- `src/NapisyPL.Core/ContextResolution/GenderReviewCoverageDiagnostics.cs`
- `src/NapisyPL.Core/ContextResolution/GenderReviewCandidateSelector.cs`
- `src/NapisyPL.Core/ContextResolution/DialogueAddresseeResolver.cs`
- `src/NapisyPL.Core/Diagnostics/AppLogger.cs`

Najważniejsze testy:

- `tests/NapisyPL.Core.Tests/SpeakerGenderReviewSafetyTests.cs`
- `tests/NapisyPL.Core.Tests/SurgicalGenderContextGuardTests.cs`
- `tests/NapisyPL.Core.Tests/TargetedGenderReviewTests.cs`
- `tests/NapisyPL.Core.Tests/GenderReviewCoverageDiagnosticsTests.cs`
- `tests/NapisyPL.Core.Tests/LocalTargetedGenderReviewDiagnosticsTests.cs`

## Kluczowe milestone commits

- 3.1.12 acoustic reviewer prompt: `2c1e6a9b4b9e10aa59c305b9b27be5f2980ddfd7`
- 3.1.13 Argos cache + review coverage: branch kończył tę fazę na `fc57b6ca415a2b34ce7fcb58edfe30d55d31c20e`
- 3.1.14 explicit candidate speaker gender prompt: `3a33847e34c5bc5932e2a830cd232e7fa3041980`
- 3.1.15 speaker safety + eligibility diagnostics: `2cbc86c1c7d04e35f2904732abe8f596ce48bd17`
- 3.1.16 direction guard: `19b3987310db5bdc844518721c30c84be0757f5c`

## Zasady pracy przy kontynuacji

1. Strict TDD: RED test commit/run → minimal production change → GREEN full suite.
2. Przy każdym safety bugfixie najpierw reprodukcja testem, nie prompt-tuning „na oko”.
3. Po GREEN uruchomić pełny CI i osobny Local Translator workflow.
4. Windows build przekazywać dopiero po CI + startup smoke + weryfikacji SHA-256 artefaktu.
5. Benchmark lokalny: preferowany `Argos + Enhanced + Qwen3.5-9B + Vulkan`.
6. Nie logować tekstu napisów ani audio; diagnostyka tylko IDs, speaker IDs, confidence, sampleCount, reason codes.
7. Nie merge'ować draft PR #2 bez jawnej zgody użytkownika.
8. Nie zmieniać równocześnie classifier thresholds i reviewer behavior — benchmarki mają izolować jedną zmienną naraz.

## Najbliższy sensowny następny krok

**P1: reason-coded context guard diagnostics**, a zaraz po nim **P2: micro-review lane dla eligible speaker contradictions**.

3.1.16 należy traktować jako aktualny bezpieczny checkpoint: na referencyjnym BCS S01E02 Enhanced nie poprawia jeszcze niczego, ale również nie psuje już poprawnego baseline'u Argosa.