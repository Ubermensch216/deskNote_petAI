using System.Text.Json;

namespace DeskNote.Ai;

/// <summary>
/// JSON Schemas handed to Ollama's <c>format</c> parameter for the structured actions.
/// </summary>
/// <remarks>
/// Report p11 asks for schema-enforced output rather than prose parsing: constrained decoding
/// means a malformed answer is impossible to emit, and whatever does come back is still validated
/// in <see cref="OllamaAiService"/> before it reaches a reminder or a tag.
/// </remarks>
internal static class AiSchemas
{
    internal static JsonElement ExtractedTasks { get; } = Parse("""
        {
          "type": "object",
          "properties": {
            "tasks": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "title": { "type": "string" },
                  "dueAt": { "type": ["string", "null"] },
                  "priority": { "type": "string", "enum": ["low", "normal", "high"] },
                  "assignee": { "type": ["string", "null"] }
                },
                "required": ["title", "priority"]
              }
            }
          },
          "required": ["tasks"]
        }
        """);

    internal static JsonElement SuggestedTags { get; } = Parse("""
        {
          "type": "object",
          "properties": {
            "tags": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "name": { "type": "string" },
                  "confidence": { "type": "number" }
                },
                "required": ["name", "confidence"]
              }
            }
          },
          "required": ["tags"]
        }
        """);

    private static JsonElement Parse(string schema) => JsonDocument.Parse(schema).RootElement.Clone();
}
