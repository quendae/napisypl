using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

public sealed class HardVoiceTurnResolverTests
{
    [Fact]
    public void Resolve_MaleThenFemaleAcceptedPerCue_TargetsFemaleWithoutExact1000()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "Did you do it?"),
            Cue(2, 1, 2, "Yes.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_00",
            [2] = "SPEAKER_01"
        };
        var cueGender = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [1] = Accepted(SpeakerVoiceGender.Male, 0.91),
            [2] = Accepted(SpeakerVoiceGender.Female, 0.88)
        };

        var result = HardVoiceTurnResolver.Resolve(cues, speakers, cueGender, 1);

        Assert.True(result.IsResolved);
        Assert.Equal(SpeakerVoiceGender.Male, result.CurrentGender);
        Assert.Equal(SpeakerVoiceGender.Female, result.TargetGender);
        Assert.Equal("SPEAKER_01", result.TargetSpeakerId);
        Assert.Equal("next_cue_opposite_gender", result.ReasonCode);
        Assert.Equal(0.88, result.Confidence, precision: 3);
    }

    [Fact]
    public void Resolve_FemaleThenMaleAcceptedPerCue_TargetsMale()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "Did you do it?"),
            Cue(2, 1, 2, "Yes.")
        };
        var cueGender = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [1] = Accepted(SpeakerVoiceGender.Female, 0.93),
            [2] = Accepted(SpeakerVoiceGender.Male, 0.86)
        };

        var result = HardVoiceTurnResolver.Resolve(
            cues,
            new Dictionary<int, string?>(),
            cueGender,
            1);

        Assert.True(result.IsResolved);
        Assert.Equal(SpeakerVoiceGender.Male, result.TargetGender);
    }

    [Fact]
    public void Resolve_WhenNextCueGenderIsUnknown_DoesNotResolve()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "Did you do it?"),
            Cue(2, 1, 2, "Yes.")
        };
        var cueGender = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [1] = Accepted(SpeakerVoiceGender.Male, 0.95),
            [2] = new(SpeakerVoiceGender.Unknown, 0, 0.01, 1.0)
        };

        var result = HardVoiceTurnResolver.Resolve(
            cues,
            new Dictionary<int, string?>(),
            cueGender,
            1);

        Assert.False(result.IsResolved);
        Assert.Equal("next_gender_unknown", result.ReasonCode);
    }

    [Fact]
    public void Resolve_ExplicitGenderBelowNormalEvidenceGate_DoesNotResolve()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "Did you do it?"),
            Cue(2, 1, 2, "Yes.")
        };
        var cueGender = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [1] = new(
                SpeakerVoiceGender.Male,
                0.99,
                0.01,
                1.0,
                SpeakerVoiceGender.Male,
                0.99),
            [2] = Accepted(SpeakerVoiceGender.Female, 0.95)
        };

        var result = HardVoiceTurnResolver.Resolve(
            cues,
            new Dictionary<int, string?>(),
            cueGender,
            1);

        Assert.False(result.IsResolved);
        Assert.Equal("current_gender_unknown", result.ReasonCode);
    }

    [Fact]
    public void Resolve_WhenConsecutiveCuesHaveSameGender_DoesNotResolve()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "Did you do it?"),
            Cue(2, 1, 2, "Yes.")
        };
        var cueGender = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [1] = Accepted(SpeakerVoiceGender.Male, 0.97),
            [2] = Accepted(SpeakerVoiceGender.Male, 0.92)
        };

        var result = HardVoiceTurnResolver.Resolve(
            cues,
            new Dictionary<int, string?>(),
            cueGender,
            1);

        Assert.False(result.IsResolved);
        Assert.Equal("same_gender_turn", result.ReasonCode);
    }

    [Fact]
    public void Resolve_WhenNextCueFollowsLongGap_DoesNotResolveAcrossSceneBoundary()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "Did you do it?"),
            Cue(2, 31, 32, "Yes.")
        };
        var cueGender = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [1] = Accepted(SpeakerVoiceGender.Male, 0.97),
            [2] = Accepted(SpeakerVoiceGender.Female, 0.92)
        };

        var result = HardVoiceTurnResolver.Resolve(
            cues,
            new Dictionary<int, string?>(),
            cueGender,
            1);

        Assert.False(result.IsResolved);
    }

    [Fact]
    public void Resolve_WhenCuesBelongToSameSpeaker_DoesNotResolve()
    {
        var cues = new[] { Cue(1, 0, 1, "Did you do it?"), Cue(2, 1.1, 2, "Yes.") };
        var speakers = new Dictionary<int, string?> { [1] = "SPEAKER_00", [2] = "SPEAKER_00" };
        var genders = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [1] = Accepted(SpeakerVoiceGender.Male, 0.97),
            [2] = Accepted(SpeakerVoiceGender.Female, 0.92)
        };

        var result = HardVoiceTurnResolver.Resolve(cues, speakers, genders, 1);

        Assert.False(result.IsResolved);
    }

    [Fact]
    public void Resolve_WhenCuesOverlapTooMuch_DoesNotResolve()
    {
        var cues = new[] { Cue(1, 0, 2, "Did you do it?"), Cue(2, 1, 3, "Yes.") };
        var genders = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [1] = Accepted(SpeakerVoiceGender.Male, 0.97),
            [2] = Accepted(SpeakerVoiceGender.Female, 0.92)
        };

        var result = HardVoiceTurnResolver.Resolve(cues, new Dictionary<int, string?>(), genders, 1);

        Assert.False(result.IsResolved);
    }

    [Fact]
    public void Resolve_WhenImmediateThirdSpeakerAppears_DoesNotResolve()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "Did you do it?"),
            Cue(2, 1.1, 2, "Wait."),
            Cue(3, 2.1, 3, "Yes.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_00", [2] = "SPEAKER_01", [3] = "SPEAKER_02"
        };
        var genders = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [1] = Accepted(SpeakerVoiceGender.Male, 0.97),
            [2] = Accepted(SpeakerVoiceGender.Female, 0.92)
        };

        var result = HardVoiceTurnResolver.Resolve(cues, speakers, genders, 1);

        Assert.False(result.IsResolved);
    }

    [Fact]
    public void Review_MaleThenFemaleAcceptedPerCue_RewritesFemaleAddressee()
    {
        var source = new[]
        {
            Cue(1, 0, 1, "What did you do?"),
            Cue(2, 1, 2, "Nothing.")
        };
        var translated = new[]
        {
            Cue(1, 0, 1, "Co zrobiłeś?"),
            Cue(2, 1, 2, "Nic.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_M",
            [2] = "SPEAKER_F"
        };
        var speakerGender = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_F"] = new(SpeakerVoiceGender.Female, 0.98, 3)
        };
        var cueGender = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [1] = Accepted(SpeakerVoiceGender.Male, 0.91),
            [2] = Accepted(SpeakerVoiceGender.Female, 0.88)
        };
        var diagnostics = new List<DeterministicGenderCueDiagnostic>();

        var result = new DeterministicGenderReviewService().Review(
            source,
            translated,
            speakers,
            speakerGender,
            cueGender,
            diagnostics,
            hardVoiceTurnOnly: true);

        Assert.Equal("Co zrobiłaś?", result[0].Text);
        var diagnostic = Assert.Single(diagnostics, item => item.CueId == 1);
        Assert.Equal("hard_voice_sequence", diagnostic.Resolver);
        Assert.Equal("next_cue_opposite_gender", diagnostic.ReasonCode);
        Assert.Equal(SpeakerVoiceGender.Female, diagnostic.TargetGender);
        Assert.True(diagnostic.GatePassed);
    }

    [Fact]
    public void Review_FemaleThenMaleAcceptedPerCue_RewritesMaleAddressee()
    {
        var source = new[]
        {
            Cue(1, 0, 1, "What did you do?"),
            Cue(2, 1, 2, "Nothing.")
        };
        var translated = new[]
        {
            Cue(1, 0, 1, "Co zrobiłaś?"),
            Cue(2, 1, 2, "Nic.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_F",
            [2] = "SPEAKER_M"
        };
        var speakerGender = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_M"] = new(SpeakerVoiceGender.Male, 0.98, 3)
        };
        var cueGender = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [1] = Accepted(SpeakerVoiceGender.Female, 0.94),
            [2] = Accepted(SpeakerVoiceGender.Male, 0.87)
        };

        var result = new DeterministicGenderReviewService().Review(
            source,
            translated,
            speakers,
            speakerGender,
            cueGender,
            hardVoiceTurnOnly: true);

        Assert.Equal("Co zrobiłeś?", result[0].Text);
    }

    [Fact]
    public void Review_CurrentMaleCueWithoutStableSpeaker_DoesNotFixSpeakerSelfForm()
    {
        var source = new[]
        {
            Cue(451, 0, 2, "I didn't get sick because I read about you in the paper."),
            Cue(452, 2, 4, "I got sick because I went outside to get the paper.")
        };
        var translated = new[]
        {
            Cue(451, 0, 2, "Nie zachorowałam, bo czytałam o tobie w gazecie."),
            Cue(452, 2, 4, "Zachorowałam, bo wyszłam z domu po gazetę.")
        };
        var cueGender = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [451] = Accepted(SpeakerVoiceGender.Male, 0.985),
            [452] = Accepted(SpeakerVoiceGender.Male, 0.96)
        };
        var diagnostics = new List<DeterministicGenderCueDiagnostic>();

        var result = new DeterministicGenderReviewService().Review(
            source,
            translated,
            new Dictionary<int, string?>(),
            new Dictionary<string, SpeakerGenderEvidence>(),
            cueGender,
            diagnostics,
            hardVoiceTurnOnly: true);

        Assert.Equal("Nie zachorowałam, bo czytałam o tobie w gazecie.", result[0].Text);
        Assert.Equal("Zachorowałam, bo wyszłam z domu po gazetę.", result[1].Text);
        Assert.DoesNotContain(diagnostics, item => item.Changed);
    }

    [Fact]
    public void Review_TargetCueGenderConflictsWithStableTargetSpeaker_DoesNotRewriteAddressee()
    {
        var source = new[]
        {
            Cue(542, 0, 1, "What did you hear?"),
            Cue(543, 1.1, 2, "Nothing.")
        };
        var translated = new[]
        {
            Cue(542, 0, 1, "Co słyszałeś?"),
            Cue(543, 1.1, 2, "Nic.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [542] = "SPEAKER_02",
            [543] = "SPEAKER_17"
        };
        var speakerGender = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_17"] = new(SpeakerVoiceGender.Male, 0.98, 3)
        };
        var cueGender = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [542] = Accepted(SpeakerVoiceGender.Male, 0.98),
            [543] = Accepted(SpeakerVoiceGender.Female, 0.99)
        };

        var result = new DeterministicGenderReviewService().Review(
            source,
            translated,
            speakers,
            speakerGender,
            cueGender,
            hardVoiceTurnOnly: true);

        Assert.Equal("Co słyszałeś?", result[0].Text);
    }

    [Theory]
    [InlineData("- I was ready.\n- I was happy.", "- Byłam gotowa.\n- Byłam szczęśliwa.")]
    [InlineData("-I was ready.\n-I was happy.", "-Byłam gotowa.\n-Byłam szczęśliwa.")]
    [InlineData("<i>-I was ready.</i>\n<i>-I was happy.</i>", "<i>-Byłam gotowa.</i>\n<i>-Byłam szczęśliwa.</i>")]
    public void Review_TwoDialogueLinesInOneCue_SkipsAllGenderEdits(string sourceText, string translatedText)
    {
        var source = new[]
        {
            Cue(551, 0, 2, sourceText)
        };
        var translated = new[]
        {
            Cue(551, 0, 2, translatedText)
        };
        var speakers = new Dictionary<int, string?>
        {
            [551] = "SPEAKER_02"
        };
        var speakerGender = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_02"] = new(SpeakerVoiceGender.Male, 0.99, 3)
        };

        var result = new DeterministicGenderReviewService().Review(
            source,
            translated,
            speakers,
            speakerGender,
            hardVoiceTurnOnly: true);

        Assert.Equal(translatedText, result[0].Text);
    }

    [Fact]
    public void Review_HardVoiceOnlyUnknownPerCue_DoesNotFallBackToSpeakerOrDialogueResolvers()
    {
        var source = new[]
        {
            Cue(1, 0, 1, "What did you do?"),
            Cue(2, 1, 2, "Nothing.")
        };
        var translated = new[]
        {
            Cue(1, 0, 1, "Co zrobiłeś?"),
            Cue(2, 1, 2, "Nic.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_00",
            [2] = "SPEAKER_01"
        };
        var speakerEvidence = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_00"] = new(SpeakerVoiceGender.Male, 1.0, 3),
            ["SPEAKER_01"] = new(SpeakerVoiceGender.Female, 1.0, 3)
        };
        var cueGender = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [1] = new(SpeakerVoiceGender.Unknown, 0, 0.005, 1.0),
            [2] = new(SpeakerVoiceGender.Unknown, 0, 0.005, 1.0)
        };
        var diagnostics = new List<DeterministicGenderCueDiagnostic>();

        var result = new DeterministicGenderReviewService().Review(
            source,
            translated,
            speakers,
            speakerEvidence,
            cueGender,
            diagnostics,
            hardVoiceTurnOnly: true);

        Assert.Equal("Co zrobiłeś?", result[0].Text);
        var diagnostic = Assert.Single(diagnostics, item => item.CueId == 1);
        Assert.Equal("hard_voice_sequence", diagnostic.Resolver);
        Assert.False(diagnostic.Changed);
    }

    private static CueVoiceGenderEvidence Accepted(SpeakerVoiceGender gender, double confidence) =>
        new(gender, confidence, 0.10, 1.0, gender, confidence);

    private static SubtitleCue Cue(int id, double start, double end, string text) =>
        new(id, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);
}
