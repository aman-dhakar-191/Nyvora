namespace Nyvora.Mcp;

/// <summary>A problem the caller can fix (bad id, unknown status...). Reported to the agent as a tool error, not a crash.</summary>
public sealed class ToolError : Exception
{
    public ToolError(string message) : base(message) { }
}
