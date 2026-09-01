using Microsoft.Data.Sqlite;
using Small_square_cavity_coating_machine.Models.History;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Small_square_cavity_coating_machine.Services.History;

public sealed class SqliteProcessTrendRepository : IProcessTrendRepository
{
    private readonly string _path;
    private readonly Lock _gate = new();
    private bool _initialized;
    private static readonly JsonSerializerOptions Json = new() { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    public SqliteProcessTrendRepository(string path) => _path = path;

    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = _path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false, DefaultTimeout = 1 }.ToString());
        try
        {
            connection.Open();
            if (!_initialized)
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE IF NOT EXISTS ProcessRuns(Id TEXT PRIMARY KEY, StartedTicks INTEGER NOT NULL, Payload TEXT NOT NULL);
                    CREATE INDEX IF NOT EXISTS IX_ProcessRuns_Started ON ProcessRuns(StartedTicks);
                    CREATE TABLE IF NOT EXISTS ProcessSamples(RunId TEXT NOT NULL, Sequence INTEGER NOT NULL, Payload TEXT NOT NULL,
                        PRIMARY KEY(RunId,Sequence));
                    """;
                command.ExecuteNonQuery();
                command.CommandText = "SELECT Payload FROM ProcessRuns";
                var interrupted = new List<ProcessTrendRun>();
                using (var reader = command.ExecuteReader())
                    while (reader.Read())
                    {
                        var run = JsonSerializer.Deserialize<ProcessTrendRun>(reader.GetString(0), Json)!;
                        if (run.EndedAt is null && run.Status == "运行中")
                            interrupted.Add(run with { Status = "程序中断", FailureReason = "结束状态未确认；未恢复执行" });
                    }
                foreach (var run in interrupted)
                {
                    command.Parameters.Clear();
                    command.CommandText = "UPDATE ProcessRuns SET Payload=$payload WHERE Id=$id";
                    command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(run, Json));
                    command.Parameters.AddWithValue("$id", run.RunId.ToString());
                    command.ExecuteNonQuery();
                }
                _initialized = true;
            }
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    public void Save(ProcessTrendRun run, IReadOnlyList<TelemetrySample> samples, int firstSequence)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO ProcessRuns VALUES($id,$start,$payload) ON CONFLICT(Id) DO UPDATE SET Payload=excluded.Payload";
            command.Parameters.AddWithValue("$id", run.RunId.ToString());
            command.Parameters.AddWithValue("$start", run.StartedAt.UtcTicks);
            command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(run, Json));
            command.ExecuteNonQuery();
            command.Parameters.Clear();
            command.CommandText = "INSERT INTO ProcessSamples VALUES($id,$sequence,$payload) ON CONFLICT(RunId,Sequence) DO UPDATE SET Payload=excluded.Payload";
            command.Parameters.AddWithValue("$id", run.RunId.ToString());
            var sequence = command.Parameters.Add("$sequence", SqliteType.Integer);
            var payload = command.Parameters.Add("$payload", SqliteType.Text);
            for (var i = 0; i < samples.Count; i++)
            {
                sequence.Value = firstSequence + i;
                payload.Value = JsonSerializer.Serialize(samples[i], Json);
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    public IReadOnlyList<ProcessTrendRun> Query(DateTimeOffset start, DateTimeOffset endExclusive)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Payload FROM ProcessRuns WHERE StartedTicks >= $start AND StartedTicks < $end ORDER BY StartedTicks DESC";
            command.Parameters.AddWithValue("$start", start.UtcTicks);
            command.Parameters.AddWithValue("$end", endExclusive.UtcTicks);
            using var reader = command.ExecuteReader();
            var result = new List<ProcessTrendRun>();
            while (reader.Read()) result.Add(JsonSerializer.Deserialize<ProcessTrendRun>(reader.GetString(0), Json)!);
            return result;
        }
    }

    public IReadOnlyList<TelemetrySample> LoadSamples(Guid runId)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Payload FROM ProcessSamples WHERE RunId=$id ORDER BY Sequence";
            command.Parameters.AddWithValue("$id", runId.ToString());
            using var reader = command.ExecuteReader();
            var samples = new List<TelemetrySample>();
            while (reader.Read()) samples.Add(JsonSerializer.Deserialize<TelemetrySample>(reader.GetString(0), Json)!);
            return samples;
        }
    }
}
