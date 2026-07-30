using Small_square_cavity_coating_machine.Models.History;

namespace Small_square_cavity_coating_machine.Services.History;

public sealed class InMemoryOperationLogRepository : IOperationLogRepository
{
    private readonly Lock _gate = new();
    private readonly List<OperationLogRecord> _records = [];

    public event EventHandler<OperationLogRecord>? RecordAdded;

    public void Add(OperationLogRecord record)
    {
        lock (_gate)
        {
            _records.Add(record);
        }

        RecordAdded?.Invoke(this, record);
    }

    public IReadOnlyList<OperationLogRecord> Query(
        DateTimeOffset startInclusive,
        DateTimeOffset endInclusive)
    {
        lock (_gate)
        {
            return _records
                .Where(record => record.Timestamp >= startInclusive && record.Timestamp <= endInclusive)
                .OrderByDescending(record => record.Timestamp)
                .ToArray();
        }
    }
}

public sealed class InMemoryAlarmLogRepository : IAlarmLogRepository
{
    private readonly Lock _gate = new();
    private readonly List<AlarmLogRecord> _records = [];

    public event EventHandler<AlarmLogRecord>? RecordAdded;

    public void Add(AlarmLogRecord record)
    {
        lock (_gate)
        {
            _records.Add(record);
        }

        RecordAdded?.Invoke(this, record);
    }

    public IReadOnlyList<AlarmLogRecord> Query(
        DateTimeOffset startInclusive,
        DateTimeOffset endInclusive)
    {
        lock (_gate)
        {
            return _records
                .Where(record => record.OccurredAt >= startInclusive && record.OccurredAt <= endInclusive)
                .OrderByDescending(record => record.OccurredAt)
                .ToArray();
        }
    }
}
