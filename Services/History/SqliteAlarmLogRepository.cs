using Microsoft.Data.Sqlite;
using Small_square_cavity_coating_machine.Models.History;
using System.IO;
using System.Text.Json;
using System.Threading.Channels;

namespace Small_square_cavity_coating_machine.Services.History;

/// <summary>UI-visible memory cache first; serialized background persistence with retry on failure.</summary>
public sealed class SqliteAlarmLogRepository : IAlarmLogRepository, IAsyncDisposable
{
    private sealed record WriteRequest(AlarmLogRecord? Record, TaskCompletionSource? Flush = null);
    private readonly InMemoryAlarmLogRepository _cache = new();
    private readonly Channel<WriteRequest> _writes = Channel.CreateUnbounded<WriteRequest>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly string _connectionString;
    private readonly Task _worker;
    private string _storageError = string.Empty;

    public SqliteAlarmLogRepository(string path)
    {
        _connectionString = new SqliteConnectionStringBuilder
        { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false, DefaultTimeout = 1 }.ToString();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Payload FROM AlarmHistory ORDER BY OccurredUtcTicks";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                _cache.Upsert(JsonSerializer.Deserialize<AlarmLogRecord>(reader.GetString(0))
                    ?? throw new InvalidDataException("报警历史包含无效记录"));
        }
        catch (Exception ex) { SetError($"报警历史加载失败：{ex.Message}"); }
        _worker = Task.Run(WriteLoopAsync);
    }

    public event EventHandler<AlarmLogRecord>? RecordAdded
    { add => _cache.RecordAdded += value; remove => _cache.RecordAdded -= value; }
    public event EventHandler<AlarmLogRecord>? RecordChanged
    { add => _cache.RecordChanged += value; remove => _cache.RecordChanged -= value; }
    public event EventHandler? StorageStatusChanged;
    public string StorageError => Volatile.Read(ref _storageError);

    public void Add(AlarmLogRecord record) => Upsert(record);
    public void Upsert(AlarmLogRecord record)
    {
        if (!_writes.Writer.TryWrite(new WriteRequest(record)))
            throw new ObjectDisposedException(nameof(SqliteAlarmLogRepository));
        _cache.Upsert(record);
    }
    public IReadOnlyList<AlarmLogRecord> Query(DateTimeOffset startInclusive, DateTimeOffset endInclusive)
        => _cache.Query(startInclusive, endInclusive);
    public IReadOnlyList<AlarmLogRecord> LoadUncleared() => _cache.LoadUncleared();

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_writes.Writer.TryWrite(new WriteRequest(null, completion)))
            throw new ObjectDisposedException(nameof(SqliteAlarmLogRepository));
        return completion.Task.WaitAsync(cancellationToken);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS AlarmHistory (
                    OccurrenceId TEXT PRIMARY KEY, OccurredUtcTicks INTEGER NOT NULL,
                    ClearedUtcTicks INTEGER NULL, Payload TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS IX_AlarmHistory_Time ON AlarmHistory(OccurredUtcTicks);
                """;
            command.ExecuteNonQuery();
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private async Task WriteLoopAsync()
    {
        var pending = new Dictionary<Guid, AlarmLogRecord>();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        while (await timer.WaitForNextTickAsync().ConfigureAwait(false))
        {
            var flushes = new List<TaskCompletionSource>();
            while (_writes.Reader.TryRead(out var request))
            {
                if (request.Record is { } record) pending[record.OccurrenceId] = record;
                if (request.Flush is { } flush) flushes.Add(flush);
            }
            Exception? failure = null;
            if (pending.Count > 0)
            {
                try
                {
                    using var connection = Open();
                    using var transaction = connection.BeginTransaction();
                    foreach (var record in pending.Values)
                    {
                        using var command = connection.CreateCommand();
                        command.Transaction = transaction;
                        command.CommandText = """
                            INSERT INTO AlarmHistory VALUES ($id, $time, $cleared, $payload)
                            ON CONFLICT(OccurrenceId) DO UPDATE SET
                            ClearedUtcTicks=excluded.ClearedUtcTicks, Payload=excluded.Payload;
                            """;
                        command.Parameters.AddWithValue("$id", record.OccurrenceId.ToString());
                        command.Parameters.AddWithValue("$time", record.OccurredAt.UtcTicks);
                        command.Parameters.AddWithValue("$cleared", (object?)record.ClearedAt?.UtcTicks ?? DBNull.Value);
                        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(record));
                        command.ExecuteNonQuery();
                    }
                    transaction.Commit();
                    pending.Clear();
                    SetError(string.Empty);
                }
                catch (Exception ex)
                {
                    failure = ex;
                    SetError($"报警记录保存失败（后台重试中）：{ex.Message}");
                }
            }
            foreach (var flush in flushes)
            {
                if (failure is null) flush.TrySetResult();
                else flush.TrySetException(failure);
            }
            if (_writes.Reader.Completion.IsCompleted) break;
        }
    }

    private void SetError(string error)
    {
        if (Interlocked.Exchange(ref _storageError, error) != error)
            StorageStatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        _writes.Writer.TryComplete();
        await _worker.ConfigureAwait(false);
    }
}
