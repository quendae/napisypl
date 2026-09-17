using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

public sealed partial class DeterministicGenderReviewService
{
    private static readonly IReadOnlyDictionary<string, string> HandFirstPersonPredicateMaleToFemale =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["nieśmiały"] = "nieśmiała",
            ["gotowy"] = "gotowa",
            ["pewny"] = "pewna",
            ["szczęśliwy"] = "szczęśliwa",
            ["zadowolony"] = "zadowolona",
            ["zmęczony"] = "zmęczona",
            ["spóźniony"] = "spóźniona",
            ["sam"] = "sama"
        };

    private static readonly IReadOnlyDictionary<string, string> FirstPersonPredicateMaleToFemale =
        GenderFormLexicon.Merge(HandFirstPersonPredicateMaleToFemale, GenderFormLexicon.Default.PredicateMaleToFemale);

    private static readonly IReadOnlyDictionary<string, string> FirstPersonPredicateFemaleToMale =
        GenderFormLexicon.Merge(
            HandFirstPersonPredicateMaleToFemale.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal),
            GenderFormLexicon.Default.PredicateFemaleToMale);

    private static bool TryGetHardVoiceSpeakerSelfGenderSafely(
        string? currentSpeaker,
        IReadOnlyDictionary<string, SpeakerGenderEvidence> speakerGenderEvidence,
        out SpeakerVoiceGender gender,
        out double confidence)
    {
        gender = SpeakerVoiceGender.Unknown;
        confidence = 0;

        if (string.IsNullOrWhiteSpace(currentSpeaker) ||
            !speakerGenderEvidence.TryGetValue(currentSpeaker!, out var speakerEvidence) ||
            !SpeakerGenderReviewEligibility.IsEligible(speakerEvidence))
            return false;

        gender = speakerEvidence.Gender;
        confidence = speakerEvidence.Confidence;
        return true;
    }

    /// <summary>Profile confidence that suffices when the cue's own pitch confirms it.</summary>
    private const double CueConfirmedProfileMinimumConfidence = 0.75;

    /// <summary>Directional pitch needed to call a cue's voice the opposite gender.</summary>
    private const double ContradictingDirectionalConfidence = 0.75;

    private static readonly TimeSpan MaximumContinuationGap = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Leftovers S01E01 #284 and #666: the cue is clearly female (0.92) and so is the
    /// speaker profile, but the profile is 0.825, below the review bar.
    /// </summary>
    private static bool TryGetCueConfirmedSelfGender(
        string? currentSpeaker,
        IReadOnlyDictionary<string, SpeakerGenderEvidence> speakerGenderEvidence,
        IReadOnlyDictionary<int, CueVoiceGenderEvidence> cueGenderEvidence,
        int cueId,
        out SpeakerVoiceGender gender,
        out double confidence)
    {
        gender = SpeakerVoiceGender.Unknown;
        confidence = 0;

        if (string.IsNullOrWhiteSpace(currentSpeaker) ||
            !speakerGenderEvidence.TryGetValue(currentSpeaker!, out var profile) ||
            profile.Gender == SpeakerVoiceGender.Unknown ||
            profile.Confidence < CueConfirmedProfileMinimumConfidence ||
            profile.SampleCount < SpeakerGenderReviewEligibility.MinimumSampleCount ||
            !HardVoiceTurnResolver.TryGetForcedGender(cueGenderEvidence, cueId, out var cueGender, out var cueConfidence) ||
            cueGender != profile.Gender ||
            cueConfidence < HardVoiceTurnResolver.PitchOverridesDiarizationConfidence)
        {
            return false;
        }

        gender = profile.Gender;
        confidence = Math.Min(profile.Confidence, cueConfidence);
        return true;
    }

    /// <summary>
    /// Leftovers S01E01 #635/#636: a female profile, but the cue leans male and its
    /// same-label continuation is confidently male.
    /// </summary>
    private static bool SelfGenderContradictedByCuePitch(
        IReadOnlyList<SubtitleCue> source,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        IReadOnlyDictionary<int, CueVoiceGenderEvidence> cueGenderEvidence,
        int cueId,
        SpeakerVoiceGender profileGender)
    {
        if (HardVoiceTurnResolver.TryGetForcedGender(cueGenderEvidence, cueId, out var forced, out _))
            return forced != profileGender;

        var currentDirectional = SpeakerVoiceGender.Unknown;
        if (cueGenderEvidence.TryGetValue(cueId, out var evidence) &&
            evidence.DirectionalConfidence >= ContradictingDirectionalConfidence)
        {
            currentDirectional = evidence.DirectionalGender;
        }

        // A directional lean with real voiced evidence contradicts on its own.
        if (currentDirectional != SpeakerVoiceGender.Unknown &&
            currentDirectional != profileGender &&
            evidence!.CombinedEvidence >= MinimumCueCombinedEvidence &&
            evidence.DurationSeconds >= MinimumCueDurationSeconds)
        {
            return true;
        }

        if (currentDirectional == profileGender)
            return false;

        var position = CuePositionIndex.Find(source, cueId);
        if (position < 0 || position + 1 >= source.Count)
            return false;

        var current = source[position];
        var next = source[position + 1];
        if (next.Start - current.End > MaximumContinuationGap ||
            !cueSpeakers.TryGetValue(cueId, out var speaker) ||
            !cueSpeakers.TryGetValue(next.Index, out var nextSpeaker) ||
            string.IsNullOrWhiteSpace(speaker) ||
            !string.Equals(speaker, nextSpeaker, StringComparison.Ordinal))
        {
            return false;
        }

        return HardVoiceTurnResolver.TryGetForcedGender(cueGenderEvidence, next.Index, out var nextGender, out _) &&
               nextGender != profileGender;
    }

    /// <summary>How far a same-speaker run is followed in each direction.</summary>
    private const int MaximumRunHops = 2;

    /// <summary>
    /// MPG S01E01 #612/#613: "Why would you tell him about Portland, Paula?" came out
    /// feminine, the next line of the same speaker ("Why would you tell anyone?")
    /// masculine. Within one uninterrupted run a speaker keeps addressing the same
    /// person, so an addressee gender established nearby carries over.
    /// Anchors are cues this review already resolved, or feminine forms the MT chose
    /// next to an English vocative name. Masculine MT forms are not anchors: they are
    /// the model's default, not a decision.
    /// </summary>
    private static bool TryGetSameSpeakerRunAddresseeGender(
        IReadOnlyList<SubtitleCue> source,
        IReadOnlyList<SubtitleCue> translated,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        IReadOnlyDictionary<int, CueVoiceGenderEvidence> cueGenderEvidence,
        IReadOnlyDictionary<int, SpeakerVoiceGender> resolvedAddresseeGender,
        int cueId,
        out SpeakerVoiceGender gender)
    {
        gender = SpeakerVoiceGender.Unknown;
        var found = SpeakerVoiceGender.Unknown;

        foreach (var candidate in SameSpeakerRun(source, cueSpeakers, cueGenderEvidence, cueId))
        {
            var anchor = SpeakerVoiceGender.Unknown;
            if (resolvedAddresseeGender.TryGetValue(candidate.Index, out var resolved))
            {
                anchor = resolved;
            }
            else if (TryGetTranslatedText(translated, candidate.Index, out var namedText) &&
                     TryGetVocativeAddresseeGender(candidate.Text, namedText, out var named))
            {
                anchor = named;
            }
            else if (EnglishVocativeNameRegex().IsMatch(candidate.Text) &&
                     TryGetTranslatedText(translated, candidate.Index, out var translatedText))
            {
                var (male, female) = CountAddresseeMarkers(translatedText);
                if (female > 0 && male == 0)
                    anchor = SpeakerVoiceGender.Female;
            }

            if (anchor != SpeakerVoiceGender.Unknown)
            {
                if (found != SpeakerVoiceGender.Unknown && found != anchor)
                    return false;
                found = anchor;
            }
        }

        gender = found;
        return found != SpeakerVoiceGender.Unknown;
    }

    /// <summary>MPG S01E10 #306/#309 need at least this many feminine neighbours.</summary>
    private const int MinimumFeminineRunCues = 2;

    /// <summary>
    /// MPG S01E10, Paula's monologue #305-#310: the MT wrote "miałam", "nie mogłam",
    /// "Nie miałam", "Mogłabym" in four lines and "Byłem", "poszedłem" in the other
    /// two. Feminine first-person forms in at least two neighbouring lines of the same
    /// uninterrupted run are a decision the MT made from context; a lone feminine form
    /// is not (Leftovers S01E01 gave men "znalazłam" and "wiedziałam" in single lines),
    /// and masculine forms are only the model's default, so they never count as evidence.
    /// </summary>
    private static bool TryGetSameSpeakerRunFeminineSelf(
        IReadOnlyList<SubtitleCue> source,
        IReadOnlyList<SubtitleCue> translated,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        IReadOnlyDictionary<int, CueVoiceGenderEvidence> cueGenderEvidence,
        int cueId)
    {
        if (cueGenderEvidence.TryGetValue(cueId, out var evidence) &&
            (evidence.Gender == SpeakerVoiceGender.Male ||
             (evidence.DirectionalGender == SpeakerVoiceGender.Male &&
              evidence.DirectionalConfidence >= ContradictingDirectionalConfidence)))
        {
            return false;
        }

        var feminine = 0;
        foreach (var candidate in SameSpeakerRun(source, cueSpeakers, cueGenderEvidence, cueId))
        {
            if (HardVoiceTurnResolver.TryGetForcedGender(cueGenderEvidence, candidate.Index, out var pitch, out _) &&
                pitch == SpeakerVoiceGender.Male)
            {
                return false;
            }

            if (!EnglishFirstPersonPronounRegex().IsMatch(candidate.Text) ||
                EnglishQuotedFirstPersonRegex().IsMatch(candidate.Text) ||
                !TryGetTranslatedText(translated, candidate.Index, out var translatedText))
            {
                continue;
            }

            var (male, female) = CountSelfMarkers(translatedText);
            if (male > 0)
                return false;
            if (female > 0)
                feminine++;
        }

        return feminine >= MinimumFeminineRunCues;
    }

    /// <summary>
    /// Up to <see cref="MaximumRunHops"/> cues on each side that share this cue's
    /// speaker label, with no gap over three seconds and no confident pitch that
    /// disagrees with this cue's own.
    /// </summary>
    private static List<SubtitleCue> SameSpeakerRun(
        IReadOnlyList<SubtitleCue> source,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        IReadOnlyDictionary<int, CueVoiceGenderEvidence> cueGenderEvidence,
        int cueId)
    {
        var run = new List<SubtitleCue>();
        var position = CuePositionIndex.Find(source, cueId);
        if (position < 0 ||
            !cueSpeakers.TryGetValue(cueId, out var speaker) ||
            string.IsNullOrWhiteSpace(speaker))
        {
            return run;
        }

        HardVoiceTurnResolver.TryGetForcedGender(cueGenderEvidence, cueId, out var currentPitch, out _);
        foreach (var step in new[] { -1, 1 })
        {
            var previous = source[position];
            for (var hop = 1; hop <= MaximumRunHops; hop++)
            {
                var index = position + step * hop;
                if (index < 0 || index >= source.Count)
                    break;

                var candidate = source[index];
                var (first, second) = step < 0 ? (candidate, previous) : (previous, candidate);
                if (second.Start - first.End > MaximumContinuationGap ||
                    !cueSpeakers.TryGetValue(candidate.Index, out var candidateSpeaker) ||
                    !string.Equals(candidateSpeaker, speaker, StringComparison.Ordinal) ||
                    (currentPitch != SpeakerVoiceGender.Unknown &&
                     HardVoiceTurnResolver.TryGetForcedGender(cueGenderEvidence, candidate.Index, out var candidatePitch, out _) &&
                     candidatePitch != currentPitch))
                {
                    break;
                }

                run.Add(candidate);
                previous = candidate;
            }
        }

        return run;
    }

    private static bool TryGetTranslatedText(IReadOnlyList<SubtitleCue> translated, int cueId, out string text)
    {
        var position = CuePositionIndex.Find(translated, cueId);
        text = position >= 0 ? translated[position].Text : string.Empty;
        return position >= 0;
    }

    private static (int Male, int Female) CountSelfMarkers(string text)
    {
        var male = 0;
        var female = 0;
        foreach (System.Text.RegularExpressions.Match match in WordRegex().Matches(text))
        {
            var word = match.Value.ToLowerInvariant();
            if (SpeakerFemaleToMale.ContainsKey(word) ||
                (word.Length > 5 && word.EndsWith("łabym", StringComparison.Ordinal)) ||
                (word.Length > 3 && word.EndsWith("łam", StringComparison.Ordinal) && IsKnownPastVerb(word, "łam")))
            {
                female++;
            }
            else if (SpeakerMaleToFemale.ContainsKey(word) ||
                     (word.Length > 4 && word.EndsWith("łbym", StringComparison.Ordinal)) ||
                     (word.Length > 3 && word.EndsWith("łem", StringComparison.Ordinal) && IsKnownPastVerb(word, "łem")))
            {
                male++;
            }
        }

        return (male, female);
    }

    private static (int Male, int Female) CountAddresseeMarkers(string text)
    {
        var male = 0;
        var female = 0;
        foreach (System.Text.RegularExpressions.Match match in WordRegex().Matches(text))
        {
            var word = match.Value.ToLowerInvariant();
            if (AddresseeFemaleToMale.ContainsKey(word) ||
                (word.Length > 5 && word.EndsWith("łabyś", StringComparison.Ordinal)) ||
                (word.Length > 3 && word.EndsWith("łaś", StringComparison.Ordinal)))
            {
                female++;
            }
            else if (AddresseeMaleToFemale.ContainsKey(word) ||
                     (word.Length > 4 && word.EndsWith("łbyś", StringComparison.Ordinal)) ||
                     (word.Length > 3 && word.EndsWith("łeś", StringComparison.Ordinal)))
            {
                male++;
            }
        }

        return (male, female);
    }

    /// <summary>A name in the cue outranks every other addressee signal we have.</summary>
    private const double VocativeAddresseeConfidence = 0.99;

    /// <summary>
    /// The addressee's gender when the English line names them ("But you have, Jaclyn."), which
    /// is where Chance S01E10 #580 wrote "Przeżyłeś" to a woman. The line has to address
    /// somebody at all, so a name without a second-person pronoun proves nothing: "Mr. Schorr."
    /// as the tail of a narrated sentence names a third person.
    /// The MT writes masculine by default, so masculine forms never outrank the name; two or
    /// more feminine forms are a decision it made from context, and a name list that disagrees
    /// with them (Andrea, Nikita, Ashley are read differently in different languages) yields.
    /// </summary>
    private static bool TryGetVocativeAddresseeGender(
        string sourceText,
        string translatedText,
        out SpeakerVoiceGender gender)
    {
        gender = SpeakerVoiceGender.Unknown;
        if (!EnglishSecondPersonPronounRegex().IsMatch(sourceText))
            return false;

        var named = VocativeAddresseeEvidence.Resolve(sourceText);
        if (named == SpeakerVoiceGender.Unknown)
            return false;
        if (named == SpeakerVoiceGender.Male && InferStrongAddresseeGender(translatedText) == SpeakerVoiceGender.Female)
            return false;

        gender = named;
        return true;
    }

    /// <summary>A capitalised name addressed at the start or end of a line: "Paula, …" / "…, Paula?"</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"(?:^|\n)\p{Lu}\p{Ll}+,|,\s*\p{Lu}\p{Ll}+\s*[?!.…]*\s*$")]
    private static partial System.Text.RegularExpressions.Regex EnglishVocativeNameRegex();

    private static bool ShouldPreserveStrongAddresseeGender(
        string translatedText,
        SpeakerVoiceGender proposedGender)
    {
        var strongTextGender = InferStrongAddresseeGender(translatedText);
        return strongTextGender != SpeakerVoiceGender.Unknown && strongTextGender != proposedGender;
    }

    private static SpeakerVoiceGender InferStrongAddresseeGender(string text)
    {
        var words = WordRegex().Matches(text).Select(match => match.Value.ToLowerInvariant()).ToArray();
        var male = 0;
        var female = 0;

        for (var index = 0; index < words.Length; index++)
        {
            var word = words[index];
            if (word.EndsWith("łabyś", StringComparison.Ordinal) && word.Length > 5)
                female++;
            else if (word.EndsWith("łbyś", StringComparison.Ordinal) && word.Length > 4)
                male++;
            else if (word.EndsWith("łaś", StringComparison.Ordinal) && word.Length > 3)
                female++;
            else if (word.EndsWith("łeś", StringComparison.Ordinal) && word.Length > 3)
                male++;

            if (word == "gdybyś" && TryFindConditionalParticiple(words, index + 1, out var contextualGender))
            {
                if (contextualGender == SpeakerVoiceGender.Male) male++;
                if (contextualGender == SpeakerVoiceGender.Female) female++;
            }
        }

        return StrongGender(male, female);
    }

    private static bool TryFindConditionalParticiple(
        IReadOnlyList<string> words,
        int startIndex,
        out SpeakerVoiceGender gender)
    {
        gender = SpeakerVoiceGender.Unknown;
        var end = Math.Min(words.Count, startIndex + 4);
        for (var index = startIndex; index < end; index++)
        {
            var word = words[index];
            if (word.Length > 2 && word.EndsWith("ła", StringComparison.Ordinal))
            {
                gender = SpeakerVoiceGender.Female;
                return true;
            }

            if (word.Length > 1 && word.EndsWith("ł", StringComparison.Ordinal))
            {
                gender = SpeakerVoiceGender.Male;
                return true;
            }
        }

        return false;
    }

    private static SpeakerVoiceGender StrongGender(int maleMarkers, int femaleMarkers)
    {
        if (maleMarkers >= 2 && femaleMarkers == 0)
            return SpeakerVoiceGender.Male;
        if (femaleMarkers >= 2 && maleMarkers == 0)
            return SpeakerVoiceGender.Female;
        return SpeakerVoiceGender.Unknown;
    }

    private static readonly IReadOnlyDictionary<string, string> HandSecondPersonPredicateMaleToFemale =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["nieśmiały"] = "nieśmiała",
            ["gotowy"] = "gotowa",
            ["pewien"] = "pewna",
            ["pewny"] = "pewna",
            ["szczęśliwy"] = "szczęśliwa",
            ["zadowolony"] = "zadowolona",
            ["zmęczony"] = "zmęczona",
            ["spóźniony"] = "spóźniona",
            ["głodny"] = "głodna",
            ["zły"] = "zła",
            ["sam"] = "sama"
        };

    private static readonly IReadOnlyDictionary<string, string> HandSecondPersonPredicateFemaleToMale =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["nieśmiała"] = "nieśmiały",
            ["gotowa"] = "gotowy",
            // "pewien" is the idiomatic masculine predicate form after "jesteś".
            ["pewna"] = "pewien",
            ["szczęśliwa"] = "szczęśliwy",
            ["zadowolona"] = "zadowolony",
            ["zmęczona"] = "zmęczony",
            ["spóźniona"] = "spóźniony",
            ["głodna"] = "głodny",
            ["zła"] = "zły",
            ["sama"] = "sam"
        };

    private static readonly IReadOnlyDictionary<string, string> SecondPersonPredicateMaleToFemale =
        GenderFormLexicon.Merge(HandSecondPersonPredicateMaleToFemale, GenderFormLexicon.Default.PredicateMaleToFemale);

    private static readonly IReadOnlyDictionary<string, string> SecondPersonPredicateFemaleToMale =
        GenderFormLexicon.Merge(HandSecondPersonPredicateFemaleToMale, GenderFormLexicon.Default.PredicateFemaleToMale);

    /// <summary>
    /// Second-person predicate adjectives after "jesteś" ("jesteś gotowy" ->
    /// "jesteś gotowa"). DESIGN.md lists this as a primary target, but only the
    /// first-person variant had been implemented.
    /// </summary>
    private static string FixSecondPersonPredicateAgreement(
        string text,
        SpeakerVoiceGender gender)
    {
        if (gender == SpeakerVoiceGender.Unknown)
            return text;

        var map = gender == SpeakerVoiceGender.Female
            ? SecondPersonPredicateMaleToFemale
            : SecondPersonPredicateFemaleToMale;

        return SecondPersonPredicateRegex().Replace(
            text,
            match => ReplacePredicateChain(match, map, SecondPersonPredicateMaleToFemale, SecondPersonPredicateFemaleToMale));
    }

    private static string FixFirstPersonPredicateAgreement(
        string sourceText,
        string text,
        SpeakerVoiceGender gender)
    {
        if (gender == SpeakerVoiceGender.Unknown || EnglishQuotedFirstPersonRegex().IsMatch(sourceText))
            return text;

        var map = gender == SpeakerVoiceGender.Female
            ? FirstPersonPredicateMaleToFemale
            : FirstPersonPredicateFemaleToMale;

        text = FirstPersonPredicateRegex().Replace(
            text,
            match => ReplacePredicateChain(match, map, FirstPersonPredicateMaleToFemale, FirstPersonPredicateFemaleToMale));
        return FixAloneAgreement(text, gender);
    }

    /// <summary>
    /// "Zrobiłbym to wszystko sama" (Chance S01E06 #618): "sam/sama" meaning "alone" follows
    /// the speaker, but only next to a first-person verb of that gender and never in
    /// "ten sam", "ta sama", "sam na sam".
    /// </summary>
    private static string FixAloneAgreement(string text, SpeakerVoiceGender gender)
    {
        // The speaker's own past forms decide, counted with the same lexicon the rest of the
        // review uses, so a present tense that merely looks past ("działam") does not count.
        var (male, female) = CountSelfMarkers(text);
        if (gender == SpeakerVoiceGender.Female && female > 0 && male == 0)
            return AloneRegex().Replace(text, match => match.Value == "Sam" ? "Sama" : match.Value == "sam" ? "sama" : match.Value);
        if (gender == SpeakerVoiceGender.Male && male > 0 && female == 0)
            return AloneRegex().Replace(text, match => match.Value == "Sama" ? "Sam" : match.Value == "sama" ? "sam" : match.Value);
        return text;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"(?<!\b(?:[Tt]en|[Tt]ym|[Tt]ego|[Tt]emu|[Tt]a|[Tt]ą|[Tt]ej|[Tt]ę|[Tt]ych|[Tt]ymi|[Tt]ak|[Tt]aki|[Tt]aka|[Tt]akiej|[Nn]a)\s+)\b(?:[Ss]am|[Ss]ama)\b(?!\s+na\s+sam)")]
    private static partial System.Text.RegularExpressions.Regex AloneRegex();

    /// <summary>
    /// Rewrites the predicate and any adjectives coordinated with it ("jestem stara
    /// i zepsuty", MPG S01E01 #14). The chain stops at the first word that is not a
    /// known predicate of either gender, so "jestem gotowa, a on gotowy" keeps "gotowy".
    /// </summary>
    private static string ReplacePredicateChain(
        System.Text.RegularExpressions.Match match,
        IReadOnlyDictionary<string, string> map,
        IReadOnlyDictionary<string, string> maleToFemale,
        IReadOnlyDictionary<string, string> femaleToMale)
    {
        var value = match.Value;
        var edits = new List<(int Index, int Length, string Text)>();
        foreach (System.Text.RegularExpressions.Capture capture in match.Groups["predicate"].Captures)
        {
            var lower = capture.Value.ToLowerInvariant();
            if (!maleToFemale.ContainsKey(lower) && !femaleToMale.ContainsKey(lower))
                break;
            if (map.TryGetValue(lower, out var replacement))
                edits.Add((capture.Index - match.Index, capture.Length, MatchCasing(capture.Value, replacement)));
        }

        for (var index = edits.Count - 1; index >= 0; index--)
        {
            var edit = edits[index];
            value = value[..edit.Index] + edit.Text + value[(edit.Index + edit.Length)..];
        }

        return value;
    }

    /// <summary>
    /// Up to two degree or time adverbs between the copula and the predicate:
    /// "byłem tak zaniepokojona" (Leftovers S01E01 #217), "jestem już gotowy".
    /// </summary>
    private const string PredicateModifiers =
        @"(?:(?:tak|bardzo|naprawdę|zbyt|za|całkiem|strasznie|trochę|dość|dosyć|okropnie|niesamowicie|szalenie|już|jeszcze|zawsze|nigdy|wciąż|ciągle|chyba|raczej|też|również|tylko)\s+){0,2}";

    /// <summary>Up to three further predicates joined by a comma, "i", "oraz", "a" or "ale".</summary>
    private const string PredicateChain =
        @"(?:(?:\s*,\s*|\s+(?:i|oraz|a|ale)\s+)" + PredicateModifiers + @"(?<predicate>\p{L}+)){0,3}";

    [System.Text.RegularExpressions.GeneratedRegex(
        @"\b(?:jestem|byłem|byłam|będę)\s+" + PredicateModifiers + @"(?<predicate>\p{L}+)" + PredicateChain,
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex FirstPersonPredicateRegex();

    [System.Text.RegularExpressions.GeneratedRegex(
        @"\b(?:jesteś|byłeś|byłaś|będziesz)\s+" + PredicateModifiers + @"(?<predicate>\p{L}+)" + PredicateChain,
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex SecondPersonPredicateRegex();

    [System.Text.RegularExpressions.GeneratedRegex(
        @"\b(?:jestem|jesteś|byłem|byłam|byłeś|byłaś|będę|będziesz)\s+" + PredicateModifiers + @"(?<predicate>\p{L}+)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex PredicateCandidateRegex();
}
