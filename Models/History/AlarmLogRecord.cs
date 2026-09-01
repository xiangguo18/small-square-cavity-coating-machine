using Small_square_cavity_coating_machine.Models.Alarms;

namespace Small_square_cavity_coating_machine.Models.History;

public sealed record AlarmLogRecord(
    DateTimeOffset OccurredAt,
    string Severity,
    string Source,
    string Message,
    DateTimeOffset? ClearedAt,
    bool IsSimulated)
{
    public Guid OccurrenceId { get; init; } = Guid.NewGuid();
    public int EventDefinitionId { get; init; } = -1;
    public string Address { get; init; } = string.Empty;
    public int Priority { get; init; }
    public AlarmQuality Quality { get; init; } = AlarmQuality.Good;
    public bool ObservedOnConnect { get; init; }
    public bool ObservedAfterQualityGap { get; init; }
    public bool RecoveryObservedAfterGap { get; init; }
    public DateTimeOffset? SourceTimestamp { get; init; }
    public string StatusText => ClearedAt.HasValue ? "已清除"
        : Quality != AlarmQuality.Good ? "状态未知" : "未清除";
    public string TimingNote => ObservedOnConnect
        ? "连接时已存在（报警时间为发现时间）" : ObservedAfterQualityGap
        ? "信号恢复时已存在（报警时间为发现时间）" : string.Empty;
    public string RecoveryNote => RecoveryObservedAfterGap
        ? "信号恢复后确认正常（清除时间为确认时间）" : string.Empty;

    public string DataSourceText => IsSimulated ? "模拟" : "实际";
}
