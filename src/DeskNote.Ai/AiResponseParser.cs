using System.Globalization;
using System.Text.Json;
using DeskNote.Core.Ai;
using DeskNote.Core.Services;

namespace DeskNote.Ai;

/// <summary>
/// Turns a structured model response into domain objects, dropping anything that fails validation.
/// </summary>
/// <remarks>
/// Constrained decoding makes malformed output unlikely, not impossible, and a schema cannot say
/// "this date is real". So the rule here is: a value that cannot be validated is discarded rather
/// than guessed at. A task with an unreadable due date survives without one — the user still sees
/// the task, and no reminder is ever created from a date nobody could parse (report p11).
/// </remarks>
public static class AiResponseParser
{
    /// <summary>Tags proposed beyond this are dropped; a long list is noise the user has to reject.</summary>
    private const int MaxTags = 5;

    public static IReadOnlyList<ExtractedTask> ReadTasks(string json)
    {
        using var document = Parse(json);

        if (!document.RootElement.TryGetProperty("tasks", out var tasks)
            || tasks.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<ExtractedTask>();

        foreach (var item in tasks.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            // Cleaned here rather than where it is appended, so the confirmation list shows the
            // exact line the note is about to receive.
            var title = AiTextCleanup.ToNoteLine(ReadString(item, "title"));
            if (string.IsNullOrEmpty(title))
            {
                continue;
            }

            result.Add(new ExtractedTask
            {
                Title = title,
                DueAt = ReadDate(ReadString(item, "dueAt")),
                Priority = ReadPriority(ReadString(item, "priority")),
                Assignee = ReadString(item, "assignee") is { Length: > 0 } assignee
                    ? assignee.Trim()
                    : null,
            });
        }

        return result;
    }

    public static IReadOnlyList<SuggestedTag> ReadTags(string json)
    {
        using var document = Parse(json);

        if (!document.RootElement.TryGetProperty("tags", out var tags)
            || tags.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<SuggestedTag>();

        foreach (var item in tags.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = AiTextCleanup.ToNoteLine(ReadString(item, "name")).TrimStart('#');

            // The note body is the source of truth for tags, and TagParser only recognises a
            // single unbroken word. A suggestion the user could not have typed is not offered.
            if (string.IsNullOrEmpty(name) || name.Any(char.IsWhiteSpace) || !seen.Add(name))
            {
                continue;
            }

            var confidence = item.TryGetProperty("confidence", out var raw)
                && raw.ValueKind == JsonValueKind.Number
                && raw.TryGetDouble(out var value)
                    ? Math.Clamp(value, 0, 1)
                    : 0;

            result.Add(new SuggestedTag(name, confidence));

            if (result.Count == MaxTags)
            {
                break;
            }
        }

        return result;
    }

    private static JsonDocument Parse(string json)
    {
        try
        {
            return JsonDocument.Parse(Unfence(json));
        }
        catch (JsonException ex)
        {
            throw new AiProviderException($"The model returned output that is not JSON: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes a Markdown code fence. Constrained decoding should not produce one, but a model
    /// that wraps its answer anyway is a formatting slip, not a reason to lose the result.
    /// </summary>
    private static string Unfence(string json)
    {
        var text = json.Trim();

        if (!text.StartsWith("```", StringComparison.Ordinal))
        {
            return text;
        }

        var firstBreak = text.IndexOf('\n');
        var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);

        return firstBreak < 0 || lastFence <= firstBreak
            ? text
            : text[(firstBreak + 1)..lastFence].Trim();
    }

    private static string? ReadString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? ReadDate(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out var parsed)
            ? parsed
            : null;

    private static TaskPriority ReadPriority(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "high" => TaskPriority.High,
        "low" => TaskPriority.Low,
        _ => TaskPriority.Normal,
    };
}
