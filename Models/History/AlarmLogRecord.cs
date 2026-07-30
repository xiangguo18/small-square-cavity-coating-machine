namespace Small_square_cavity_coating_machine.Models.History;

public sealed record AlarmLogRecord(
    DateTimeOffset OccurredAt,
    string Severity,
    string Source,
    string Message,
    DateTimeOffset? ClearedAt,
    bool IsSimulated)
{
    public string StatusText => ClearedAt.HasValue ? "已清除" : "未清除";

    public string DataSourceText => IsSimulated ? "模拟" : "实际";
}
