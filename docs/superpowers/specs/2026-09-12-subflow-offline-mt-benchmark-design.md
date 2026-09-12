# SubFlow — benchmark większych translatorów offline EN→PL

## Cel

Dodać do SubFlow powtarzalny benchmark większych, klasycznych modeli machine translation działających całkowicie offline po jednorazowym pobraniu modelu. Benchmark ma odpowiedzieć, czy warto zastąpić obecny lekki Argos EN→PL (~90 MB) większym modelem bazowym, zanim zmienimy domyślną architekturę produktu.

Porównujemy cztery warianty na identycznym wejściu:

1. obecny `Argos EN→PL` — baseline,
2. `Firefox/Bergamot EN→PL`,
3. `OPUS-MT / Marian EN→PL`,
4. `NLLB-200 distilled 600M` — benchmark jakościowy, nie kandydat produkcyjny.

Enhanced, diarization, speaker gender, addressee resolver i Qwen reviewer nie są częścią różnic między backendami. Najpierw mierzymy surowe MT; najlepszy wariant można później przepuścić przez niezmieniony Enhanced pipeline.

## Zakres decyzji

Ta zmiana nie wybiera nowego domyślnego translatora. Dostarcza narzędzia i dane do decyzji.

Argos pozostaje:
- domyślnym backendem,
- baseline benchmarku,
- bez zmiany zachowania w istniejących jobach,
- objęty wszystkimi istniejącymi regresjami 3.2.x, w szczególności poprawkami soft-wrap/dialogue-turn z 3.2.3–3.2.5.

Nowe modele są eksperymentalne i pobierane wyłącznie na żądanie. Nie zwiększają bazowej paczki Windows SubFlow.

## Wspólny preprocessing — warunek uczciwego benchmarku

Obecnie logika przygotowania tekstu do klasycznego MT znajduje się wewnątrz `ArgosOfflineProvider` (`BuildLogicalLines`, wykrywanie dialogue-turn oraz sentence split). Benchmark nie może porównywać różnych sposobów dzielenia napisów.

Logikę tę wyciągamy do wspólnego komponentu, np. `MachineTranslationTextPreprocessor`, używanego przez:
- Argos,
- Bergamot,
- OPUS/Marian,
- NLLB.

Kontrakt preprocessor-a:
- zachowuje soft-wrap jako jedno logiczne zdanie,
- rozdziela prawdziwe tury dialogowe również po tagach `<i>`, `{...}` itp.,
- zachowuje dotychczasowe reguły skrótów i sentence split,
- nie zmienia treści ani kolejności segmentów,
- mapuje części tłumaczenia z powrotem do oryginalnego cue deterministycznie.

Istniejące regresje 3.2.5 muszą przejść bez zmian semantycznych. Cues 141, 253 i 437 z benchmarku Better Call Saul pozostają sentinelami jakości/regresji.

## Wspólny kontrakt lokalnego MT

Dodajemy neutralny wobec silnika interfejs klienta, np.:

```csharp
public interface IOfflineMachineTranslatorClient
{
    string BackendId { get; }
    string DisplayName { get; }
    Task EnsureReadyAsync(IProgress<string>? status = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
    OfflineMachineTranslatorInfo GetInfo();
}
```

`OfflineMachineTranslatorInfo` zawiera co najmniej:
- backend id,
- model id/version,
- źródło modelu,
- lokalny rozmiar modelu w bajtach,
- engine/runtime version,
- device (`cpu` w pierwszej wersji benchmarku),
- informacje licencyjne potrzebne do raportu.

`ArgosTranslationRuntimeManager` może nadal zachować swój publiczny kontrakt kompatybilności, ale provider benchmarkowy używa wspólnej abstrakcji albo cienkiego adaptera. Nie wykonujemy dużego refactoru niezwiązanego z benchmarkiem.

## Backend 1 — Argos

Obecna implementacja pozostaje źródłem baseline.

Nie zmieniamy:
- model download/install flow,
- protokołu JSONL,
- cache użytkownika,
- provider display name,
- domyślnego wyboru w UI.

