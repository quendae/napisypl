using System.Text.Json;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.LocalTranslation;

public abstract record LocalTranslatorCommand;

public sealed record LoadCommand(string ModelPath) : LocalTranslatorCommand;
public sealed record TranslateCommand(string JobId, IReadOnlyList<TranslationSegment> Segments) : LocalTranslatorCommand;
public sealed record ShutdownCommand : LocalTranslatorCommand;

public abstract record LocalTranslatorEvent;

public sealed record ReadyEvent(string Model, string Version) : LocalTranslatorEvent;
public sealed record SegmentEvent(string JobId, int Id, string Text) : LocalTranslatorEvent;
public sealed record LocalProgressEvent(string JobId, int Completed, int Total) : LocalTranslatorEvent;
public sealed record CompleteEvent(string JobId) : LocalTranslatorEvent;
public sealed record LocalErrorEvent(string? JobId, string Code, string Message) : LocalTranslatorEvent;

public static class LocalTranslatorProtocol
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string SerializeCommand(LocalTranslatorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command switch
        {
            LoadCommand load => SerializeLoad(load),
            TranslateCommand translate => SerializeTranslate(translate),
            ShutdownCommand => JsonSerializer.Serialize(new { type = "shutdown" }, JsonOptions),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command.GetType().Name, "Nieznany typ polecenia lokalnego tłumacza.")
        };
    }

    public static LocalTranslatorEvent ParseEvent(string jsonLine)
    {
        if (string.IsNullOrWhiteSpace(jsonLine))
            throw new InvalidDataException("Lokalny tłumacz zwrócił pustą linię protokołu.");

        try
        {
            using var document = JsonDocument.Parse(jsonLine);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Zdarzenie lokalnego tłumacza nie jest obiektem JSON.");

            var type = RequiredString(root, "type");
            return type switch
            {
                "ready" => new ReadyEvent(RequiredString(root, "model"), RequiredString(root, "version")),
                "segment" => new SegmentEvent(
                    RequiredString(root, "jobId"),
                    RequiredInt(root, "id", minimum: 1),
                    RequiredStringAllowEmpty(root, "text")),
                "progress" => ParseProgress(root),
                "complete" => new CompleteEvent(RequiredString(root, "jobId")),
                "error" => new LocalErrorEvent(
                    OptionalString(root, "jobId"),
                    RequiredString(root, "code"),
                    RequiredString(root, "message")),
                _ => throw new InvalidDataException($"Nieznany typ zdarzenia lokalnego tłumacza: {type}.")
            };
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Lokalny tłumacz zwrócił nieprawidłowy JSON.", ex);
        }
    }

    private static string SerializeLoad(LoadCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.ModelPath))
            throw new ArgumentException("Ścieżka modelu lokalnego tłumacza nie może być pusta.", nameof(command));

        return JsonSerializer.Serialize(new
        {
            type = "load",
            modelPath = command.ModelPath
        }, JsonOptions);
    }

    private static string SerializeTranslate(TranslateCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.JobId))
            throw new ArgumentException("Identyfikator zadania lokalnego tłumacza nie może być pusty.", nameof(command));
        if (command.Segments is null || command.Segments.Count == 0)
            throw new ArgumentException("Lokalne zadanie tłumaczenia musi zawierać co najmniej jeden segment.", nameof(command));
        if (command.Segments.Any(segment => segment.Id <= 0))
            throw new ArgumentException("Identyfikatory segmentów lokalnego tłumacza muszą być dodatnie.", nameof(command));
        if (command.Segments.Select(segment => segment.Id).Distinct().Count() != command.Segments.Count)
            throw new ArgumentException("Identyfikatory segmentów lokalnego tłumacza muszą być unikalne.", nameof(command));

        return JsonSerializer.Serialize(new
        {
            type = "translate",
            jobId = command.JobId,
            segments = command.Segments.Select(segment => new { id = segment.Id, text = segment.Text }).ToArray()
        }, JsonOptions);
    }

    private static LocalProgressEvent ParseProgress(JsonElement root)
    {
        var jobId = RequiredString(root, "jobId");
        var completed = RequiredInt(root, "completed", minimum: 0);
        var total = RequiredInt(root, "total", minimum: 1);
        if (completed > total)
            throw new InvalidDataException("Postęp lokalnego tłumacza ma completed większe niż total.");
        return new LocalProgressEvent(jobId, completed, total);
    }

    private static string RequiredString(JsonElement root, string name)
    {
        var value = RequiredStringAllowEmpty(root, name);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"Pole {name} nie może być puste.");
        return value;
    }

    private static string RequiredStringAllowEmpty(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Brak wymaganego pola tekstowego {name}.");
        return property.GetString() ?? string.Empty;
    }

    private static string? OptionalString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
            return null;
        if (property.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Pole {name} musi być tekstem.");
        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int RequiredInt(JsonElement root, string name, int minimum)
    {
        if (!root.TryGetProperty(name, out var property) || !property.TryGetInt32(out var value) || value < minimum)
            throw new InvalidDataException($"Pole {name} musi być liczbą całkowitą >= {minimum}.");
        return value;
    }
}
