using NapisyPL.Core.ContextResolution;

namespace NapisyPL.Core.Tests;

public sealed class ContextResolverProtocolTests
{
    [Fact]
    public void ParseResponse_AcceptsSpeakerGenderAndAddresseeMetadata()
    {
        const string json = """
        {
          "speakers": {
            "SPEAKER_01": { "gender": "female", "confidence": 0.96 },
            "SPEAKER_02": { "gender": "male", "confidence": 0.91 }
          },
          "lines": {
            "118": { "addressee": "SPEAKER_01", "confidence": 0.83 },
            "119": { "addressee": "unknown", "confidence": 0.42 }
          }
        }
        """;

        var result = ContextResolverProtocol.ParseResponse(json);

        Assert.Equal(SpeakerGender.Female, result.Speakers["SPEAKER_01"].Gender);
        Assert.Equal(0.96, result.Speakers["SPEAKER_01"].Confidence, 2);
        Assert.Equal(SpeakerGender.Male, result.Speakers["SPEAKER_02"].Gender);
        Assert.Equal("SPEAKER_01", result.Lines[118].Addressee);
        Assert.Equal(0.83, result.Lines[118].Confidence, 2);
    }

    [Fact]
    public void ParseResponse_RejectsInvalidGender()
    {
        const string json = """
        { "speakers": { "SPEAKER_01": { "gender": "probably_female", "confidence": 0.8 } }, "lines": {} }
        """;

        Assert.Throws<InvalidDataException>(() => ContextResolverProtocol.ParseResponse(json));
    }

    [Fact]
    public void ParseResponse_RejectsConfidenceOutsideZeroToOne()
    {
        const string json = """
        { "speakers": { "SPEAKER_01": { "gender": "female", "confidence": 1.2 } }, "lines": {} }
        """;

        Assert.Throws<InvalidDataException>(() => ContextResolverProtocol.ParseResponse(json));
    }

    [Fact]
    public void BuildPrompt_UsesNoThinkAndRequestsMetadataOnly()
    {
        var cues = new[]
        {
            new ContextCue(118, "SPEAKER_01", "Are you ready?", null),
            new ContextCue(119, "SPEAKER_02", "I was born ready.", "John")
        };

        var prompt = ContextResolverProtocol.BuildPrompt(cues);

        Assert.Contains("/no_think", prompt);
        Assert.Contains("SPEAKER_01", prompt);
        Assert.Contains("118", prompt);
        Assert.Contains("Are you ready?", prompt);
        Assert.Contains("metadata", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("translate the subtitles", prompt, StringComparison.OrdinalIgnoreCase);
    }
}