Jedyne funkcjonalne współdzielenie to przeniesienie preprocessingu do wspólnego komponentu z testami zachowującymi byte-for-byte dotychczasową kolejność requestów do Argosa dla istniejących przypadków testowych.

## Backend 2 — Firefox/Bergamot

Używamy rzeczywistego `bergamot-translator`, a nie modelu Firefox uruchomionego przez inną bibliotekę. Dzięki temu benchmark obejmuje ten sam typ zoptymalizowanego inference, z którego wywodzi się Firefox Translations.

Silnik:
- projekt `browsermt/bergamot-translator`,
- licencja silnika MPL-2.0,
- wersja/runtime przypięte w manifeście benchmarku,
- Windows x64 CPU,
- proces helpera utrzymywany przy życiu przez cały job, aby nie ładować modelu dla każdego cue.

Model:
- English → Polish z publicznego registry modeli Mozilla/Firefox Translations,
- pobierany przy pierwszym wyborze backendu,
- źródłowy URL, hash i wersja rozwiązane w momencie instalacji i zapisane w lokalnym manifeście,
- pliki modelu nie są bundlowane z głównym ZIP-em SubFlow.

Ze względu na trwającą w 2026 dyskusję Mozilli o jednoznacznym licencjonowaniu najnowszych generacji wag, benchmark nie redystrybuuje bieżących wag Firefoxa we własnym release. Pobieramy je bezpośrednio ze źródła Mozilli i zapisujemy źródło/hash w raporcie. Decyzja o ewentualnej przyszłej redystrybucji jest poza zakresem tego benchmarku.

Helper komunikuje się z aplikacją przez ten sam styl JSONL co Argos: load/ready/translate/progress/segment/complete/error/shutdown. Tekst napisów nie trafia do logów diagnostycznych.

## Backend 3 — OPUS-MT / Marian EN→PL

Używamy oryginalnego checkpointu Marian/Tatoeba English→Polish, nie aktualnego portu `gsarti/opus-mt-tc-en-pl` jako źródła wag do benchmarku. Karta tego portu wskazuje, że wersja HF jest obecnie niefunkcjonalna i odsyła do oryginalnego checkpointu.

Źródło/model:
- Tatoeba/OPUS `eng-pol`, release 2021-02-19,
- model typu Marian transformer,
- source `en`, target `pl`,
- model instalowany on-demand i pinowany w lokalnym manifeście przez URL/hash.

Runtime:
- preferujemy natywny Marian/Bergamot-compatible runtime CPU, jeśli checkpoint jest bezpośrednio kompatybilny po minimalnej, udokumentowanej konwersji/config patch,
- jeśli wymagany będzie osobny Marian executable, pozostaje on osobnym helperem za tym samym `IOfflineMachineTranslatorClient`, dzięki czemu nie wpływa na provider ani benchmark harness.

Nie używamy chat-LLM ani promptów.

## Backend 4 — NLLB-200 distilled 600M

Model:
- `facebook/nllb-200-distilled-600M`,
- source language `eng_Latn`,
- target language `pol_Latn`,
- około 2.48 GB repozytorium / 2.46 GB głównej wagi FP32 w aktualnym wydaniu,
- limit wejścia respektuje ograniczenia modelu; nasze krótkie części subtitle pozostają znacznie poniżej limitu.

Runtime benchmarkowy:
- lokalny Python helper,
- `transformers` w wersji przypiętej do gałęzi 4.x (model card wskazuje, że high-level translation pipeline nie jest obsługiwany w Transformers 5.x),
- bezpośrednie `AutoTokenizer` + `AutoModelForSeq2SeqLM`,
- CPU jako pierwszy wspólny punkt porównania,
- model pobierany on-demand do osobnego cache SubFlow.

NLLB jest wyłącznie benchmark-only. Model ma licencję CC-BY-NC-4.0, a jego model card opisuje go jako research model nieprzeznaczony do production deployment. Nie staje się domyślnym providerem produktu i nie jest bundlowany w release SubFlow.

## Zarządzanie assetami

