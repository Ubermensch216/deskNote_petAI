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

    /// <summary>
    /// One moment, and optionally a rule for repeating it.
    /// </summary>
    /// <remarks>
    /// <c>freq</c> is an enum rather than free text so the model cannot invent a frequency the
    /// scheduler does not implement — <c>Recurrence</c> reads exactly these four.
    /// </remarks>
    internal static JsonElement ParsedReminder { get; } = Parse("""
        {
          "type": "object",
          "properties": {
            "dueAt": { "type": ["string", "null"] },
            "freq": { "type": ["string", "null"], "enum": ["daily", "weekly", "monthly", "yearly", null] },
            "interval": { "type": ["integer", "null"] }
          },
          "required": ["dueAt"]
        }
        """);

    /// <summary>A single line, which is all a note title ever is.</summary>
    internal static JsonElement SuggestedTitle { get; } = Parse("""
        {
          "type": "object",
          "properties": {
            "title": { "type": "string" }
          },
          "required": ["title"]
        }
        """);

    private static JsonElement Parse(string schema) => JsonDocument.Parse(schema).RootElement.Clone();
}
