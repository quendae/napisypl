# Enhanced mode — status i plan kontynuacji

Aktualizacja: 2026-09-12, po wdrożeniu fundamentu SubFlow 3.2 **addressee-first**.

## Stan repo / punkt wznowienia

- Repo: `quendae/napisypl`
- Branch roboczy: `feature/local-translator-batch-v2`
- Draft PR: `#2` — **nie merge'ować bez wyraźnej decyzji użytkownika**.
- Aktualny zweryfikowany commit kodu: `e0ca55a0328fbda385c06857134cb70ae614ac8f`.
- CI: run `34694701111` — success.
- Testy .NET: `157/157` pass.
- Local Translator: run `34694701103` — success.
- Windows x64 artifact: `SubFlow-win-x64`, ID `10298403707`.
- SHA-256 artefaktu: `3891f44e774a5e062ac15bd35f4343a7e4c3968a6042dd563e76d53b15d6b990`.

Ten dokument jest checkpointem do dalszej pracy. Nowa sesja/agent powinien zacząć od tego pliku, PR #2 oraz wymienionych niżej plików kodu.

## Najważniejsza korekta celu

**Nie próbujemy przede wszystkim ustalić płci osoby mówiącej. Chcemy poprawnie ustalić, do kogo skierowana jest wypowiedź i jaki rodzaj gramatyczny powinny mieć formy odnoszące się do tej osoby.**

Przykłady najważniejszego przypadku:

- `zrobiłeś` / `zrobiłaś`
- `byłeś` / `byłaś`
- `gotowy` / `gotowa` w bezpośrednim zwrocie do rozmówcy
- inne drugioosobowe czasowniki, przymiotniki i imiesłowy zależne od płci adresata

Gender osoby mówiącej nadal może być przydatny jako profil stabilnego `SPEAKER_xx`, ale głównie dlatego, że ten sam `SPEAKER_xx` może później zostać rozpoznany jako **adresat** wypowiedzi innej osoby.

Przykład:

`SPEAKER_A mówi → resolver ustala probableAddressee=SPEAKER_B → gender evidence SPEAKER_B pochodzi z własnych wypowiedzi B → sprawdzamy formy skierowane przez A do B`.

Nigdy nie wolno używać gender `SPEAKER_A` do decyzji `zrobiłeś ↔ zrobiłaś`, jeśli forma dotyczy `SPEAKER_B`.

## Obecna architektura Enhanced

Praktyczny pipeline po 3.2:

`speaker diarization → stable speaker IDs → acoustic gender profile per speaker → Argos baseline → candidate detection → addressee resolution → candidateAddresseeGender → targeted Qwen review → deterministic guards → SRT`

Dodatkowo nadal obsługujemy prawdziwe speaker-target forms pierwszej osoby (`byłem/byłam`, `zrobiłem/zrobiłam`), ale nie są już głównym kierunkiem rozwoju.

### Bazowy translator

- Argos EN→PL jest stabilnym baseline'em.
- Persistent cache: `%LocalAppData%\SubFlow\cache\translations`.
- Cache przechowuje czysty baseline, nie wynik po review.
- Enhanced nie ma ponownie tłumaczyć całego pliku Qwenem.

### Reviewer

- Domyślny: `Qwen3.5-9B`.
- Runtime: lokalny `llama.cpp`, preferowany Vulkan.
- Runtime jest preloadowany równolegle z audio/Argos.
- Qwen ma wykonywać tylko małe, kontrolowane korekty, a nie swobodne przepisywanie napisów.

## Acoustic gender evidence

Model: SherpaOnnx audio tagging (`k2-fsa/sherpa-onnx-zipformer-small-audio-tagging-2024-04-15`).

Aggregator:

- min łącznego czasu: `1.5s`
- min combined speech evidence: `0.03`
- min normalized winner confidence: `0.75`
- min per-sample direction confidence: `0.65`
- spójność: około `2/3` próbek

Eligibility do automatycznej korekty:

- gender != `Unknown`
- confidence >= `0.85`
- sampleCount >= `2`

W 3.2 ta sama polityka jest używana dla gender profilu **resolved addressee**.

## Safety stack 3.1.x

### 3.1.14

Jawny `candidateSpeakerGender` sprawił, że Qwen zaczął proponować edycje. Ujawniło to false positives.

### 3.1.15

Dodano minimum 2 próbki, evidence-aware context guard oraz privacy-safe diagnostykę. Cue 437 przestało być błędnie zmieniane `Myślałem → Myślałam`.

### 3.1.16

Dodano deterministic gender-direction guard. Cue 141 z poprawnym `SPEAKER_43:male:984:3:true` nie może już przyjąć błędnej zmiany `Chciałem → Chciałam`.

Referencyjny benchmark BCS S01E02 po 3.1.16 wrócił byte-for-byte do czystego Argos baseline'u.

## 3.2 — addressee-first foundation

Zweryfikowany commit kodu: `e0ca55a0328fbda385c06857134cb70ae614ac8f`.

### Co zmieniono

`TargetedGenderReviewProtocol` dodaje per candidate:

- `probableAddressee`
- `candidateAddresseeGender`
- `candidateAddresseeGenderConfidence`

