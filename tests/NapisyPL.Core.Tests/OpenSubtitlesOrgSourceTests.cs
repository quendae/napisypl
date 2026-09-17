using System.IO.Compression;
using System.Text;
using NapisyPL.Core.Models;
using NapisyPL.Core.Subtitles;
using NapisyPL.Core.Subtitles.Online;

namespace NapisyPL.Core.Tests;

public class OpenSubtitlesOrgSourceTests
{
    [Fact]
    public void PathsAreLowerCaseAndAlphabetical()
    {
        var release = ReleaseName.Parse("Chance.S01E01.The.Summer.of.Love.1080p.WEB-DL.mkv");

        Assert.Equal("episode-1/query-chance/season-1/sublanguageid-pol",
            OpenSubtitlesOrgSource.BuildQueryPath(release, SubtitleLanguage.Polish));
        Assert.Equal("moviebytesize-1234/moviehash-8e245d9679d31e12/sublanguageid-eng",
            OpenSubtitlesOrgSource.BuildHashPath("8E245D9679D31E12", 1234, SubtitleLanguage.English));
    }

    [Fact]
    public void SearchResponseIsParsed()
    {
        // Shape of a live response (2026-09-17), trimmed.
        const string json = """
        [{"MatchedBy":"fulltext","IDSubtitleFile":"1955355905","SubFileName":"Chance.S01E01.The.Summer.of.Love.720p.HULU.WEBRip.AAC2.0.H.264-NTb.srt",
          "SubFormat":"srt","SubSumCD":"1","SubDownloadsCnt":"1185","SubHearingImpaired":"0","SubAutoTranslation":"0",
          "MovieReleaseName":" Chance.S01E01.The.Summer.of.Love.720p.HULU.WEBRip.AAC2.0.H.264-NTb","SeriesSeason":"1","SeriesEpisode":"1",
          "SubDownloadLink":"https://dl.opensubtitles.org/en/download/src-api/vrf-19c80c58/filead/1955355905.gz"},
         {"MatchedBy":"moviehash","SubFormat":"srt","SubSumCD":"1","SubDownloadsCnt":"3","SubAutoTranslation":"0",
          "MovieReleaseName":"Chance.S01E01.1080p.WEB-DL","SubDownloadLink":"https://dl.opensubtitles.org/en/download/filead/2.gz"},
         {"MatchedBy":"fulltext","SubFormat":"srt","SubSumCD":"1","SubAutoTranslation":"1",
          "MovieReleaseName":"machine","SubDownloadLink":"https://dl.opensubtitles.org/en/download/filead/3.gz"},
         {"MatchedBy":"fulltext","SubFormat":"sub","SubSumCD":"1","MovieReleaseName":"microdvd","SubDownloadLink":"https://dl.opensubtitles.org/x/4.gz"}]
        """;

        var candidates = OpenSubtitlesOrgSource.ParseSearch(json, SubtitleLanguage.Polish);

        Assert.Equal(2, candidates.Count);
        Assert.Equal("Chance.S01E01.The.Summer.of.Love.720p.HULU.WEBRip.AAC2.0.H.264-NTb", candidates[0].ReleaseName);
        Assert.Equal(1185, candidates[0].DownloadCount);
        Assert.False(candidates[0].IsHashMatch);
        Assert.True(candidates[1].IsHashMatch);
    }

    [Fact]
    public void GzipPayloadLosesTheInjectedAdvertisement()
    {
        const string srt = "1\n00:00:06,000 --> 00:00:12,074\nNaciśnij play. Napisy się pojawiają.\nPonad 200 języków. Jedna aplikacja:  tryray.app\n\n" +
                           "2\n00:00:27,900 --> 00:00:29,899\nTłumaczenie: Alex\n\n" +
                           "3\n00:00:31,000 --> 00:00:33,000\nWidziałem to na chess.com, serio.\n\n" +
                           "4\n00:00:35,000 --> 00:00:36,000\nDobra.\n";
        using var memory = new MemoryStream();
        using (var gzip = new GZipStream(memory, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes(srt));

        var cues = SubtitlePayload.Read(memory.ToArray(), ReleaseName.Parse("Chance.S01E01.mkv"));

        // Edge cues with a web address go, including the one that is dialogue: a price worth
        // paying at the very start or end of a file.
        Assert.Equal(["Tłumaczenie: Alex", "Dobra."], cues.Select(cue => cue.Text));
        Assert.Equal([1, 2], cues.Select(cue => cue.Index));
    }

    [Fact]
    public void DialogueInTheMiddleIsNeverFiltered()
    {
        var cues = Enumerable.Range(1, 20)
            .Select(index => new SubtitleCue(index, TimeSpan.FromSeconds(index * 3), TimeSpan.FromSeconds(index * 3 + 2),
                index == 10 ? "Wejdź na www.example.com" : "Kwestia " + index))
            .ToArray();

        Assert.Equal(20, SubtitlePayload.RemoveAdvertisements(cues).Count);
    }
}
