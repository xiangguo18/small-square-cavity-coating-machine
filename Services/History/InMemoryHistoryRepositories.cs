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
    public event EventHandler<AlarmLogRecord>? RecordChanged;
    public event EventHandler? StorageStatusChanged { add { } remove { } }
    public string StorageError => string.Empty;

    public void Add(AlarmLogRecord record) => Upsert(record);

    public void Upsert(AlarmLogRecord record)
    {
        lock (_gate)
        {
            var index = _records.FindIndex(item => item.OccurrenceId == record.OccurrenceId);
            if (index >= 0)
                _records[index] = record;
            else
                _records.Add(record);
            if (index < 0) RecordAdded?.Invoke(this, record);
            RecordChanged?.Invoke(this, record);
        }
    }

    public IReadOnlyList<AlarmLogRecord> LoadUncleared()
    {
        lock (_gate) return _records.Where(record => !record.ClearedAt.HasValue).ToArray();
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
