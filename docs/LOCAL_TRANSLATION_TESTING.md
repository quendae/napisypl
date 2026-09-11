# Local translation test checklist

This branch adds local EN→PL translation through Argos/CTranslate2 and changes Enhanced mode so the local Qwen model reviews only selected gender-sensitive subtitle lines after the base translation.

## Local Argos (offline)

1. Start the Windows build with `NapisyPL.exe` and keep `SubFlow.ArgosHelper.exe` next to it.
2. Choose **Local Argos (offline)**.
3. Load an English SRT or a video with an English text subtitle track.
4. Start translation.
5. On first use, SubFlow downloads `translate-en_pl-1_9.argosmodel` (~67 MB) into `%LocalAppData%\SubFlow\argos` and shows download progress.
6. Later runs reuse the downloaded model and the helper process keeps the CTranslate2 model loaded for the current app session.
7. Verify that the output is UTF-8 Polish and timestamps/order are unchanged.

## Folder mode

1. Drop or choose a folder containing supported videos/subtitle files.
2. Existing `.pl.srt` / `.pl.txt` outputs must not be queued again.
3. Processing is sequential.
4. Files without usable text subtitles are skipped and logged instead of aborting the entire folder.

## Enhanced

Enhanced is meaningful for video input because it uses audio diarization to associate subtitle cues with stable speaker IDs.

The expected pipeline is:

`speaker diarization → base translator (Argos/DeepL/etc.) → candidate detection → Qwen 1.7B targeted review → SRT`

Qwen should **not** translate the full subtitle file and should not run when no gender-sensitive candidates are found. The log contains an `enhanced_phase` / `gender_review` entry with `candidateCount` and the number of changed cues.

Useful cases to inspect manually include Polish forms such as `byłem/byłam`, `zrobiłeś/zrobiłaś`, `gotowy/gotowa`, `zmęczony/zmęczona`, and similar speaker-dependent forms.

## Logs

Use **Otwórz log** in the app. Logs intentionally record metadata rather than subtitle contents or API keys. For local translation, check provider/batch timing and Enhanced `candidateCount` values.

## CI integration smoke

The feature branch CI builds the packaged Windows helper and downloads the real Argos EN→PL 1.9 model. It then sends `Hello, how are you?` through the packaged executable and requires a non-empty Polish translation before publishing the Windows artifact.
