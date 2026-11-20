using System.Text.Json;

namespace Agent.Core.Ui;

/// <summary>Strict JSON Schema for requesting an Agent UI document from Codex App Server.</summary>
public static class AgentUiDocumentSchema
{
    private static readonly JsonElement Schema = CreateSchema();

    private static JsonElement CreateSchema()
    {
        using var document = JsonDocument.Parse("""
    {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "schemaVersion": { "type": "integer", "const": 1 },
        "title": { "type": ["string", "null"], "maxLength": 120 },
        "blocks": {
          "type": "array", "minItems": 1, "maxItems": 32,
          "items": {
            "type": "object", "additionalProperties": false,
            "properties": {
              "type": { "type": "string", "enum": ["text", "card", "code", "status", "keyValue", "list", "checklist", "progress", "action"] },
              "title": { "type": ["string", "null"], "maxLength": 120 },
              "text": { "type": ["string", "null"], "maxLength": 8000 },
              "language": { "type": ["string", "null"], "enum": ["text", "json", "csharp", "rust", "typescript", "javascript", "python", "sql", "bash", "xml", null] },
              "values": { "type": ["array", "null"], "maxItems": 12, "items": { "type": "object", "additionalProperties": false, "properties": { "key": { "type": "string", "maxLength": 80 }, "value": { "type": "string", "maxLength": 1000 } }, "required": ["key", "value"] } },
              "items": { "type": ["array", "null"], "maxItems": 30, "items": { "type": "string", "maxLength": 1000 } },
              "status": { "type": ["string", "null"], "enum": ["info", "success", "warning", "error", "pending", "running", "completed", "failed", null] },
              "actionId": { "type": ["string", "null"], "enum": ["navigate.chat", "navigate.agents", "navigate.projects", "navigate.history", "navigate.context", "navigate.plugins", "navigate.settings", null] }
            },
            "required": ["type", "title", "text", "language", "values", "items", "status", "actionId"]
          }
        }
      },
      "required": ["schemaVersion", "title", "blocks"]
    }
    """);
        return document.RootElement.Clone();
    }

    public static JsonElement Create() => Schema.Clone();
                                                }
                                                