`candidateAddresseeGender` istnieje tylko wtedy, gdy:

1. aplikacja rozwiązała `probableAddressee`,
2. ten speaker ma known male/female evidence,
3. confidence >= 0.85,
4. sampleCount >= 2.

Prompt jawnie mówi, że:

- główną niejednoznacznością jest często płeć **osoby, do której mówi się w zdaniu**,
- `candidateAddresseeGender` pochodzi z własnych fragmentów mowy probableAddressee,
- nie jest genderem current speaker,
- przy `candidateAddresseeGender` model ma wykonać addressee-target check,
- bez `candidateAddresseeGender` nie wolno proponować `target="addressee"`.

### Deterministic guard

`SurgicalGenderContextGuard` dla `target="addressee"` wymaga teraz jednocześnie:

- confidence edycji >= 0.95,
- `probableAddressee != null`,
- probableAddressee != currentSpeaker,
- eligible gender evidence **probableAddressee**,
- kierunek odmiany zgodny z gender probableAddressee.

Czyli np.:

- resolved B=female: `zrobiłeś → zrobiłaś` może przejść,
- resolved B=male: taka sama zmiana jest twardo odrzucona,
- resolved B=male: `zrobiłaś → zrobiłeś` może przejść,
- brak eligible evidence B: żadna automatyczna addressee correction nie przejdzie.

### TDD 3.2

RED:

- testy wymagały nowego `probableAddresseeGenderEvidence`, którego produkcja nie miała — oczekiwany compile failure.

GREEN:

- `157/157` testów pass,
- Argos helper build + smoke success,
- Windows x64 publish success,
- desktop startup smoke success,
- Local Translator workflow success.

Nowe/zmienione testy:

- `tests/NapisyPL.Core.Tests/AddresseeGenderReviewSafetyTests.cs`
- `tests/NapisyPL.Core.Tests/SurgicalGenderContextGuardTests.cs`
- `tests/NapisyPL.Core.Tests/LocalTargetedGenderReviewAddresseeTests.cs`

## Referencyjny benchmark — Better Call Saul S01E02

Plik:

`Better.Call.Saul.S01E02.1080p.x264.EAC3-SURGE.mkv`

Tryb:

- Argos EN→PL
- Enhanced ON
- Qwen3.5-9B
- Vulkan

Referencyjny czysty baseline:

- 471 cue
- 35,527 bytes
- SHA-256 `8bd73dffe19cc0fb09bbe63cbcf983f615e05ffedeef3cf3501cbffaa4758407`

Benchmark 3.1.16:

- diarization cache hit, 641 segmentów,
- 60 speakerów,
- knownGenderCount=8,
- Argos cache hit, 471 segmentów w około 9 ms,
- candidateCount=100,
- eligibleSpeakerCandidateCount=9,
- knownAddresseeCandidateCount=1 — **cue 414**,
- window 7 (`141,144,145,147,149`): Qwen proposed=5, completed=0, dropped=5,
- final completed=0,
- wynik byte-for-byte identyczny z baseline'em Argosa.

Regresje, których nie wolno przywrócić:

- cue 141 musi pozostać `Chciałem...`,
- cue 437 musi pozostać `Myślałem...`.

**Najciekawszy przypadek dla benchmarku 3.2: cue 414**, bo poprzednia diagnostyka wskazywała go jako jedyny known-addressee candidate. Trzeba sprawdzić, czy jego resolved addressee ma teraz eligible gender evidence i czy Qwen dostaje `candidateAddresseeGender`.

## Plan dalszych działań

### P0 — utrzymać safety baseline

Nie luzować guardów 3.1.15/3.1.16. Każda kolejna wersja musi przejść BCS S01E02 bez ponownego popsucia cue 141/437.

### P1 — benchmark 3.2 addressee-first

Uruchomić BCS S01E02 na nowym buildzie i zbadać:

- czy cue 414 ma resolved probableAddressee,
- czy adresat ma eligible gender evidence,
- czy reviewer proponuje addressee-target edits,
- czy którekolwiek poprawne zmiany faktycznie przechodzą,
- czy finalny wynik zachowuje safety 141/437.

Jeżeli log nie daje wystarczającej widoczności, dodać privacy-safe diagnostykę `candidateId:addresseeId:gender:confidence:samples:eligible`.

### P2 — lepszy resolver adresata + reason codes

Obecny `DialogueAddresseeResolver` jest bardzo konserwatywny: głównie wzorzec `B -> A -> B`, do 2 cue i 8 sekund.

Rozwinąć go stopniowo i mierzalnie, np. o:

- dłuższe ciągi A/A vs B/B,
- bezpieczne utrzymanie aktualnego partnera dialogowego,
- rozpoznanie scen z dokładnie dwiema aktywnymi osobami,
- reset partnera po zmianie sceny / dłuższej przerwie / wejściu trzeciej osoby.

Dodać reason codes:

- `missing_addressee`
- `ineligible_addressee_evidence`
- `addressee_gender_direction_mismatch`
- `target_mismatch`
- analogiczne speaker reason codes dla drugorzędnego speaker-target lane.

