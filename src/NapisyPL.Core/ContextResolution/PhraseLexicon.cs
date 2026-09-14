using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NapisyPL.Core.ContextResolution;

public sealed class PhraseLexicon
{
    private const string DefaultFileName = "phrase_lexicon.en-pl.json";
    private static readonly Lazy<PhraseLexicon> Default = new(LoadDefaultCore);

    private readonly IReadOnlyList<PhraseLexiconEntry> _entries;

    private PhraseLexicon(IEnumerable<PhraseLexiconEntry> entries)
    {
        _entries = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
            .OrderByDescending(entry => entry.Priority)
            .ToArray();
    }

    public int Count => _entries.Count;

    public static PhraseLexicon LoadDefault() => Default.Value;

    public static PhraseLexicon LoadJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var document = JsonSerializer.Deserialize<PhraseLexiconDocument>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        if (document is null || document.Version <= 0)
            throw new InvalidDataException("Phrase lexicon JSON must contain a positive version.");

        return new PhraseLexicon(document.Entries ?? []);
    }

    public static PhraseLexicon LoadCsv(string csv)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(csv);

        var rows = ParseCsv(csv).ToArray();
        if (rows.Length < 2)
            return new PhraseLexicon([]);

        var headers = rows[0]
            .Select((value, index) => (Name: value.Trim(), Index: index))
            .ToDictionary(item => item.Name, item => item.Index, StringComparer.OrdinalIgnoreCase);

        string Field(IReadOnlyList<string> row, string name)
        {
            if (!headers.TryGetValue(name, out var index) || index >= row.Count)
                return string.Empty;
            return row[index].Trim();
        }

        var entries = new List<PhraseLexiconEntry>();
        foreach (var row in rows.Skip(1))
        {
            var id = Field(row, "id");
            if (string.IsNullOrWhiteSpace(id))
                continue;

            _ = int.TryParse(Field(row, "priority"), out var priority);
            entries.Add(new PhraseLexiconEntry
            {
                Id = id,
                Category = Field(row, "category"),
                SourcePatterns = SplitPatterns(Field(row, "source_patterns")),
                Male = new PhraseLexiconGenderVariant
                {
                    TranslatedPatterns = SplitPatterns(Field(row, "male_patterns")),
                    Replace = Field(row, "male_replace")
                },
                Female = new PhraseLexiconGenderVariant
                {
                    TranslatedPatterns = SplitPatterns(Field(row, "female_patterns")),
                    Replace = Field(row, "female_replace")
                },
                Priority = priority,
                Source = Field(row, "source"),
                License = Field(row, "license")
            });
        }

        return new PhraseLexicon(entries);
    }

    public static PhraseLexicon LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var content = File.ReadAllText(path, Encoding.UTF8);
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".json" => LoadJson(content),
            ".csv" => LoadCsv(content),
            _ => throw new NotSupportedException($"Unsupported phrase lexicon format: {Path.GetExtension(path)}")
        };
    }

    public string ApplyGenderedAddressee(
        string? sourceText,
        string translatedText,
        SpeakerVoiceGender gender)
    {
        if (string.IsNullOrEmpty(translatedText) || gender == SpeakerVoiceGender.Unknown)
            return translatedText;

        var result = translatedText;
        foreach (var entry in _entries)
        {
            if (!string.Equals(entry.Category, "gendered_phrase", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!SourceMatches(entry.SourcePatterns, sourceText))
                continue;

            var variant = gender == SpeakerVoiceGender.Male ? entry.Male : entry.Female;
            if (variant is null || string.IsNullOrWhiteSpace(variant.Replace))
                continue;

            foreach (var pattern in variant.TranslatedPatterns ?? [])
            {
                if (string.IsNullOrWhiteSpace(pattern))
                    continue;

                var regex = BuildPhraseRegex(pattern);
                result = regex.Replace(
                    result,
                    match => MatchCasing(match.Value, variant.Replace),
                    count: 1);
            }
        }

        return result;
    }

    private static PhraseLexicon LoadDefaultCore()
    {
        var externalPath = Path.Combine(AppContext.BaseDirectory, DefaultFileName);
        if (File.Exists(externalPath))
            return LoadFile(externalPath);

        var assembly = typeof(PhraseLexicon).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(DefaultFileName, StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
            throw new InvalidOperationException($"Embedded phrase lexicon '{DefaultFileName}' was not found.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not open embedded phrase lexicon '{resourceName}'.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return LoadJson(reader.ReadToEnd());
    }

    private static bool SourceMatches(IReadOnlyList<string>? sourcePatterns, string? sourceText)
    {
        if (sourcePatterns is null || sourcePatterns.Count == 0)
            return true;
        if (string.IsNullOrWhiteSpace(sourceText))
            return true;

        return sourcePatterns.Any(pattern =>
            !string.IsNullOrWhiteSpace(pattern) && BuildPhraseRegex(pattern).IsMatch(sourceText));
    }

    private static Regex BuildPhraseRegex(string pattern)
    {
        var escaped = string.Join(
            @"\s+",
            Regex.Split(pattern.Trim(), @"\s+")
                .Where(part => part.Length > 0)
                .Select(Regex.Escape));

        return new Regex(
            $@"(?<!\p{{L}}){escaped}(?!\p{{L}})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string MatchCasing(string source, string replacement)
    {
        if (source.All(character => !char.IsLetter(character) || char.IsUpper(character)))
            return replacement.ToUpperInvariant();

        if (source.Length > 0 && char.IsUpper(source[0]) && replacement.Length > 0)
            return char.ToUpperInvariant(replacement[0]) + replacement[1..];

        return replacement;
    }

    private static List<string> SplitPatterns(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static IEnumerable<IReadOnlyList<string>> ParseCsv(string text)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quoted)
            {
                if (character == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(character);
                }
                continue;
            }

            switch (character)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    if (row.Any(value => !string.IsNullOrWhiteSpace(value)))
                        yield return row.ToArray();
                    row.Clear();
                    break;
                default:
                    field.Append(character);
                    break;
            }
        }

        row.Add(field.ToString());
        if (row.Any(value => !string.IsNullOrWhiteSpace(value)))
            yield return row.ToArray();
    }

    private sealed class PhraseLexiconDocument
    {
        public int Version { get; set; }
        public List<PhraseLexiconEntry>? Entries { get; set; }
    }

    private sealed class PhraseLexiconEntry
    {
        public string Id { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public List<string>? SourcePatterns { get; set; }
        public PhraseLexiconGenderVariant? Male { get; set; }
        public PhraseLexiconGenderVariant? Female { get; set; }
        public int Priority { get; set; }
        public string? Source { get; set; }
        public string? License { get; set; }
    }

    private sealed class PhraseLexiconGenderVariant
    {
        public List<string>? TranslatedPatterns { get; set; }
        public string Replace { get; set; } = string.Empty;
    }
}
