using System.IO.Compression;
using System.Text;
using NapisyPL.Core.Subtitles;
using NapisyPL.Core.Subtitles.Online;

namespace NapisyPL.Core.Tests;

public class OnlineSubtitleSourceTests
{
    private const string Srt = "1\r\n00:00:01,000 --> 00:00:02,500\r\nZażółć gęślą jaźń\r\n\r\n2\r\n00:00:03,000 --> 00:00:04,000\r\nDruga\r\n";

    [Fact]
    public void EpisodeReleaseNameIsParsed()
    {
        var release = ReleaseName.Parse(@"Z:\seriale\Chance.S01E03.Hiring.It.Done.1080p.WEB-DL-tamir.AC3-5.1.x265.mkv");

        Assert.Equal("Chance", release.Title);
        Assert.Equal(1, release.Season);
        Assert.Equal(3, release.Episode);
        Assert.Contains("webdl", release.Tokens);
        Assert.Contains("tamir", release.Tokens);
    }

    [Fact]
    public void MovieReleaseNameIsParsed()
    {
        var release = ReleaseName.Parse("The.Matrix.1999.1080p.BluRay.x264-GROUP.mkv");

        Assert.Equal("The Matrix", release.Title);
        Assert.Equal(1999, release.Year);
        Assert.False(release.IsEpisode);
    }

    [Fact]
    public void RankingPrefersHashThenClosestReleaseAndDropsOtherEpisodes()
    {
        var video = ReleaseName.Parse("Chance.S01E03.Hiring.It.Done.1080p.WEB-DL-tamir.AC3-5.1.x265.mkv");
        OnlineSubtitleCandidate Candidate(string release, bool hash = false) =>
            new("X", SubtitleLanguage.Polish, release, hash, false, 10, release);

        var ranked = OnlineSubtitleRanking.Rank(
        [
            Candidate("Chance.S01E03.720p.HDTV.x264-KILLERS"),
            Candidate("Chance.S01E04.1080p.WEB-DL-tamir"),
            Candidate("Chance.S01E03.1080p.WEB-DL.DD5.1.H264-NTb"),
            Candidate("whatever", hash: true)
        ], video);

        Assert.Equal(["whatever", "Chance.S01E03.1080p.WEB-DL.DD5.1.H264-NTb", "Chance.S01E03.720p.HDTV.x264-KILLERS"],
            ranked.Select(candidate => candidate.ReleaseName));
    }

    [Fact]
    public void OpenSubtitlesQueryIsSortedAndLowerCase()
    {
        var query = new OnlineSubtitleQuery("Chance.S01E03.mkv", ReleaseName.Parse("Chance.S01E03.1080p.mkv"), "8e245d9679d31e12");

        Assert.Equal("episode_number=3&languages=pl&moviehash=8e245d9679d31e12&query=chance&season_number=1",
            OpenSubtitlesComSource.BuildSearchQuery(query, SubtitleLanguage.Polish));
    }

    [Fact]
    public void OpenSubtitlesSearchResponseIsParsed()
    {
        const string json = """
        {"total_count":2,"data":[
          {"id":"1","type":"subtitle","attributes":{"language":"pl","download_count":812,"hearing_impaired":false,
            "release":"Chance.S01E03.1080p.WEB-DL","moviehash_match":true,
            "files":[{"file_id":4321,"cd_number":1,"file_name":"chance.103.srt"}]}},
          {"id":"2","type":"subtitle","attributes":{"language":"pl","download_count":5,
            "release":"Chance.S01E03.CD","files":[{"file_id":1},{"file_id":2}]}}
        ]}
        """;

        var candidate = Assert.Single(OpenSubtitlesComSource.ParseSearch(json, SubtitleLanguage.Polish));
        Assert.True(candidate.IsHashMatch);
        Assert.Equal("4321", candidate.Token);
        Assert.Equal(812, candidate.DownloadCount);
    }

    [Fact]
    public void OpenSubtitlesDownloadWithoutLinkReportsTheMessage()
    {
        var exception = Assert.Throws<OnlineSubtitleSourceException>(() =>
            OpenSubtitlesComSource.ParseDownloadLink("""{"message":"You have downloaded your allowed 5 subtitles"}"""));
        Assert.Contains("allowed 5", exception.Message);
    }

    [Fact]
    public void SubDlSearchResponseKeepsOnlyThisEpisodeAndLanguage()
    {
        const string json = """
        {"status":true,"results":[{"sd_id":1,"type":"tv","name":"Chance"}],"subtitles":[
          {"release_name":"Chance.S01E03.1080p.WEB-DL","lang":"polish","language":"PL","url":"/subtitle/111-222.zip","season":1,"episode":3,"hi":false},
          {"release_name":"Chance.S01E04.1080p.WEB-DL","lang":"polish","language":"PL","url":"/subtitle/333-444.zip","season":1,"episode":4},
          {"release_name":"Chance.S01.Complete","lang":"polish","language":"PL","url":"subtitle/555.zip","season":1,"episode":0,"full_season":true},
          {"release_name":"Chance.S01E03.1080p.WEB-DL","lang":"english","language":"EN","url":"/subtitle/666.zip","season":1,"episode":3}
        ]}
        """;

        var candidates = SubDlSource.ParseSearch(json, SubtitleLanguage.Polish, ReleaseName.Parse("Chance.S01E03.mkv"));

        Assert.Equal(["/subtitle/111-222.zip", "/subtitle/555.zip"], candidates.Select(candidate => candidate.Token));
    }

    [Fact]
    public void SeasonPackArchiveYieldsThisEpisodeInWindows1250()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "Chance.S01E02.srt", "1\r\n00:00:01,000 --> 00:00:02,000\r\nInny odcinek\r\n");
            Add(archive, "Chance.S01E03.srt", Srt);
        }

        var cues = SubtitlePayload.Read(memory.ToArray(), ReleaseName.Parse("Chance.S01E03.mkv"));

        Assert.Equal("Zażółć gęślą jaźń", cues[0].Text);
        Assert.Equal(2, cues.Count);

        static void Add(ZipArchive archive, string name, string text)
        {
            using var stream = archive.CreateEntry(name).Open();
            var bytes = Encoding.GetEncoding(1250).GetBytes(text);
            stream.Write(bytes);
        }
    }

    [Fact]
    public void Utf8AndIso88592AreDecoded()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Assert.Equal("Zażółć gęślą jaźń", SubtitleTextDecoder.Decode(Encoding.UTF8.GetBytes("Zażółć gęślą jaźń")));
        Assert.Equal("Zażółć gęślą jaźń", SubtitleTextDecoder.Decode(Encoding.GetEncoding(28592).GetBytes("Zażółć gęślą jaźń")));
    }

    [Fact]
    public void MovieHashSumsLittleEndianWords()
    {
        var chunk = new byte[16];
        chunk[0] = 1;
        chunk[8] = 2;
        Assert.Equal(3UL, MovieHash.Sum(chunk));
    }
}