Każdy nowy backend ma osobny katalog pod `%LocalAppData%\SubFlow\offline-mt\`:

```text
offline-mt/
  bergamot/
    runtime/
    model/
    manifest.json
  opus-marian/
    runtime/
    model/
    manifest.json
  nllb-600m/
    runtime/
    model/
    manifest.json
```

Manifest zawiera:
- backend id,
- engine version,
- model id/version,
- source URL(s),
- SHA-256 pobranych assetów,
- installed size,
- installation timestamp,
- license identifier / source note.

Instalacja korzysta z tymczasowego katalogu i atomowego przeniesienia po poprawnej weryfikacji. Przerwany/niepełny download nie jest traktowany jako zainstalowany model.

## ProviderFactory i UI

Do listy providerów dodajemy jawnie eksperymentalne pozycje:

- `Local Firefox/Bergamot (offline, benchmark)`
- `Local OPUS/Marian (offline, benchmark)`
- `Local NLLB-600M (offline, benchmark)`

Argos nadal widnieje jako zwykły `Local Argos (offline)` i pozostaje domyślnym lokalnym wyborem.

Po wybraniu eksperymentalnego backendu UI:
- nie pokazuje API key/Base URL,
- pokazuje status modelu i rozmiar pobrania przed instalacją, gdy informacja jest dostępna,
- pozwala `Pobierz / zainstaluj`, `Przeinstaluj`,
- oznacza NLLB jako `Benchmark only · CC-BY-NC`,
- nie pobiera nic automatycznie tylko dlatego, że aplikacja wystartowała.

Nie tworzymy osobnego rozbudowanego ekranu ustawień modeli w tej iteracji.

## Benchmark harness

Dodajemy developerski benchmark uruchamialny niezależnie od Enhanced review. Przyjmuje istniejący plik SRT/tekstowe segmenty i listę backendów.

Dla każdego backendu wykonuje:
1. warm/cold runtime setup jest raportowany osobno,
2. ten sam preprocessing,
3. to samo wejście i kolejność segmentów,
4. tłumaczenie,
5. rekonstrukcję SRT,
6. zapis raportu.

Artefakty jednego runu:

```text
benchmark-YYYYMMDD-HHMMSS/
  argos.srt
  bergamot.srt
  opus-marian.srt
  nllb-600m.srt
  report.json
  report.txt
