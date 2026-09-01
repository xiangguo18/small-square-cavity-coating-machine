using Microsoft.Data.Sqlite;
using Small_square_cavity_coating_machine.Models.Equipment;
using Small_square_cavity_coating_machine.Models.History;
using Small_square_cavity_coating_machine.Services.Equipment;

namespace Small_square_cavity_coating_machine.Services.History;

public interface IProcessDefinitionRepository
{
    IReadOnlyList<ProcessDefinition> Load();
}

public sealed class SqliteProcessDefinitionRepository(string path) : IProcessDefinitionRepository
{
    public IReadOnlyList<ProcessDefinition> Load()
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,Name,\"Group\",DataType,Address,Unit,Min,Max,Des FROM ProcessDef ORDER BY Id";
        using var reader = command.ExecuteReader();
        var result = new List<ProcessDefinition>();
        while (reader.Read())
        {
            string Text(int i) => reader.IsDBNull(i) ? "" : reader.GetString(i).Trim();
            result.Add(new(reader.GetInt32(0), Text(1), Text(2), Text(3), Text(4), Text(5),
                reader.GetDouble(6), reader.GetDouble(7), Text(8)));
        }
        Validate(result);
        return result;
    }

    public static void Validate(IReadOnlyList<ProcessDefinition> definitions)
    {
        if (definitions.Count != 7 || !definitions.Select(p => p.Id).Order().SequenceEqual(Enumerable.Range(0, 7)))
            throw new InvalidOperationException("ProcessDef必须包含唯一的0～6号曲线定义");
        var indices = definitions.Select(p => EquipmentAddress.Index(p.Address, EquipmentGroups.Process)).ToArray();
        if (indices.Distinct().Count() != definitions.Count)
            throw new InvalidOperationException("ProcessDef包含重复数组地址");
        string[] units = ["Pa", "Pa", "V", "A", "V", "A", "℃"];
        foreach (var p in definitions)
            if (p.DataType != "REAL" || p.Unit != units[p.Id] || string.IsNullOrWhiteSpace(p.Name)
                || !double.IsFinite(p.Min) || !double.IsFinite(p.Max) || p.Min > p.Max)
                throw new InvalidOperationException($"ProcessDef定义类型、单位或范围无效：{p.Id} / {p.Address}");
    }
}