Logować tylko cue ID / speaker ID / reason, bez tekstu napisów.

### P3 — addressee-first micro-review

Docelowo nie wysyłać 100 kandydatów w 20 ogólnych oknach.

Najpierw deterministycznie znaleźć cue, które:

1. zawierają formę drugiej osoby zależną od rodzaju,
2. mają resolved probableAddressee,
3. adresat ma eligible gender evidence,
4. obecna polska forma wygląda na sprzeczną z gender adresata.

Dopiero wtedy wysłać bardzo wąski request Qwena — najlepiej 1 candidate/request lub małą jednoznaczną paczkę — a odpowiedź ponownie przepuścić przez wszystkie guardy.

To jest główny plan jakościowy 3.2+, nie dalsze agresywne promptowanie speaker gender.

### P4 — golden corpus skupiony na adresacie

Przygotować ręcznie oznaczony zestaw obejmujący przede wszystkim:

- `zrobiłeś/zrobiłaś`,
- `byłeś/byłaś`,
- drugioosobowe przymiotniki i imiesłowy,
- poprawny male addressee,
- poprawny female addressee,
- celowo błędny kierunek w obie strony,
- brak pewnego adresata,
- rozmowę 3+ osób,
- speaker-target first-person jako kontrolę,
- third-person jako kontrolę negatywną.

Mierzyć precision, true corrections, rejected correct proposals i addressee-resolution coverage. Priorytet: **precision przed recall**.

### P5 — video tylko jeśli identity/addressee resolution pozostanie bottleneckiem

Jeśli audio + turn-taking nie wystarczą do ustalenia, kto jest adresatem, można używać krótkiego klipu wokół problematycznego cue i active-speaker / face tracking do ustalenia uczestników sceny oraz kto aktualnie mówi.

**Nie używać obrazu do zgadywania płci osoby po wyglądzie.** Video ma pomagać w relacji `kto mówi / kto jest rozmówcą`, a gender profilu nadal powinien pochodzić z kontrolowanego evidence lub jednoznacznego kontekstu.

Nie analizować całego filmu klatka-po-klatce, dopóki benchmark nie pokaże, że resolver adresata jest głównym ograniczeniem.

### P6 — cache acoustic gender evidence

Dopiero po ustabilizowaniu correctness. Cache powinien uwzględniać plik/audio, diarization/model revision, classifier revision i sampling/threshold schema version.

## Ważne pliki

- `src/NapisyPL.Core/ContextResolution/DialogueAddresseeResolver.cs`
- `src/NapisyPL.Core/ContextResolution/TargetedGenderReviewProtocol.cs`
- `src/NapisyPL.Core/ContextResolution/LocalTargetedGenderReviewService.cs`
- `src/NapisyPL.Core/ContextResolution/SurgicalGenderEditProtocol.cs`
- `src/NapisyPL.Core/ContextResolution/SpeakerGenderEvidence.cs`
- `src/NapisyPL.Core/ContextResolution/SpeakerVoiceGenderService.cs`
- `src/NapisyPL.Core/ContextResolution/GenderReviewCandidateSelector.cs`
- `src/NapisyPL.Core/ContextResolution/GenderReviewCoverageDiagnostics.cs`
- `src/NapisyPL.Core/Services/EnhancedTranslationPipeline.cs`
- `src/NapisyPL.Core/Services/EnhancedTranslationCache.cs`
- `src/NapisyPL.Core/Diagnostics/AppLogger.cs`

## Kluczowe milestone commits

- 3.1.12 acoustic reviewer: `2c1e6a9b4b9e10aa59c305b9b27be5f2980ddfd7`
- 3.1.13 Argos cache + coverage: `fc57b6ca415a2b34ce7fcb58edfe30d55d31c20e`
- 3.1.14 explicit speaker signal: `3a33847e34c5bc5932e2a830cd232e7fa3041980`
- 3.1.15 speaker safety/eligibility: `2cbc86c1c7d04e35f2904732abe8f596ce48bd17`
- 3.1.16 direction guard: `19b3987310db5bdc844518721c30c84be0757f5c`
- 3.2 addressee-first foundation: `e0ca55a0328fbda385c06857134cb70ae614ac8f`

## Zasady kontynuacji

1. Strict TDD: RED → potwierdzony fail → minimal production change → pełny GREEN.
2. Nie mieszać zmian classifier thresholds z reviewer/resolver behavior w jednym benchmarku.
3. Nie logować treści napisów ani audio; tylko IDs, confidence, sampleCount, eligibility, reason codes.
4. Każda automatyczna korekta addressee ma być autoryzowana przez resolved addressee, nie current speaker.
5. Po kodzie: pełny CI + Local Translator + Windows startup smoke.
6. Preferowany benchmark: `Argos + Enhanced + Qwen3.5-9B + Vulkan`.
7. Nie merge'ować draft PR #2 bez jawnej zgody użytkownika.

## Najbliższy krok

**Benchmark 3.2 na BCS S01E02, ze szczególną obserwacją cue 414.** Następnie — zależnie od wyniku — privacy-safe addressee diagnostics i addressee-first micro-review/resolver improvements.