```

Raport per backend zawiera:
- backend/model/runtime version,
- source/model hash,
- model size bytes,
- cold load time,
- translation time,
- total time,
- segment count,
- character count,
- segments/s,
- characters/s,
- peak working set, jeśli platforma pozwala zmierzyć bez dodatkowego ciężkiego dependency,
- success/failure i znormalizowany error code.

Raport globalny zawiera hash wejścia oraz wersję preprocessora, aby późniejsze benchmarki były porównywalne.

## Benchmark Better Call Saul

Pierwszy realny benchmark wykonujemy na tym samym Better Call Saul S01E02, który jest używany w regresjach 3.2.x.

Weryfikacja ręczna obejmuje przede wszystkim:
- idiomy/slang,
- naturalność polskiej składni,
- zachowanie pełnej treści,
- błędne osoby/liczby gramatyczne,
- błędne rodzaje,
- nazwy własne,
- markup/dialogue turns,
- cues, które wcześniej ujawniły problemy preprocessingu.

Stałe sentinele:
- cue 141 — nie może zostać sztucznie przełączony na żeński rodzaj,
- cue 253 — musi zachować 3. osobę liczby mnogiej; brak regresji `Zapomniałaś/Zapomniałeś`,
- cue 437 — brak fałszywej feminizacji.

Dodatkowo kontrolujemy cue 30 i 137 pod kątem zachowania obu tur dialogu/pełnej treści.

Automatyczny BLEU/COMET nie jest kryterium wyboru w tej iteracji. Finalna decyzja opiera się na jakości rzeczywistych napisów + koszcie runtime/modelu.

## Cache

Cache tłumaczeń musi rozróżniać backend/model/preprocessor version. Wynik Argosa nie może zostać użyty jako cache hit dla Bergamot/OPUS/NLLB ani odwrotnie.

Klucz cache zawiera co najmniej:
- backend id,
- model version/hash,
- preprocessor schema version,
- source text hash,
- source/target language.

Zmiana backendu zawsze daje osobny namespace cache.

## Logowanie

Dodajemy eventy diagnostyczne bez treści subtitle:
- `offline_mt_install_start/end`,
- `offline_mt_runtime_start/ready/stop`,
- `offline_mt_model_info`,
- `offline_mt_benchmark_start/end`,
- `offline_mt_benchmark_backend_start/end`.

Logujemy wersje, rozmiary, czasy, liczbę segmentów/znaków i błędy. Nie logujemy pełnych zdań ani wygenerowanych tłumaczeń.

## Błędy i izolacja

Awaria jednego eksperymentalnego backendu nie uszkadza Argosa ani innych backendów.

Benchmark:
- zapisuje failure konkretnego backendu,
- kontynuuje pozostałe backendy,
- nie usuwa udanych wyników,
- kończy raportem częściowym, jeśli co najmniej jeden backend zadziałał.

Provider użyty do zwykłego tłumaczenia zachowuje normalne zachowanie fail-fast dla aktualnego pliku.

## TDD i testy

Implementacja przebiega RED → GREEN małymi commitami.

Testy obowiązkowe:
- wspólny preprocessor zachowuje wszystkie obecne testy Argosa,
- tagged dialogue turns nadal są rozdzielane,
- soft-wrap cue253-like pozostaje połączony,
- każdy provider mapuje 1:1 wejścia i wyjścia oraz odrzuca mismatch count,
- factory tworzy właściwy backend,
- model asset manager nie uznaje niepełnego pobrania za instalację,
- manifest zapisuje model/runtime/hash/license metadata,
- cache izoluje backendy i wersje modeli,
- benchmark daje każdemu backendowi identyczne przygotowane części,
- failure jednego backendu nie przerywa całego benchmarku,
- report JSON/TXT zawiera wymagane metryki,
- logi nie zawierają treści subtitle,
- NLLB jest oznaczony benchmark-only,
- istniejące 3.2.5 regression tests przechodzą bez zmian.

Integracyjne smoke testy z realnymi dużymi modelami nie są częścią zwykłego szybkiego CI. Trafiają do osobnego manual/workflow benchmark, aby standardowe CI nie pobierało kilku GB modeli przy każdym commicie.

Standardowe CI testuje helper protocol i provider/runtime przez fake/stub backendy. Osobny workflow Windows x64 może pobrać wybrany model i wykonać krótki realny smoke test jawnie/manualnie.

## Kryteria akceptacji

Zmiana jest gotowa do benchmarku użytkownika, gdy:

1. wszystkie dotychczasowe testy, w tym regresje 3.2.5, przechodzą,
2. Argos zachowuje dotychczasowe wyniki request-preprocessing dla fixture'ów,
3. każdy z trzech nowych backendów może zostać zainstalowany na żądanie albo zwraca jednoznaczny, raportowalny błąd instalacji,
4. benchmark może uruchomić dowolny podzbiór czterech backendów,
5. każdy udany backend tworzy osobny SRT,
6. `report.json` i `report.txt` zawierają wersję/model/hash/rozmiar/czasy/throughput,
7. cache nie miesza wyników różnych backendów,
8. standardowy Windows build nie bundluje wielogigabajtowych modeli,
9. Better Call Saul benchmark można powtórzyć bez zmiany Enhanced/Qwen logic,
10. PR pozostaje draft i nie jest merge'owany bez osobnej zgody użytkownika.

## Poza zakresem

- automatyczna zmiana domyślnego providera z Argosa,
- usuwanie Argosa,
- GPU/ROCm/DirectML tuning,
- NLLB 1.3B / MADLAD,
- COMET/BLEU jako automatyczne kryterium jakości,
- retraining/fine-tuning modeli,
- redystrybucja najnowszych wag Firefox bez osobnej weryfikacji licencji,
- zmiany w diarization/gender/addressee/Qwen review,
- merge PR #2.