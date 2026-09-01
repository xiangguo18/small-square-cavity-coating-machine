using Small_square_cavity_coating_machine.Models.History;

namespace Small_square_cavity_coating_machine.Services.History;

public interface ITelemetrySource : IAsyncDisposable
{
    event EventHandler<TelemetrySample>? SampleReceived;

    Task StartAsync(CancellationToken cancellationToken);
}

public interface ISessionTrendStore
{
    event EventHandler<TelemetrySample>? SampleAdded;

    void Append(TelemetrySample sample);

    IReadOnlyList<TelemetrySample> Snapshot();
}

public interface ITrendFileService
{
    Task SaveAsync(
        string path,
        IReadOnlyList<TelemetrySample> samples,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TelemetrySample>> LoadAsync(
        string path,
        CancellationToken cancellationToken = default);
}

public interface IHistoryFileDialogService
{
    string? SelectSavePath(string suggestedFileName);

    string? SelectOpenPath();
}

public interface IOperationLogRepository
{
    event EventHandler<OperationLogRecord>? RecordAdded;

    void Add(OperationLogRecord record);

    IReadOnlyList<OperationLogRecord> Query(DateTimeOffset startInclusive, DateTimeOffset endInclusive);
}

public interface IAlarmLogRepository
{
    event EventHandler<AlarmLogRecord>? RecordAdded;

    event EventHandler<AlarmLogRecord>? RecordChanged;

    event EventHandler? StorageStatusChanged;

    string StorageError { get; }

    void Add(AlarmLogRecord record);

    void Upsert(AlarmLogRecord record);

    IReadOnlyList<AlarmLogRecord> LoadUncleared();

    IReadOnlyList<AlarmLogRecord> Query(DateTimeOffset startInclusive, DateTimeOffset endInclusive);
}
