using NapisyPL.Core.ContextResolution;

namespace NapisyPL.Core.Tests;

public sealed class PhraseLexiconTests
{
    [Fact]
    public void LoadJson_GenderedMarriageRule_RewritesMaleAddresseeNaturally()
    {
        const string json = """
        {
          "version": 1,
          "entries": [
            {
              "id": "get-married",
              "category": "gendered_phrase",
              "sourcePatterns": ["get married", "got married"],
              "male": {
                "replace": "ożeniłeś się",
                "translatedPatterns": ["wyszłaś za mąż", "wyszedłeś za mąż"]
              },
              "female": {
                "replace": "wyszłaś za mąż",
                "translatedPatterns": ["ożeniłeś się", "ożeniłaś się"]
              },
              "priority": 100,
              "source": "SubFlow",
              "license": "MIT"
            }
          ]
        }
        """;

        var lexicon = PhraseLexicon.LoadJson(json);

        var result = lexicon.ApplyGenderedAddressee(
            "Wait. You got married?",
            "Zatrzymaj się. Wyszłaś za mąż?",
            SpeakerVoiceGender.Male);

        Assert.Equal("Zatrzymaj się. Ożeniłeś się?", result);
    }

    [Fact]
    public void LoadJson_DoesNotRewriteWhenEnglishSourceDoesNotMatchRule()
    {
        const string json = """
        {
          "version": 1,
          "entries": [
            {
              "id": "get-married",
              "category": "gendered_phrase",
              "sourcePatterns": ["get married", "got married"],
              "male": {
                "replace": "ożeniłeś się",
                "translatedPatterns": ["wyszłaś za mąż"]
              },
              "female": {
                "replace": "wyszłaś za mąż",
                "translatedPatterns": ["ożeniłeś się"]
              },
              "priority": 100
            }
          ]
        }
        """;

        var lexicon = PhraseLexicon.LoadJson(json);

        var result = lexicon.ApplyGenderedAddressee(
            "She left with her husband.",
            "Wyszłaś za mąż?",
            SpeakerVoiceGender.Male);

        Assert.Equal("Wyszłaś za mąż?", result);
    }

    [Fact]
    public void LoadCsv_ImportsGenderedPhraseRule()
    {
        const string csv = "id,category,source_patterns,male_patterns,male_replace,female_patterns,female_replace,priority\n" +
                           "get-married,gendered_phrase,get married|got married,wyszłaś za mąż|wyszedłeś za mąż,ożeniłeś się,ożeniłeś się|ożeniłaś się,wyszłaś za mąż,100\n";

        var lexicon = PhraseLexicon.LoadCsv(csv);

        var result = lexicon.ApplyGenderedAddressee(
            "Did you get married?",
            "Wyszedłeś za mąż?",
            SpeakerVoiceGender.Male);

        Assert.Equal("Ożeniłeś się?", result);
    }

    [Fact]
    public void DefaultLexicon_ContainsMarriageRule()
    {
        var lexicon = PhraseLexicon.LoadDefault();

        var result = lexicon.ApplyGenderedAddressee(
            "You got married?",
            "Wyszłaś za mąż?",
            SpeakerVoiceGender.Male);

        Assert.Equal("Ożeniłeś się?", result);
    }
}
