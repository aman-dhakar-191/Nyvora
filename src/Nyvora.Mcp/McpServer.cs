using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nyvora.Mcp;

/// <summary>
/// Minimal Model Context Protocol server over newline-delimited JSON-RPC (the stdio transport): initialize, ping,
/// tools/list and tools/call. Hand-written on purpose so it has no dependency that CI cannot pin down.
/// </summary>
public sealed class McpServer
{
    private static readonly string[] SupportedVersions = { "2025-06-18", "2025-03-26", "2024-11-05" };

    private readonly NyvoraTools _tools;
    private readonly string _version;

    public McpServer(NyvoraTools tools, string version)
    {
        _tools = tools;
        _version = version;
    }

    public void Run(TextReader input, TextWriter output)
    {
        string? line;
        while ((line = input.ReadLine()) is not null)
        {
            if (line.Trim().Length == 0)
            {
                continue;
            }

            var reply = Handle(line);
            if (reply is not null)
            {
                output.WriteLine(reply);
                output.Flush();
            }
        }
    }

    /// <summary>One incoming message to at most one outgoing line (null for notifications and client responses).</summary>
    public string? Handle(string line)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            return Error(null, -32700, "Parse error");
        }

        if (node is not JsonObject msg)
        {
            return Error(null, -32600, "Invalid request");
        }

        var id = msg["id"]?.DeepClone();
        var method = msg["method"] is JsonNode m && m.GetValueKind() == JsonValueKind.String ? (string?)m : null;
        if (method is null || id is null)
        {
            return null; // a notification (no id) or a response to us: nothing to send back
        }

        try
        {
            switch (method)
            {
                case "initialize":
                    var asked = msg["params"]?["protocolVersion"] is JsonNode v && v.GetValueKind() == JsonValueKind.String ? (string?)v : null;
                    return Result(id, new JsonObject
                    {
                        ["protocolVersion"] = asked is not null && SupportedVersions.Contains(asked) ? asked : SupportedVersions[0],
                        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                        ["serverInfo"] = new JsonObject { ["name"] = "nyvora", ["version"] = _version },
                        ["instructions"] = _tools.AllowWrite
                            ? "Nyvora task manager. Use list_tasks first to get task ids."
                            : "Nyvora task manager (read-only: the user has not allowed changes). Use list_tasks to read tasks.",
                    });
                case "ping":
                    return Result(id, new JsonObject());
                case "tools/list":
                    return Result(id, new JsonObject { ["tools"] = _tools.List() });
                case "tools/call":
                    var name = msg["params"]?["name"] is JsonNode n && n.GetValueKind() == JsonValueKind.String ? (string?)n : null;
                    if (name is null)
                    {
                        return Error(id, -32602, "Missing tool name");
                    }

                    var args = msg["params"]?["arguments"] as JsonObject ?? new JsonObject();
                    var outcome = _tools.Call(name, args);
                    if (outcome is null)
                    {
                        return Error(id, -32602, $"Unknown tool: {name}");
                    }

                    return Result(id, new JsonObject
                    {
                        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = outcome.Text }),
                        ["isError"] = outcome.IsError,
                    });
                default:
                    return Error(id, -32601, $"Method not found: {method}");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or InvalidCastException)
        {
            return Error(id, -32602, "Invalid parameters: " + ex.Message);
        }
    }

    private static string Result(JsonNode id, JsonObject result) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToJsonString();

    private static string Error(JsonNode? id, int code, string message) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        }.ToJsonString();
}
