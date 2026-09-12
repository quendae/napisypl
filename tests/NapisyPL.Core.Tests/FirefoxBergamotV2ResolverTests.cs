using NapisyPL.Core.OfflineMt.Bergamot;

namespace NapisyPL.Core.Tests;

public sealed class FirefoxBergamotV2ResolverTests
{
    [Fact]
    public void ResolveRegistryV2_PrefersBaseArchitectureOverNewerTiny()
    {
        var json = """
        {
          "data": [
            {
              "name":"model.enpl.base.bin",
              "sourceLanguage":"en",
              "targetLanguage":"pl",
              "architecture":"base",
              "version":"3.0",
              "fileType":"model",
              "decompressedHash":"base-model-raw",
              "decompressedSize":101,
              "filter_expression":"",
              "attachment":{"hash":"base-model-zst","size":51,"location":"main-workspace/translations-models-v2/base-model.zst","filename":"base-model.zst"}
            },
            {
              "name":"vocab.enpl.base.spm",
              "sourceLanguage":"en",
              "targetLanguage":"pl",
              "architecture":"base",
              "version":"3.0",
              "fileType":"vocab",
              "decompressedHash":"base-vocab-raw",
              "decompressedSize":102,
              "filter_expression":"",
              "attachment":{"hash":"base-vocab-zst","size":52,"location":"main-workspace/translations-models-v2/base-vocab.zst","filename":"base-vocab.zst"}
            },
            {
              "name":"lex.enpl.base.bin",
              "sourceLanguage":"en",
              "targetLanguage":"pl",
              "architecture":"base",
              "version":"3.0",
              "fileType":"lex",
              "decompressedHash":"base-lex-raw",
              "decompressedSize":103,
              "filter_expression":"",
              "attachment":{"hash":"base-lex-zst","size":53,"location":"main-workspace/translations-models-v2/base-lex.zst","filename":"base-lex.zst"}
            },
            {
              "name":"model.enpl.tiny.bin",
              "sourceLanguage":"en",
              "targetLanguage":"pl",
              "architecture":"tiny",
              "version":"9.0",
              "fileType":"model",
              "decompressedHash":"tiny-model-raw",
              "decompressedSize":201,
              "filter_expression":"",
              "attachment":{"hash":"tiny-model-zst","size":61,"location":"main-workspace/translations-models-v2/tiny-model.zst","filename":"tiny-model.zst"}
            },
            {
              "name":"vocab.enpl.tiny.spm",
              "sourceLanguage":"en",
              "targetLanguage":"pl",
              "architecture":"tiny",
              "version":"9.0",
              "fileType":"vocab",
              "decompressedHash":"tiny-vocab-raw",
              "decompressedSize":202,
              "filter_expression":"",
              "attachment":{"hash":"tiny-vocab-zst","size":62,"location":"main-workspace/translations-models-v2/tiny-vocab.zst","filename":"tiny-vocab.zst"}
            },
            {
              "name":"lex.enpl.tiny.bin",
              "sourceLanguage":"en",
              "targetLanguage":"pl",
              "architecture":"tiny",
              "version":"9.0",
              "fileType":"lex",
              "decompressedHash":"tiny-lex-raw",
              "decompressedSize":203,
              "filter_expression":"",
              "attachment":{"hash":"tiny-lex-zst","size":63,"location":"main-workspace/translations-models-v2/tiny-lex.zst","filename":"tiny-lex.zst"}
            }
          ]
        }
        """;

        var descriptor = FirefoxBergamotModelResolver.Resolve(json, "en", "pl");

        Assert.Equal("3.0", descriptor.ModelVersion);
        Assert.Equal("base-model-raw", descriptor.Model.Sha256);
        Assert.Equal(101, descriptor.Model.SizeBytes);
        Assert.Equal(
            "https://firefox-settings-attachments.cdn.mozilla.net/main-workspace/translations-models-v2/base-model.zst",
            descriptor.Model.Url);
    }
}
