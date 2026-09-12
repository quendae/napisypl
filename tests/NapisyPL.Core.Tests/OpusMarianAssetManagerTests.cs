using NapisyPL.Core.OfflineMt.Bergamot;

namespace NapisyPL.Core.Tests;

public sealed class OpusMarianAssetManagerTests
{
    [Fact]
    public void PinnedDescriptor_UsesExpectedEngPolRelease()
    {
        var descriptor = OpusMarianModelDescriptor.Pinned;

        Assert.Equal("opus-marian-eng-pol-2021-02-19", descriptor.BackendId);
        Assert.Equal("eng", descriptor.SourceLanguage);
        Assert.Equal("pol", descriptor.TargetLanguage);
        Assert.Equal("2021-02-19", descriptor.Release);
        Assert.Equal(
            "https://object.pouta.csc.fi/Tatoeba-MT-models/eng-pol/opus-2021-02-19.zip",
            descriptor.ArchiveUrl);
        Assert.Equal("normalization + SentencePiece spm32k/spm32k", descriptor.Preprocessing);
    }

    [Theory]
    [InlineData("../evil.bin")]
    [InlineData("nested/../../evil.bin")]
    [InlineData("/absolute.bin")]
    [InlineData("C:\\absolute.bin")]
    public void ResolveArchiveEntryPath_RejectsTraversalAndAbsolutePaths(string entryName)
    {
        var root = Path.Combine(Path.GetTempPath(), "subflow-opus-test-root");

        Assert.Throws<InvalidDataException>(() =>
            OpusMarianAssetManager.ResolveArchiveEntryPath(root, entryName));
    }

    [Fact]
    public void ResolveArchiveEntryPath_AllowsSafeNestedEntry()
    {
        var root = Path.Combine(Path.GetTempPath(), "subflow-opus-test-root");

        var resolved = OpusMarianAssetManager.ResolveArchiveEntryPath(root, "model/model.npz");

        Assert.StartsWith(Path.GetFullPath(root), resolved, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("model", "model.npz"), resolved, StringComparison.OrdinalIgnoreCase);
    }
}
