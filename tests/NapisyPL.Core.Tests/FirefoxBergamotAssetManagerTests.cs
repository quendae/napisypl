using NapisyPL.Core.OfflineMt.Bergamot;

namespace NapisyPL.Core.Tests;

public sealed class FirefoxBergamotAssetManagerTests
{
    [Fact]
    public void ResolveRegistry_SelectsHighestCompleteDesktopEnPlVersion()
    {
        var json = """
        {
          "data": [
            {
              "name": "model.enpl.intgemm.alphas.bin",
              "fromLang": "en",
              "toLang": "pl",
              "version": "2.0",
              "fileType": "model",
              "filter_expression": "",
              "attachment": { "hash": "old-model", "size": 10, "location": "old/model.bin", "filename": "model.enpl.intgemm.alphas.bin" }
            },
            {
              "name": "vocab.enpl.spm",
              "fromLang": "en",
              "toLang": "pl",
              "version": "2.0",
              "fileType": "vocab",
              "filter_expression": "",
              "attachment": { "hash": "old-vocab", "size": 11, "location": "old/vocab.spm", "filename": "vocab.enpl.spm" }
            },
            {
              "name": "lex.50.50.enpl.s2t.bin",
              "fromLang": "en",
              "toLang": "pl",
              "version": "2.0",
              "fileType": "lex",
              "filter_expression": "",
              "attachment": { "hash": "old-lex", "size": 12, "location": "old/lex.bin", "filename": "lex.50.50.enpl.s2t.bin" }
            },
            {
              "name": "model.enpl.intgemm.alphas.bin",
              "fromLang": "en",
              "toLang": "pl",
              "version": "2.1",
              "fileType": "model",
              "filter_expression": "",
              "attachment": { "hash": "new-model", "size": 31, "location": "main-workspace/translations-models/model-new.bin", "filename": "model.enpl.intgemm.alphas.bin" }
            },
            {
              "name": "vocab.enpl.spm",
              "fromLang": "en",
              "toLang": "pl",
              "version": "2.1",
              "fileType": "vocab",
              "filter_expression": "",
              "attachment": { "hash": "new-vocab", "size": 32, "location": "main-workspace/translations-models/vocab-new.spm", "filename": "vocab.enpl.spm" }
            },
            {
              "name": "lex.50.50.enpl.s2t.bin",
              "fromLang": "en",
              "toLang": "pl",
              "version": "2.1",
              "fileType": "lex",
              "filter_expression": "",
              "attachment": { "hash": "new-lex", "size": 33, "location": "main-workspace/translations-models/lex-new.bin", "filename": "lex.50.50.enpl.s2t.bin" }
            },
            {
              "name": "trgvocab.enpl.android.spm",
              "fromLang": "en",
              "toLang": "pl",
              "version": "9.9",
              "fileType": "trgvocab",
              "filter_expression": "env.appinfo.OS == 'Android'",
              "attachment": { "hash": "android", "size": 99, "location": "android/trg.spm", "filename": "trgvocab.enpl.android.spm" }
            }
          ]
        }
        """;

        var descriptor = FirefoxBergamotModelResolver.Resolve(json, "en", "pl");

        Assert.Equal("2.1", descriptor.ModelVersion);
        Assert.Equal("new-model", descriptor.Model.Sha256);
        Assert.Equal("new-vocab", descriptor.SourceVocab.Sha256);
        Assert.Same(descriptor.SourceVocab, descriptor.TargetVocab);
        Assert.Equal("new-lex", descriptor.Shortlist.Sha256);
        Assert.Equal(
            "https://firefox-settings-attachments.cdn.mozilla.net/main-workspace/translations-models/model-new.bin",
            descriptor.Model.Url);
    }

    [Fact]
    public void ResolveRegistry_SupportsSeparateSourceAndTargetVocabs()
    {
        var json = """
        {
          "data": [
            { "fromLang":"en", "toLang":"pl", "version":"3.0", "fileType":"model", "filter_expression":"", "attachment":{"hash":"m","size":1,"location":"m.bin","filename":"model.bin"} },
            { "fromLang":"en", "toLang":"pl", "version":"3.0", "fileType":"srcvocab", "filter_expression":"", "attachment":{"hash":"s","size":2,"location":"s.spm","filename":"source.spm"} },
            { "fromLang":"en", "toLang":"pl", "version":"3.0", "fileType":"trgvocab", "filter_expression":"", "attachment":{"hash":"t","size":3,"location":"t.spm","filename":"target.spm"} },
            { "fromLang":"en", "toLang":"pl", "version":"3.0", "fileType":"lex", "filter_expression":"", "attachment":{"hash":"l","size":4,"location":"l.bin","filename":"lex.bin"} }
          ]
        }
        """;

        var descriptor = FirefoxBergamotModelResolver.Resolve(json, "en", "pl");

        Assert.Equal("s", descriptor.SourceVocab.Sha256);
        Assert.Equal("t", descriptor.TargetVocab.Sha256);
        Assert.NotSame(descriptor.SourceVocab, descriptor.TargetVocab);
    }

    [Fact]
    public void ResolveRegistry_IncompleteNewerVersion_FallsBackToCompleteVersion()
    {
        var json = """
        {
          "data": [
            { "fromLang":"en", "toLang":"pl", "version":"2.1", "fileType":"model", "filter_expression":"", "attachment":{"hash":"m21","size":1,"location":"m21.bin","filename":"model.bin"} },
            { "fromLang":"en", "toLang":"pl", "version":"2.0", "fileType":"model", "filter_expression":"", "attachment":{"hash":"m20","size":1,"location":"m20.bin","filename":"model.bin"} },
            { "fromLang":"en", "toLang":"pl", "version":"2.0", "fileType":"vocab", "filter_expression":"", "attachment":{"hash":"v20","size":2,"location":"v20.spm","filename":"vocab.spm"} },
            { "fromLang":"en", "toLang":"pl", "version":"2.0", "fileType":"lex", "filter_expression":"", "attachment":{"hash":"l20","size":3,"location":"l20.bin","filename":"lex.bin"} }
          ]
        }
        """;

        var descriptor = FirefoxBergamotModelResolver.Resolve(json, "en", "pl");

        Assert.Equal("2.0", descriptor.ModelVersion);
        Assert.Equal("m20", descriptor.Model.Sha256);
    }
}
