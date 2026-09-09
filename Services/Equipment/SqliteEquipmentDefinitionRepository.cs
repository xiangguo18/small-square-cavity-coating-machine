using Microsoft.Data.Sqlite;
using Small_square_cavity_coating_machine.Models.Equipment;
using System.Globalization;

namespace Small_square_cavity_coating_machine.Services.Equipment;

public interface IEquipmentDefinitionRepository
{
    IReadOnlyList<IoDefinition> LoadIo();
    IReadOnlyList<ParameterDefinition> LoadParameters();
    EquipmentControlDefinitions LoadControls();
}

public static class EquipmentAddress
{
    public static string Group(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) throw new InvalidOperationException("设备地址为空");
        var bracket = address.IndexOf('[', StringComparison.Ordinal);
        return bracket > 0 && address.EndsWith(']') ? address[..bracket] : address;
    }

    public static int Index(string address, string group)
    {
        var prefix = group + "[";
        if (!address.StartsWith(prefix, StringComparison.Ordinal) || !address.EndsWith(']')
            || !int.TryParse(address.AsSpan(prefix.Length, address.Length - prefix.Length - 1),
                NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            throw new InvalidOperationException($"无效的{group}数组地址：{address}");
        return index;
    }
}

public sealed class SqliteEquipmentDefinitionRepository(string path) : IEquipmentDefinitionRepository
{
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    public IReadOnlyList<IoDefinition> LoadIo()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,Name,Type,Location,Module,Address,Des,Chinese,English FROM IO ORDER BY Id";
        using var reader = command.ExecuteReader();
        var result = new List<IoDefinition>();
        while (reader.Read())
        {
            string Text(int i) => reader.IsDBNull(i) ? "" : reader.GetString(i).Trim();
            var row = new IoDefinition(reader.GetInt32(0), Text(1), Text(2), Text(3), Text(4), Text(5), Text(6), Text(7), Text(8));
            EquipmentAddress.Index(row.Address, EquipmentGroups.Io);
            result.Add(row);
        }
        ValidateUnique(result.Select(r => r.Address), result.Select(r => r.Id));
        return result;
    }

    public IReadOnlyList<ParameterDefinition> LoadParameters()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,Name,Unit,Address,Value,Location,Type,Chinese,DefaultValue,MinValue,MaxValue FROM Parameter ORDER BY Id";
        using var reader = command.ExecuteReader();
        var result = new List<ParameterDefinition>();
        while (reader.Read())
        {
            string Text(int i) => reader.IsDBNull(i) ? "" : reader.GetString(i).Trim();
            var row = new ParameterDefinition(reader.GetInt32(0), Text(1), Text(2), Text(3), reader.GetDouble(4),
                Text(5), Text(6), Text(7), reader.GetDouble(8), reader.GetDouble(9), reader.GetDouble(10));
            EquipmentAddress.Index(row.Address, EquipmentGroups.Parameter);
            if (!double.IsFinite(row.MinValue) || !double.IsFinite(row.MaxValue) || row.MinValue > row.MaxValue)
                throw new InvalidOperationException($"参数范围无效：{row.Address}");
            result.Add(row);
        }
        ValidateUnique(result.Select(r => r.Address), result.Select(r => r.Id));
        return result;
    }

    public EquipmentControlDefinitions LoadControls()
    {
        using var connection = Open();
        var parts = new Dictionary<int, PartDefinition>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id,Location,Name,Node,Interlock FROM Part ORDER BY Id";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string Text(int i) => reader.IsDBNull(i) ? "" : reader.GetString(i).Trim();
                var row = new PartDefinition(reader.GetInt32(0), Text(1), Text(2), Text(3), Text(4));
                if (!string.IsNullOrWhiteSpace(row.StateAddress))
                    EquipmentAddress.Index(row.StateAddress, EquipmentGroups.PartState);
                if (!parts.TryAdd(row.Id, row)) throw new InvalidOperationException($"Part重复编号：{row.Id}");
            }
        }

        var stateAddresses = parts.Values.Where(p => !string.IsNullOrWhiteSpace(p.StateAddress))
            .Select(p => p.StateAddress).ToArray();
        if (stateAddresses.Distinct(StringComparer.Ordinal).Count() != stateAddresses.Length)
            throw new InvalidOperationException("Part包含重复的Part_State地址");

        var commands = new Dictionary<int, PartCommandDefinition>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id,PartId,Command,Chinese,Node,Info,InterlockNode FROM PartCommand ORDER BY Id";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string Text(int i) => reader.IsDBNull(i) ? "" : reader.GetString(i).Trim();
                var row = new PartCommandDefinition(reader.GetInt32(0), reader.GetInt32(1), Text(2), Text(3),
                    Text(4), Text(5), Text(6));
                EquipmentAddress.Index(row.Address, EquipmentGroups.PartCommand);
                if (EquipmentAddress.Index(row.Address, EquipmentGroups.PartCommand) != row.Id)
                    throw new InvalidOperationException($"PartCommand编号与数组下标不一致：{row.Id}");
                if (!string.IsNullOrWhiteSpace(row.InterlockAddress))
                    EquipmentAddress.Index(row.InterlockAddress, EquipmentGroups.Interlock);
                if (!parts.ContainsKey(row.PartId)) throw new InvalidOperationException($"PartCommand引用不存在的Part：{row.Id}/{row.PartId}");
                if (!commands.TryAdd(row.Id, row)) throw new InvalidOperationException($"PartCommand重复编号：{row.Id}");
            }
        }

        var data = new Dictionary<int, PartDataDefinition>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id,PartId,Name,Chinese,Type,Node,SetNode,Min,Max,Unit FROM PartData ORDER BY Id";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string Text(int i) => reader.IsDBNull(i) ? "" : reader.GetString(i).Trim();
                var row = new PartDataDefinition(reader.GetInt32(0), reader.GetInt32(1), Text(2), Text(3), Text(4),
                    Text(5), Text(6), reader.GetDouble(7), reader.GetDouble(8), Text(9));
                EquipmentAddress.Index(row.Address, EquipmentGroups.PartData);
                if (EquipmentAddress.Index(row.Address, EquipmentGroups.PartData) != row.Id)
                    throw new InvalidOperationException($"PartData编号与数组下标不一致：{row.Id}");
                if (!string.IsNullOrWhiteSpace(row.SetAddress))
                {
                    var setGroup = EquipmentAddress.Group(row.SetAddress);
                    if (setGroup != EquipmentGroups.PartDataSet1 && setGroup != EquipmentGroups.PartDataSet2)
                        throw new InvalidOperationException($"PartData设定地址组无效：{row.SetAddress}");
                    EquipmentAddress.Index(row.SetAddress, setGroup);
                }
                if (!double.IsFinite(row.Minimum) || !double.IsFinite(row.Maximum) || row.Minimum > row.Maximum)
                    throw new InvalidOperationException($"PartData范围无效：{row.Address}");
                if (!data.TryAdd(row.Id, row)) throw new InvalidOperationException($"PartData重复编号：{row.Id}");
            }
        }

        var systemCommands = new Dictionary<string, SystemCommandDefinition>(StringComparer.Ordinal);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Name,Chinese,CommandNode,FeedbackNode,RequiresBuiltInAdmin FROM SystemCommandDef ORDER BY Name";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string Text(int i) => reader.IsDBNull(i) ? "" : reader.GetString(i).Trim();
                // EnableNode 为历史兼容字段，运行时不再读取其内容（使能由 PLC 内部处理）。
                var row = new SystemCommandDefinition(Text(0), Text(1), Text(2), "", Text(3), reader.GetInt32(4) != 0);
                if (string.IsNullOrWhiteSpace(row.Name) || string.IsNullOrWhiteSpace(row.CommandAddress))
                    throw new InvalidOperationException("SystemCommandDef名称或命令地址为空");
                ValidateScalar(row.CommandAddress, "系统命令");
                if (!string.IsNullOrWhiteSpace(row.FeedbackAddress)) ValidateFeedbackAddress(row.FeedbackAddress);
                if (!systemCommands.TryAdd(row.Name, row)) throw new InvalidOperationException($"SystemCommandDef重复：{row.Name}");
            }
        }

        var interlocks = new Dictionary<int, InterlockDefinition>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id,DeviceKey,Action,Chinese,Address FROM InterlockDef ORDER BY Id";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string Text(int i) => reader.IsDBNull(i) ? "" : reader.GetString(i).Trim();
                var row = new InterlockDefinition(reader.GetInt32(0), Text(1), Text(2), Text(3), Text(4));
                EquipmentAddress.Index(row.Address, EquipmentGroups.Interlock);
                if (EquipmentAddress.Index(row.Address, EquipmentGroups.Interlock) != row.Id)
                    throw new InvalidOperationException($"InterlockDef编号与数组下标不一致：{row.Id}");
                if (!interlocks.TryAdd(row.Id, row)) throw new InvalidOperationException($"InterlockDef重复：{row.Id}");
            }
        }

        foreach (var command in commands.Values.Where(c => !string.IsNullOrWhiteSpace(c.InterlockAddress)))
            if (!interlocks.Values.Any(i => i.Address == command.InterlockAddress))
                throw new InvalidOperationException($"PartCommand互锁定义不存在：{command.Id}/{command.InterlockAddress}");

        ValidateUnique(commands.Values.Select(c => c.Address), commands.Keys);
        ValidateUnique(data.Values.Select(d => d.Address), data.Keys);
        ValidateUnique(interlocks.Values.Select(i => i.Address), interlocks.Keys);

        var addresses = parts.Values.Select(p => p.StateAddress)
            .Concat(commands.Values.SelectMany(p => new[] { p.Address, p.InterlockAddress }))
            .Concat(data.Values.SelectMany(p => new[] { p.Address, p.SetAddress }))
            .Concat(systemCommands.Values.SelectMany(p => new[] { p.CommandAddress, p.EnableAddress, p.FeedbackAddress }))
            .Concat(interlocks.Values.Select(p => p.Address))
            .Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal).ToArray();
        return new(parts, commands, data, systemCommands, interlocks, addresses);
    }

    private static void ValidateScalar(string address, string description)
    {
        if (!EquipmentGroups.IsScalar(address) || !EquipmentGroups.Monitored.Contains(address, StringComparer.Ordinal))
            throw new InvalidOperationException($"{description}地址不是受支持的标量点：{address}");
    }

    /// <summary>系统反馈地址允许受支持的标量点，或用于按钮绿灯的 Part_State 数组元素。</summary>
    private static void ValidateFeedbackAddress(string address)
    {
        if (EquipmentGroups.IsScalar(address) && EquipmentGroups.Monitored.Contains(address, StringComparer.Ordinal))
            return;
        if (EquipmentAddress.Group(address) == EquipmentGroups.PartState)
        {
            _ = EquipmentAddress.Index(address, EquipmentGroups.PartState);
            return;
        }
        throw new InvalidOperationException($"系统反馈地址不是受支持的标量或Part_State元素：{address}");
    }

    private static void ValidateUnique(IEnumerable<string> addresses, IEnumerable<int> ids)
    {
        var array = addresses.ToArray();
        if (array.Length == 0 || array.Distinct(StringComparer.Ordinal).Count() != array.Length
            || ids.Distinct().Count() != array.Length)
            throw new InvalidOperationException("定义表为空或包含重复编号/地址");
    }
}
