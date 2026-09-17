using System.Text.RegularExpressions;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

public sealed record DeterministicGenderCueDiagnostic(
    int CueId,
    string? CurrentSpeaker,
    bool HasGenderedCandidate,
    string? CandidateWord,
    string Resolver,
    string ReasonCode,
    SpeakerVoiceGender TargetGender,
    double Confidence,
    bool GatePassed,
    string? MatchedWord,
    string? Replacement,
    bool Changed);

public sealed partial class DeterministicGenderReviewService
{
    private const double MinimumAddresseeConfidence = 0.94;
    private const double MinimumCueGenderConfidence = 0.82;
    private const double MinimumCueCombinedEvidence = 0.03;
    private const double MinimumCueDurationSeconds = 0.75;

    private readonly PhraseLexicon _phraseLexicon;

    // Hand-written irregular entries. They override the generated SGJP lexicon,
    // which supplies every other verb.
    private static readonly IReadOnlyDictionary<string, string> HandSpeakerMaleToFemale =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mógłbym"] = "mogłabym",
            ["powinienem"] = "powinnam",
            ["poszedłem"] = "poszłam",
            ["wyszedłem"] = "wyszłam",
            ["przyszedłem"] = "przyszłam",
            ["odszedłem"] = "odeszłam",
            ["płynąłem"] = "płynęłam",
            ["wziąłem"] = "wzięłam",
            ["zacząłem"] = "zaczęłam",
            ["jadłem"] = "jadłam",
            ["szedłem"] = "szłam"
        };

    /// <summary>
    /// Whitelisted past-tense verb stems. A bare "-łem/-łam" or "-łeś/-łaś" suffix
    /// rule would also rewrite present-tense forms and nouns, so both the speaker
    /// and the addressee direction only fire on a known verb stem.
    /// </summary>
    private static readonly HashSet<string> PastVerbStems = new(StringComparer.Ordinal)
    {
        "by", "bra", "chcia", "czyta", "da", "dosta", "działa", "gra", "jecha",
        "kaza", "kocha", "kupi", "mia", "mówi", "myśla", "napisa", "obejrza",
        "pamięta", "pocałowa", "pracowa", "pyta", "robi", "siedzia", "słysza", "sta",
        "szuka", "uderzy", "umia", "widzia", "wiedzia", "wysyła", "zachorowa", "założy",
        "znalaz", "zobaczy", "zosta", "zrobi",
        // High-frequency subtitle verbs the original list omitted.
        "chodzi", "czu", "dzwoni", "mog", "musia", "obieca", "odpowiedzia",
        "patrzy", "poczeka", "powiedzia", "powtórzy", "prosi", "próbowa",
        "przypomnia", "rozumia", "skończy", "spa", "stara", "straci", "trzyma",
        "uwierzy", "wróci", "wygra", "zaczeka", "zapomnia", "zapyta", "zauważy",
        "zdecydowa", "zostawi", "zrozumia"
    };

    private static readonly IReadOnlyDictionary<string, string> HandAddresseeMaleToFemale =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mógłbyś"] = "mogłabyś",
            ["powinieneś"] = "powinnaś",
            ["poszedłeś"] = "poszłaś",
            ["wyszedłeś"] = "wyszłaś",
            ["przyszedłeś"] = "przyszłaś",
            ["odszedłeś"] = "odeszłaś"
        };

    private static readonly IReadOnlyDictionary<string, string> SpeakerMaleToFemale =
        GenderFormLexicon.Merge(HandSpeakerMaleToFemale, GenderFormLexicon.Default.SelfMaleToFemale);
    private static readonly IReadOnlyDictionary<string, string> SpeakerFemaleToMale =
        GenderFormLexicon.Merge(Reverse(HandSpeakerMaleToFemale), GenderFormLexicon.Default.SelfFemaleToMale);
    private static readonly IReadOnlyDictionary<string, string> AddresseeMaleToFemale =
        GenderFormLexicon.Merge(HandAddresseeMaleToFemale, GenderFormLexicon.Default.AddresseeMaleToFemale);
    private static readonly IReadOnlyDictionary<string, string> AddresseeFemaleToMale =
        GenderFormLexicon.Merge(Reverse(HandAddresseeMaleToFemale), GenderFormLexicon.Default.AddresseeFemaleToMale);

    private static readonly string[] GenderedSuffixes =
    [
        "łabym", "łabyś", "łbym", "łbyś", "łam", "łem", "łaś", "łeś"
    ];

    public DeterministicGenderReviewService(PhraseLexicon? phraseLexicon = null)
    {
        _phraseLexicon = phraseLexicon ?? PhraseLexicon.LoadDefault();
    }

    public IReadOnlyList<SubtitleCue> Review(
        IReadOnlyList<SubtitleCue> source,
        IReadOnlyList<SubtitleCue> translated,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        IReadOnlyDictionary<string, SpeakerGenderEvidence> speakerGenderEvidence,
        IReadOnlyDictionary<int, CueVoiceGenderEvidence>? cueGenderEvidence = null,
        ICollection<DeterministicGenderCueDiagnostic>? diagnostics = null,
        bool hardVoiceTurnOnly = false,
        IReadOnlyDictionary<int, SpeakerVoiceGender>? labeledCueGender = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(translated);
        ArgumentNullException.ThrowIfNull(cueSpeakers);
        ArgumentNullException.ThrowIfNull(speakerGenderEvidence);

        if (source.Count == 0 || translated.Count == 0)
            return translated.ToArray();

        var sourceById = source.ToDictionary(cue => cue.Index);
        var result = new List<SubtitleCue>(translated.Count);
        var localCueGender = cueGenderEvidence ?? new Dictionary<int, CueVoiceGenderEvidence>();
        var resolvedAddresseeGender = new Dictionary<int, SpeakerVoiceGender>();

        foreach (var cue in translated)
        {
            if (!sourceById.TryGetValue(cue.Index, out var sourceCue))
            {
                result.Add(cue);
                continue;
            }

            cueSpeakers.TryGetValue(cue.Index, out var currentSpeaker);
            var originalText = cue.Text;
            var candidateWord = FindGenderedCandidate(originalText);
            var text = originalText;

            var resolver = "none";
            var reasonCode = "unresolved";
            var targetGender = SpeakerVoiceGender.Unknown;
            var confidence = 0d;
            var gatePassed = false;

            if (HasMultipleDialogueLines(sourceCue.Text) || HasMultipleDialogueLines(originalText))
            {
                if (diagnostics is not null && candidateWord is not null)
                {
                    diagnostics.Add(new DeterministicGenderCueDiagnostic(
                        cue.Index,
                        currentSpeaker,
                        true,
                        candidateWord,
                        "none",
                        "multiple_dialogue_lines",
                        SpeakerVoiceGender.Unknown,
                        0,
                        false,
                        null,
                        null,
                        false));
                }

                result.Add(cue);
                continue;
            }

            if (hardVoiceTurnOnly)
            {
                resolver = "hard_voice_sequence";

                // A stable diarized speaker profile rewrites the speaker's own forms.
                // Per-cue audio alone never does; it only confirms a weaker profile
                // or vetoes one it contradicts.
                // An SDH speaker label ("JACLYN:") names the speaker outright and beats audio.
                var labeled = labeledCueGender is not null &&
                              labeledCueGender.TryGetValue(cue.Index, out var labelGender) &&
                              labelGender != SpeakerVoiceGender.Unknown;
                var hasCurrentSelfGender = TryGetHardVoiceSpeakerSelfGenderSafely(
                    currentSpeaker,
                    speakerGenderEvidence,
                    out var currentSelfGender,
                    out var currentSelfConfidence);
                if (labeled)
                {
                    hasCurrentSelfGender = true;
                    currentSelfGender = labeledCueGender![cue.Index];
                    currentSelfConfidence = 0.99;
                }

                // A weaker profile is still enough when this very cue confidently
                // agrees with it. Pitch alone is not: boys and falsetto read as female.
                if (!hasCurrentSelfGender)
                {
                    hasCurrentSelfGender = TryGetCueConfirmedSelfGender(
                        currentSpeaker,
                        speakerGenderEvidence,
                        localCueGender,
                        cue.Index,
                        out currentSelfGender,
                        out currentSelfConfidence);
                }

                // A cluster can merge two voices. When the cue itself, or the same
                // label's continuation, sounds like the other gender, the profile is
                // not describing this line.
                var selfContradicted = hasCurrentSelfGender && !labeled &&
                    SelfGenderContradictedByCuePitch(source, cueSpeakers, localCueGender, cue.Index, currentSelfGender);
                if (selfContradicted)
                    hasCurrentSelfGender = false;

                if (hasCurrentSelfGender)
                {
                    text = FixSpeakerAgreement(sourceCue.Text, text, currentSelfGender);
                    text = FixFirstPersonPredicateAgreement(sourceCue.Text, text, currentSelfGender);
                    targetGender = currentSelfGender;
                    confidence = currentSelfConfidence;
                    gatePassed = true;
                }

                var runSelfChanged = false;
                if (!hasCurrentSelfGender &&
                    EnglishFirstPersonPronounRegex().IsMatch(sourceCue.Text) &&
                    TryGetSameSpeakerRunFeminineSelf(source, translated, cueSpeakers, localCueGender, cue.Index))
                {
                    var runText = FixSpeakerAgreement(sourceCue.Text, text, SpeakerVoiceGender.Female);
                    runText = FixFirstPersonPredicateAgreement(sourceCue.Text, runText, SpeakerVoiceGender.Female);
                    if (!string.Equals(runText, text, StringComparison.Ordinal))
                    {
                        text = runText;
                        runSelfChanged = true;
                        targetGender = SpeakerVoiceGender.Female;
                        gatePassed = true;
                    }
                }

                var hardVoiceTurn = HardVoiceTurnResolver.Resolve(
                    source,
                    cueSpeakers,
                    localCueGender,
                    cue.Index);

                if (!hardVoiceTurn.IsResolved)
                {
                    var between = HardVoiceTurnResolver.ResolveAddresseeBetweenSameSpeaker(
                        source,
                        cueSpeakers,
                        localCueGender,
                        speakerGenderEvidence,
                        cue.Index);
                    if (between.IsResolved)
                        hardVoiceTurn = between;
                }

                var addresseeResolved = false;
                if (hardVoiceTurn.IsResolved)
                {
                    var stableTargetGender = SpeakerVoiceGender.Unknown;
                    var hasStableTargetGender =
                        !string.IsNullOrWhiteSpace(hardVoiceTurn.TargetSpeakerId) &&
                        TryEligibleGender(
                            hardVoiceTurn.TargetSpeakerId!,
                            speakerGenderEvidence,
                            out stableTargetGender) &&
                        stableTargetGender == hardVoiceTurn.TargetGender;

                    var targetHasProfile =
                        !string.IsNullOrWhiteSpace(hardVoiceTurn.TargetSpeakerId) &&
                        TryEligibleGender(hardVoiceTurn.TargetSpeakerId!, speakerGenderEvidence, out _);

                    // Without any stable profile for the target, a confident pitch
                    // turn is the evidence. A profile that exists and disagrees still
                    // blocks the rewrite.
                    if (!hasStableTargetGender &&
                        !targetHasProfile &&
                        hardVoiceTurn.Confidence >= HardVoiceTurnResolver.PitchOverridesDiarizationConfidence)
                    {
                        hasStableTargetGender = true;
                        stableTargetGender = hardVoiceTurn.TargetGender;
                    }

                    if (!hasStableTargetGender)
                    {
                        reasonCode = "target_speaker_gender_unconfirmed";
                        targetGender = SpeakerVoiceGender.Unknown;
                        confidence = hardVoiceTurn.Confidence;
                        gatePassed = false;
                    }
                    else if (ShouldPreserveStrongAddresseeGender(text, stableTargetGender))
                    {
                        reasonCode = "intra_cue_addressee_gender_conflict";
                        targetGender = SpeakerVoiceGender.Unknown;
                        confidence = hardVoiceTurn.Confidence;
                        gatePassed = false;
                    }
                    else
                    {
                        reasonCode = hardVoiceTurn.ReasonCode;
                        targetGender = stableTargetGender;
                        confidence = hardVoiceTurn.Confidence;
                        gatePassed = true;
                        text = FixAddresseeAgreement(sourceCue.Text, text, stableTargetGender);
                        addresseeResolved = true;
                        resolvedAddresseeGender[cue.Index] = stableTargetGender;
                    }
                }
                else
                {
                    var speakerSelfChanged = !string.Equals(text, originalText, StringComparison.Ordinal);
                    reasonCode = runSelfChanged
                        ? "speaker_from_same_speaker_run"
                        : speakerSelfChanged && labeled
                            ? "speaker_label"
                        : speakerSelfChanged && hasCurrentSelfGender
                            ? "current_cue_gender"
                            : selfContradicted && candidateWord is not null
                                ? "self_gender_contradicted_by_cue_pitch"
                                : hardVoiceTurn.ReasonCode;
                    gatePassed = hasCurrentSelfGender || gatePassed;
                }

                if (!addresseeResolved &&
                    EnglishSecondPersonPronounRegex().IsMatch(sourceCue.Text) &&
                    TryGetSameSpeakerRunAddresseeGender(
                        source,
                        translated,
                        cueSpeakers,
                        localCueGender,
                        resolvedAddresseeGender,
                        cue.Index,
                        out var runGender) &&
                    !ShouldPreserveStrongAddresseeGender(text, runGender))
                {
                    var runText = FixAddresseeAgreement(sourceCue.Text, text, runGender);
                    if (!string.Equals(runText, text, StringComparison.Ordinal))
                    {
                        text = runText;
                        reasonCode = "addressee_from_same_speaker_run";
                        targetGender = runGender;
                        gatePassed = true;
                    }
                }
            }
            else
            {
                var speakerGender = SpeakerVoiceGender.Unknown;
                var labeledSpeaker = labeledCueGender is not null &&
                                     labeledCueGender.TryGetValue(cue.Index, out speakerGender) &&
                                     speakerGender != SpeakerVoiceGender.Unknown;
                if (labeledSpeaker ||
                    (!string.IsNullOrWhiteSpace(currentSpeaker) &&
                     TryEligibleGender(currentSpeaker!, speakerGenderEvidence, out speakerGender)))
                {
                    text = FixSpeakerAgreement(sourceCue.Text, text, speakerGender);
                    text = FixFirstPersonPredicateAgreement(sourceCue.Text, text, speakerGender);
                    if (!string.Equals(text, originalText, StringComparison.Ordinal))
                    {
                        resolver = "speaker_self";
                        reasonCode = labeledSpeaker ? "speaker_label" : "current_speaker_gender";
                        targetGender = speakerGender;
                        confidence = labeledSpeaker ? 0.99 : speakerGenderEvidence[currentSpeaker!].Confidence;
                        gatePassed = true;
                    }
                }

                var localTurn = LocalTurnGenderResolver.Resolve(
                    source,
                    cueSpeakers,
                    localCueGender,
                    cue.Index,
                    speakerGenderEvidence);
                if (localTurn.IsResolved)
                {
                    resolver = "local_turn";
                    reasonCode = localTurn.ReasonCode;
                    targetGender = localTurn.Gender;
                    confidence = localTurn.Confidence;
                    gatePassed = localTurn.Confidence >= MinimumAddresseeConfidence;
                }
                else
                {
                    if (string.Equals(text, originalText, StringComparison.Ordinal))
                        reasonCode = localTurn.ReasonCode;
                }

                if (localTurn.IsResolved && localTurn.Confidence >= MinimumAddresseeConfidence)
                {
                    text = FixAddresseeAgreement(sourceCue.Text, text, localTurn.Gender);
                }
                else if (!string.IsNullOrWhiteSpace(currentSpeaker))
                {
                    var addressee = DialogueAddresseeResolver.ResolveDetailed(source, cueSpeakers, cue.Index);
                    if (addressee.IsResolved)
                    {
                        resolver = "dialogue_addressee";
                        reasonCode = addressee.ReasonCode;
                        confidence = addressee.Confidence;
                        gatePassed = addressee.Confidence >= MinimumAddresseeConfidence;
                    }

                    if (addressee.IsResolved &&
                        addressee.Confidence >= MinimumAddresseeConfidence &&
                        !string.Equals(addressee.SpeakerId, currentSpeaker, StringComparison.Ordinal) &&
                        TryEligibleGender(addressee.SpeakerId!, speakerGenderEvidence, out var addresseeGender))
                    {
                        if (TryGetImmediateAddresseeCueGender(
                                source,
                                cueSpeakers,
                                localCueGender,
                                cue.Index,
                                addressee.SpeakerId!,
                                out var localAddresseeGender) &&
                            localAddresseeGender != addresseeGender)
                        {
                            reasonCode = "addressee_gender_conflict";
                            targetGender = SpeakerVoiceGender.Unknown;
                            gatePassed = false;
                        }
                        else
                        {
                            targetGender = addresseeGender;
                            text = FixAddresseeAgreement(sourceCue.Text, text, addresseeGender);
                        }
                    }
                }
            }

            var changed = !string.Equals(text, originalText, StringComparison.Ordinal);
            var (matchedWord, replacement) = changed
                ? FindFirstChangedWordPair(originalText, text)
                : (null, null);

            if (diagnostics is not null && (candidateWord is not null || changed))
            {
                diagnostics.Add(new DeterministicGenderCueDiagnostic(
                    cue.Index,
                    currentSpeaker,
                    candidateWord is not null,
                    candidateWord,
                    resolver,
                    reasonCode,
                    targetGender,
                    confidence,
                    gatePassed,
                    matchedWord,
                    replacement,
                    changed));
            }

            result.Add(changed ? cue with { Text = text } : cue);
        }

        return result;
    }

    internal static string FixSpeakerAgreement(string text, SpeakerVoiceGender gender) =>
        FixSpeakerAgreement(sourceText: null, text, gender);

    private static string FixSpeakerAgreement(
        string? sourceText,
        string text,
        SpeakerVoiceGender gender)
    {
        if (sourceText is not null && EnglishQuotedFirstPersonRegex().IsMatch(sourceText))
            return text;

        // Polish present-tense forms such as "wysyłam" can look exactly like a
        // feminine past-tense suffix to a purely string-based rewriter. When the
        // English source is explicitly present progressive, do not apply the
        // generic -łam/-łem past-gender suffix rewrite. Irregular/conditional
        // mappings remain available.
        var protectGenericPastSuffix = sourceText is not null &&
            EnglishFirstPersonPresentProgressiveRegex().IsMatch(sourceText);
        var maximumReplacements = sourceText is null
            ? int.MaxValue
            : EnglishFirstPersonPronounRegex().Matches(sourceText).Count;

        return gender switch
        {
            SpeakerVoiceGender.Female => FixWords(
                text,
                SpeakerMaleToFemale,
                [("łbym", "łabym"), ("łem", "łam")],
                protectGenericPastSuffix,
                maximumReplacements),
            SpeakerVoiceGender.Male => FixWords(
                text,
                SpeakerFemaleToMale,
                [("łabym", "łbym"), ("łam", "łem")],
                protectGenericPastSuffix,
                maximumReplacements),
            _ => text
        };
    }

    internal static string FixAddresseeAgreement(string text, SpeakerVoiceGender gender) =>
        FixAddresseeAgreementCore(PhraseLexicon.LoadDefault(), sourceText: null, text, gender);

    private string FixAddresseeAgreement(string sourceText, string text, SpeakerVoiceGender gender) =>
        FixAddresseeAgreementCore(_phraseLexicon, sourceText, text, gender);

    private static string FixAddresseeAgreementCore(
        PhraseLexicon phraseLexicon,
        string? sourceText,
        string text,
        SpeakerVoiceGender gender)
    {
        if (gender == SpeakerVoiceGender.Unknown)
            return text;

        // Mirror of the speaker-side guard: a second-person Polish form has to come
        // from the English source actually addressing someone. Without a "you" there
        // is nothing to agree with, so no rewrite is authorised.
        var maximumReplacements = sourceText is null
            ? int.MaxValue
            : EnglishSecondPersonPronounRegex().Matches(sourceText).Count;
        if (maximumReplacements == 0)
            return text;

        var phraseCorrected = phraseLexicon.ApplyGenderedAddressee(sourceText, text, gender);
        var suffixCorrected = gender switch
        {
            SpeakerVoiceGender.Female => FixWords(
                phraseCorrected,
                AddresseeMaleToFemale,
                [("łbyś", "łabyś"), ("łeś", "łaś")],
                maximumReplacements: maximumReplacements),
            SpeakerVoiceGender.Male => FixWords(
                phraseCorrected,
                AddresseeFemaleToMale,
                [("łabyś", "łbyś"), ("łaś", "łeś")],
                maximumReplacements: maximumReplacements),
            _ => phraseCorrected
        };

        return FixSecondPersonPredicateAgreement(suffixCorrected, gender);
    }

    private static bool TryEligibleGender(
        string speakerId,
        IReadOnlyDictionary<string, SpeakerGenderEvidence> evidence,
        out SpeakerVoiceGender gender)
    {
        gender = SpeakerVoiceGender.Unknown;
        if (!evidence.TryGetValue(speakerId, out var value) || !SpeakerGenderReviewEligibility.IsEligible(value))
            return false;
        gender = value.Gender;
        return true;
    }

    private static bool TryGetImmediateAddresseeCueGender(
        IReadOnlyList<SubtitleCue> source,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        IReadOnlyDictionary<int, CueVoiceGenderEvidence> cueGenderEvidence,
        int currentCueId,
        string addresseeSpeaker,
        out SpeakerVoiceGender gender)
    {
        gender = SpeakerVoiceGender.Unknown;
        var position = CuePositionIndex.Find(source, currentCueId);
        if (position < 0 || position + 1 >= source.Count)
            return false;

        var next = source[position + 1];
        if (!cueSpeakers.TryGetValue(next.Index, out var nextSpeaker) ||
            !string.Equals(nextSpeaker, addresseeSpeaker, StringComparison.Ordinal) ||
            !cueGenderEvidence.TryGetValue(next.Index, out var evidence) ||
            evidence.Gender == SpeakerVoiceGender.Unknown ||
            evidence.Confidence < MinimumCueGenderConfidence ||
            evidence.CombinedEvidence < MinimumCueCombinedEvidence ||
            evidence.DurationSeconds < MinimumCueDurationSeconds)
        {
            return false;
        }

        gender = evidence.Gender;
        return true;
    }

    private static string FixWords(
        string text,
        IReadOnlyDictionary<string, string> irregular,
        IReadOnlyList<(string From, string To)> suffixes,
        bool protectGenericPastSuffix = false,
        int maximumReplacements = int.MaxValue)
    {
        var replacements = 0;
        return
        WordRegex().Replace(text, match =>
        {
            var original = match.Value;
            var lower = original.ToLowerInvariant();

            if (replacements >= maximumReplacements)
                return original;

            if (irregular.TryGetValue(lower, out var irregularReplacement))
            {
                replacements++;
                return MatchCasing(original, irregularReplacement);
            }

            foreach (var (from, to) in suffixes)
            {
                if (protectGenericPastSuffix && IsGenericPastSuffix(from))
                    continue;

                if (!lower.EndsWith(from, StringComparison.Ordinal) || lower.Length <= from.Length)
                    continue;
                if (IsGenericPastSuffix(from) && !IsKnownPastVerb(lower, from))
                    continue;
                var replacement = lower[..^from.Length] + to;
                replacements++;
                return MatchCasing(original, replacement);
            }

            return original;
        });
    }

    private static bool IsGenericPastSuffix(string suffix) =>
        suffix is "łam" or "łem" or "łaś" or "łeś";

    private static bool IsKnownPastVerb(string word, string suffix) =>
        PastVerbStems.Contains(word[..^suffix.Length]);

    /// <summary>
    /// Cue ids whose pitch actually has to be measured: every cue whose translation
    /// carries a gender-dependent form, plus the cue that follows it, because turn
    /// resolution reads the next cue's gender to decide the addressee.
    /// Cues outside this set can never change, so measuring them is wasted work.
    /// </summary>
    public static IReadOnlySet<int> CollectRelevantCueIds(
        IReadOnlyList<SubtitleCue> source,
        IReadOnlyList<SubtitleCue> translated)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(translated);

        var relevant = new HashSet<int>();
        var positions = new Dictionary<int, int>(source.Count);
        for (var index = 0; index < source.Count; index++)
            positions[source[index].Index] = index;

        foreach (var cue in translated)
        {
            if (FindGenderedCandidate(cue.Text) is null)
                continue;

            relevant.Add(cue.Index);
            if (positions.TryGetValue(cue.Index, out var position) && position + 1 < source.Count)
                relevant.Add(source[position + 1].Index);
        }

        return relevant;
    }

    private static string? FindGenderedCandidate(string text)
    {
        foreach (Match match in WordRegex().Matches(text))
        {
            var lower = match.Value.ToLowerInvariant();
            if (SpeakerMaleToFemale.ContainsKey(lower) ||
                SpeakerFemaleToMale.ContainsKey(lower) ||
                AddresseeMaleToFemale.ContainsKey(lower) ||
                AddresseeFemaleToMale.ContainsKey(lower) ||
                GenderedSuffixes.Any(suffix =>
                    lower.EndsWith(suffix, StringComparison.Ordinal) && lower.Length > suffix.Length))
            {
                return match.Value;
            }
        }

        foreach (Match match in PredicateCandidateRegex().Matches(text))
        {
            var predicate = match.Groups["predicate"].Value.ToLowerInvariant();
            if (FirstPersonPredicateMaleToFemale.ContainsKey(predicate) ||
                FirstPersonPredicateFemaleToMale.ContainsKey(predicate) ||
                SecondPersonPredicateMaleToFemale.ContainsKey(predicate) ||
                SecondPersonPredicateFemaleToMale.ContainsKey(predicate))
            {
                return match.Groups["predicate"].Value;
            }
        }

        return null;
    }

    // "Line one / - Line two" is two speakers as well (Doc S02E19), not one wrapped line.
    private static bool HasMultipleDialogueLines(string text) =>
        NapisyPL.Core.Translation.MachineTranslationTextPreprocessor.IsDialogueExchange(text);

    private static (string? From, string? To) FindFirstChangedWordPair(string before, string after)
    {
        var beforeWords = WordRegex().Matches(before).Select(match => match.Value).ToArray();
        var afterWords = WordRegex().Matches(after).Select(match => match.Value).ToArray();
        var count = Math.Min(beforeWords.Length, afterWords.Length);
        for (var index = 0; index < count; index++)
        {
            if (!string.Equals(beforeWords[index], afterWords[index], StringComparison.Ordinal))
                return (beforeWords[index], afterWords[index]);
        }

        return (null, null);
    }

    private static string MatchCasing(string source, string replacement)
    {
        if (source.All(character => !char.IsLetter(character) || char.IsUpper(character)))
            return replacement.ToUpperInvariant();

        if (source.Length > 0 && char.IsUpper(source[0]))
            return char.ToUpperInvariant(replacement[0]) + replacement[1..];

        return replacement;
    }

    private static IReadOnlyDictionary<string, string> Reverse(IReadOnlyDictionary<string, string> source) =>
        source.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"\bI\s*(?:['’]m|am)\b[^.!?]{0,100}\b\p{L}+ing\b", RegexOptions.IgnoreCase)]
    private static partial Regex EnglishFirstPersonPresentProgressiveRegex();

    [GeneratedRegex(@"\bI\b", RegexOptions.IgnoreCase)]
    private static partial Regex EnglishFirstPersonPronounRegex();

    [GeneratedRegex(@"\b(?:you|your|yours|yourself)\b", RegexOptions.IgnoreCase)]
    private static partial Regex EnglishSecondPersonPronounRegex();

    [GeneratedRegex("(?:[\"“][^\"”]{0,120}\\bI\\b[^\"”]{0,120}[\"”]|(?:^|[\\s,:;])'[^']{0,120}\\bI\\b[^']{0,120}')", RegexOptions.IgnoreCase)]
    private static partial Regex EnglishQuotedFirstPersonRegex();

}
