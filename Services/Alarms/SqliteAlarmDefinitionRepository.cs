using Microsoft.Data.Sqlite;
using Small_square_cavity_coating_machine.Models.Alarms;
using System.IO;
using System.Security.Cryptography;

namespace Small_square_cavity_coating_machine.Services.Alarms;

public sealed class SqliteAlarmDefinitionRepository(string path) : IAlarmDefinitionRepository
{
    // Versioned extraction keeps the embedded master intact, without overwriting a working database.
    public static string ExtractEmbeddedDatabase(string directory)
    {
        using var resource = typeof(SqliteAlarmDefinitionRepository).Assembly
            .GetManifestResourceStream("Equipment.Seed.db")
            ?? throw new InvalidOperationException("缺少内置方腔.db资源");
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, $"方腔.definition-{hash[..16]}.db");
        if (!File.Exists(target))
        {
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            output.Write(bytes);
        }
        if (!SHA256.HashData(File.ReadAllBytes(target)).AsSpan().SequenceEqual(SHA256.HashData(bytes)))
            throw new InvalidOperationException("报警定义库校验失败，请恢复原始定义库");
        return target;
    }

    public IReadOnlyList<AlarmDefinition> Load()
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Priority, Address, Message, Chinese, English FROM EventDef ORDER BY Id";
        using var reader = command.ExecuteReader();
        var definitions = new List<AlarmDefinition>();
        while (reader.Read())
        {
            string Text(int index) => reader.IsDBNull(index) ? string.Empty : reader.GetString(index).Trim();
            definitions.Add(new AlarmDefinition(reader.GetInt32(0), Text(1), reader.GetInt32(2),
                Text(3), Text(4), Text(5), Text(6)));
        }
        if (definitions.Count == 0 || definitions.Any(d => string.IsNullOrWhiteSpace(d.Address))
            || definitions.Select(d => d.Id).Distinct().Count() != definitions.Count
            || definitions.Select(d => d.Address).Distinct(StringComparer.Ordinal).Count() != definitions.Count)
            throw new InvalidOperationException("EventDef为空、地址缺失或存在重复定义");
        return definitions;
    }
}
