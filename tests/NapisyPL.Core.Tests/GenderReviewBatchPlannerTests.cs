using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

public sealed class GenderReviewBatchPlannerTests
{
    [Fact]
    public void BuildReviewBatches_OneHundredCandidatesFitInTenRequests()
    {
        var cues = Enumerable.Range(1, 471).Select(i => Cue(i, $"Linia {i}")).ToArray();
        var candidateIds = Enumerable.Range(0, 100)
            .Select(i => 2 + i * 4)
            .ToHashSet();

        var batches = GenderReviewCandidateSelector.BuildReviewBatches(
            cues,
            candidateIds,
            radius: 2,
            maxCandidatesPerBatch: 10,
            maxContextCuesPerBatch: 50);

        Assert.Equal(10, batches.Count);
        Assert.All(batches, batch => Assert.InRange(batch.CandidateIds.Count, 1, 10));
        Assert.All(batches, batch => Assert.InRange(batch.Cues.Count, 1, 50));
        foreach (var id in candidateIds)
            Assert.Contains(batches, batch => batch.CandidateIds.Contains(id));
    }

    [Fact]
    public void BuildReviewBatches_KeepsOnlyNeighbourhoodsInsteadOfLongContiguousRanges()
    {
        var cues = Enumerable.Range(1, 200).Select(i => Cue(i, $"Linia {i}")).ToArray();
        var candidateIds = new HashSet<int> { 10, 100, 190 };

        var batch = Assert.Single(GenderReviewCandidateSelector.BuildReviewBatches(
            cues,
            candidateIds,
            radius: 1,
            maxCandidatesPerBatch: 10,
            maxContextCuesPerBatch: 20));

        Assert.Equal(new[] { 9, 10, 11, 99, 100, 101, 189, 190, 191 }, batch.Cues.Select(cue => cue.Index).ToArray());
    }

    private static SubtitleCue Cue(int id, string text) =>
        new(id, TimeSpan.FromSeconds(id), TimeSpan.FromSeconds(id + 1), text);
}
