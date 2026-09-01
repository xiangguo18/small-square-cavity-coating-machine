using Microsoft.Data.Sqlite;
using Small_square_cavity_coating_machine.Models.Equipment;
using Small_square_cavity_coating_machine.Models.History;
using System.IO;
using System.Text.Json;

namespace Small_square_cavity_coating_machine.Services.Equipment;

/// <summary>Separate from immutable definitions. Pending audit must be durable before any PLC write.</summary>
public sealed class EquipmentRuntimeRepository : IEquipmentRuntimeRepository
{
    private readonly Lock _gate = new();
    private readonly string _path;
    private readonly Dictionary<Guid, OperationLogRecord> _records = [];
    private readonly Dictionary<(string, string), ParameterCache> _cache = [];
    public string StorageError { get; private set; } = "";
    public event EventHandler<OperationLogRecord>? RecordAdded;

    public EquipmentRuntimeRepository(string path)
    {
        _path = path;
        try
        {
            using var connection = Open();
            using var query = connection.CreateCommand();
            query.CommandText = "SELECT Payload FROM Operations";
            var interrupted = new List<OperationLogRecord>();
            using (var reader = query.ExecuteReader())
                while (reader.Read())
                {
                    var record = JsonSerializer.Deserialize<OperationLogRecord>(reader.GetString(0))!;
                    if (record.Outcome is "待处理" or "配方运行中")
                    {
                        record = record with { Outcome = "结果未确认", FailureReason = "上次程序退出前未完成确认；需人工核对，未自动重放/续跑" };
                        interrupted.Add(record);
                    }
                    _records[record.OccurrenceId] = record;
                }
            query.CommandText = "SELECT Payload FROM ParameterCache";
            using (var reader = query.ExecuteReader())
                while (reader.Read())
                {
                    var cache = JsonSerializer.Deserialize<ParameterCache>(reader.GetString(0))!;
                    _cache[(cache.Endpoint, cache.Address)] = cache;
                }
            foreach (var record in interrupted) Persist(record, null);
        }
        catch (Exception ex) { StorageError = $"本机设备记录不可用：{ex.Message}"; }
    }

    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = _path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false, DefaultTimeout = 1 }.ToString());
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS Operations(Id TEXT PRIMARY KEY, Payload TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS ParameterCache(Endpoint TEXT NOT NULL, Address TEXT NOT NULL,
                    Payload TEXT NOT NULL, PRIMARY KEY(Endpoint,Address));
                """;
            command.ExecuteNonQuery();
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private void Persist(OperationLogRecord record, ParameterCache? cache)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO Operations(Id,Payload) VALUES($id,$payload) ON CONFLICT(Id) DO UPDATE SET Payload=excluded.Payload";
        command.Parameters.AddWithValue("$id", record.OccurrenceId.ToString());
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(record));
        command.ExecuteNonQuery();
        if (cache is not null)
        {
            command.Parameters.Clear();
            command.CommandText = "INSERT INTO ParameterCache(Endpoint,Address,Payload) VALUES($endpoint,$address,$payload) ON CONFLICT(Endpoint,Address) DO UPDATE SET Payload=excluded.Payload";
            command.Parameters.AddWithValue("$endpoint", cache.Endpoint);
            command.Parameters.AddWithValue("$address", cache.Address);
            command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(cache));
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void BeginWrite(OperationLogRecord record)
    {
        lock (_gate)
        {
            try { Persist(record, null); StorageError = ""; }
            catch (Exception ex) { StorageError = $"待处理审计保存失败：{ex.Message}"; throw new IOException(StorageError, ex); }
            _records[record.OccurrenceId] = record;
        }
        RecordAdded?.Invoke(this, record);
    }

    public void FinishWrite(OperationLogRecord record, ParameterCache? cache)
    {
        Exception? error = null;
        lock (_gate)
        {
            _records[record.OccurrenceId] = record;
            try
            {
                Persist(record, cache); StorageError = "";
                if (cache is not null) _cache[(cache.Endpoint, cache.Address)] = cache;
            }
            catch (Exception ex) { StorageError = $"本机记录保存失败：{ex.Message}"; error = new IOException(StorageError, ex); }
        }
        RecordAdded?.Invoke(this, record);
        if (error is not null) throw error;
    }

    public void Add(OperationLogRecord record)
    {
        try { FinishWrite(record, null); }
        catch (IOException) { /* Other operation logs remain visible in memory; parameter writes fail closed via BeginWrite. */ }
    }

    public IReadOnlyList<ParameterCache> LoadCache(string endpoint)
    {
        lock (_gate) return _cache.Values.Where(c => c.Endpoint == endpoint).ToArray();
    }

    public IReadOnlyList<OperationLogRecord> Query(DateTimeOffset startInclusive, DateTimeOffset endInclusive)
    {
        lock (_gate) return _records.Values.Where(r => r.Timestamp >= startInclusive && r.Timestamp <= endInclusive)
            .OrderByDescending(r => r.Timestamp).ToArray();
    }
}
