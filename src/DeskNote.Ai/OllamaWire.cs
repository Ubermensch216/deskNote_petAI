using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeskNote.Ai;

/// <summary>Request and response shapes of the Ollama HTTP API, kept in one place.</summary>
internal static class OllamaWire
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal sealed record Message(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    internal sealed record ChatRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("messages")]
        public required IReadOnlyList<Message> Messages { get; init; }

        [JsonPropertyName("stream")]
        public bool Stream { get; init; }

        /// <summary>A JSON Schema that constrains decoding, so structured actions cannot return prose.</summary>
        [JsonPropertyName("format")]
        public JsonElement? Format { get; init; }

        [JsonPropertyName("options")]
        public ChatOptions? Options { get; init; }
    }

    internal sealed record ChatOptions
    {
        [JsonPropertyName("temperature")]
        public double Temperature { get; init; }
    }

    internal sealed record ChatResponse
    {
        [JsonPropertyName("message")]
        public Message? Message { get; init; }

        [JsonPropertyName("done")]
        public bool Done { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }
    }

    internal sealed record TagsResponse
    {
        [JsonPropertyName("models")]
        public IReadOnlyList<ModelEntry> Models { get; init; } = [];
    }

    internal sealed record ModelEntry
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; init; }

        /// <summary>Bytes of the loaded model currently resident in VRAM; present only on /api/ps.</summary>
        [JsonPropertyName("size_vram")]
        public long SizeVram { get; init; }
    }
}
