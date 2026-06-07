namespace Phelix.Core.Session;

public record ToolCallRecord(
    string CallId,
    string Name,
    string ArgumentsJson,
    string Result,
    ToolCallStatus Status
);
