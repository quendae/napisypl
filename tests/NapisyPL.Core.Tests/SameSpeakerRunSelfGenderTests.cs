using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

/// <summary>
/// Maximum Pleasure Guaranteed S01E10, Paula's monologue #305-#310: the speaker label
/// is a mixed cluster with no usable gender, and the MT mixed the genders itself.
/// </summary>
public class SameSpeakerRunSelfGenderTests
{
    private static SubtitleCue Cue(int id, double start, double end, string text) =>
        new(id, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);

    private static (SubtitleCue[] Source, SubtitleCue[] Translated) Monologue() =>
    (
        new[]
        {
            Cue(305, 1130.0, 1134.6, "When my marriage ended, it destroyed me."),
            Cue(306, 1134.7, 1137.1, "I was a wreck. I…"),
            Cue(307, 1138.3, 1142.6, "But I had a kid,\nso, I couldn't be a wreck, you know."),
            Cue(308, 1142.7, 1147.8, "I wasn't gonna meet some guy\nat the farmers' market or at a book club."),
            Cue(309, 1147.9, 1151.2, "So, I went online for some companionship,"),
            Cue(310, 1151.3, 1157.4, "like, I could schedule that between\nschool pickups and work deadlines.")
        },
        new[]
        {
            Cue(305, 1130.0, 1134.6, "Kiedy moje małżeństwo się skończyło, zniszczyło mnie."),
            Cue(306, 1134.7, 1137.1, "Byłem jak wrak. I..."),
            Cue(307, 1138.3, 1142.6, "Ale ja miałam dziecko, więc nie mogłam być wrakiem, wiesz."),
            Cue(308, 1142.7, 1147.8, "Nie miałam zamiaru spotkać się z jakimś facetem na targu czy w klubie książkowym."),
            Cue(309, 1147.9, 1151.2, "Więc poszedłem do Internetu po towarzystwo,"),
            Cue(310, 1151.3, 1157.4, "Mogłabym to zaplanować między odbiorem w szkole a terminami w pracy.")
        }
    );

    private static Dictionary<int, string?> AllSpeaker(string label) =>
        Enumerable.Range(305, 6).ToDictionary(id => id, _ => (string?)label);

    private static IReadOnlyList<SubtitleCue> Review(
        SubtitleCue[] source,
        SubtitleCue[] translated,
        Dictionary<int, string?> speakers,
        Dictionary<int, CueVoiceGenderEvidence>? cues = null) =>
        new DeterministicGenderReviewService().Review(
            source, translated, speakers, new Dictionary<string, SpeakerGenderEvidence>(),
            cues ?? new Dictionary<int, CueVoiceGenderEvidence>(), hardVoiceTurnOnly: true);

    [Fact]
    public void MasculineLinesInsideAFeminineMonologue_FollowTheRest()
    {
        var (source, translated) = Monologue();

        var result = Review(source, translated, AllSpeaker("SPEAKER_03"));

        Assert.Equal("Byłam jak wrak. I...", result[1].Text);
        Assert.Equal("Więc poszłam do Internetu po towarzystwo,", result[4].Text);
        Assert.Equal(translated[2].Text, result[2].Text);
    }

    [Fact]
    public void ALoneFeminineNeighbour_IsNotEnough()
    {
        var (source, translated) = Monologue();
        translated[3] = translated[3] with { Text = "Nie zamierzałem spotkać się z jakimś facetem." };
        translated[5] = translated[5] with { Text = "Mogę to zaplanować między odbiorem w szkole a terminami w pracy." };

        var result = Review(source, translated, AllSpeaker("SPEAKER_03"));

        Assert.Equal("Więc poszedłem do Internetu po towarzystwo,", result[4].Text);
    }

    [Fact]
    public void ConfidentMalePitch_KeepsTheMasculineLine()
    {
        var (source, translated) = Monologue();
        var cues = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [306] = new(SpeakerVoiceGender.Unknown, 0, 0.02, 2.4, SpeakerVoiceGender.Male, 0.80)
        };

        var result = Review(source, translated, AllSpeaker("SPEAKER_03"), cues);

        Assert.Equal("Byłem jak wrak. I...", result[1].Text);
    }

    [Fact]
    public void DifferentSpeakerLabels_DoNotFormARun()
    {
        var (source, translated) = Monologue();
        var speakers = AllSpeaker("SPEAKER_03");
        speakers[307] = "SPEAKER_01";
        speakers[308] = "SPEAKER_01";

        var result = Review(source, translated, speakers);

        Assert.Equal("Byłem jak wrak. I...", result[1].Text);
    }

    [Fact]
    public void AMasculineMtNeighbour_BlocksTheRun()
    {
        var (source, translated) = Monologue();
        translated[3] = translated[3] with { Text = "Nie zamierzałem spotkać się z jakimś facetem." };

        var result = Review(source, translated, AllSpeaker("SPEAKER_03"));

        // 306 and 309 both see 307 (feminine) and 308 (masculine): disagreement,
        // so nothing changes even though 310 is feminine too.
        Assert.Equal("Byłem jak wrak. I...", result[1].Text);
        Assert.Equal("Więc poszedłem do Internetu po towarzystwo,", result[4].Text);
    }
}
