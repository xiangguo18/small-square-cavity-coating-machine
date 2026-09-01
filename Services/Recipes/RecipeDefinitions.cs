using Microsoft.Data.Sqlite;
using Small_square_cavity_coating_machine.Models.Recipes;
using Small_square_cavity_coating_machine.Models.Equipment;
using Small_square_cavity_coating_machine.Services.Alarms;
using Small_square_cavity_coating_machine.Services.Equipment;
using System.Globalization;
using System.IO;

namespace Small_square_cavity_coating_machine.Services.Recipes;

public sealed record RecipeDefinition(int Index, string Name, string Unit, double DefaultValue, double Minimum, double Maximum);
public interface IRecipeDefinitionRepository { IReadOnlyList<RecipeDefinition> Load(); }

public sealed class SqliteRecipeDefinitionRepository(string path) : IRecipeDefinitionRepository
{
    public IReadOnlyList<RecipeDefinition> Load()
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        db.Open();
        using var query = db.CreateCommand();
        query.CommandText = "SELECT Name,Unit,DefaultValue,MinValue,MaxValue,RecipeType FROM MultiRecipeDef ORDER BY rowid";
        using var rows = query.ExecuteReader();
        var result = new List<RecipeDefinition>();
        while (rows.Read())
        {
            var index = result.Count;
            if (index >= 18 || rows.GetString(0) != RecipeDefinitions.Names[index]
                || !string.Equals(rows.GetString(1), RecipeDefinitions.Units[index], StringComparison.OrdinalIgnoreCase)
                || rows.GetString(5) != "A")
                throw new InvalidOperationException("MultiRecipeDef字段顺序、名称、单位或配方类型与已确认18项映射不一致");
            var d = new RecipeDefinition(index, rows.GetString(0), rows.GetString(1), rows.GetDouble(2), rows.GetDouble(3), rows.GetDouble(4));
            if (!double.IsFinite(d.Minimum) || !double.IsFinite(d.Maximum) || !double.IsFinite(d.DefaultValue)
                || d.Minimum > d.Maximum || d.DefaultValue < d.Minimum || d.DefaultValue > d.Maximum)
                throw new InvalidOperationException("MultiRecipeDef范围或默认值无效");
            result.Add(d);
        }
        if (result.Count != 18) throw new InvalidOperationException("MultiRecipeDef必须包含18项");
        return result;
    }
}

public static class RecipeDefinitions
{
    // Explicit user-approved mapping; rowid is only checked ordering, never a general PLC address rule.
    public static readonly string[] Names = ["直流电源设定功率","中频电源设定功率","功率跨度","间隔时间","预溅射时间","样品台转速","镀膜时间",
        "启辉Ar","工作Ar","启辉N2","工作N2","启辉O2","工作O2","气流稳定时间","启辉气压","工作气压","启辉APC","工作APC"];
    public static readonly string[] Units = ["W","W","W","S","S","RPM","S","SCCM","SCCM","SCCM","SCCM","SCCM","SCCM","S","Pa","Pa","%","%"];
    private static readonly Lazy<IReadOnlyList<RecipeDefinition>> Embedded = new(() =>
        new SqliteRecipeDefinitionRepository(SqliteAlarmDefinitionRepository.ExtractEmbeddedDatabase(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SmallSquareCavityCoatingMachine", "definitions"))).Load());
    public static IReadOnlyList<RecipeDefinition> Default => Embedded.Value;
    public static double[] Values(RecipeLayer p) => [
        p.CathodeAPower,p.CathodeBPower,p.PowerSpan,p.IntervalSeconds,p.PreSputterSeconds,p.StageSpeedRpm,p.CoatingSeconds,
        p.IgnitionArgonSccm,p.WorkingArgonSccm,p.IgnitionNitrogenSccm,p.WorkingNitrogenSccm,p.IgnitionOxygenSccm,p.WorkingOxygenSccm,
        p.GasStabilizationSeconds,
        p.PressureControlMode == RecipePressureControlMode.ApcPosition ? 0 : p.IgnitionPressurePa,
        p.PressureControlMode == RecipePressureControlMode.ApcPosition ? 0 : p.WorkingPressurePa,
        p.PressureControlMode == RecipePressureControlMode.Pressure ? 0 : p.IgnitionApcPercent,
        p.PressureControlMode == RecipePressureControlMode.Pressure ? 0 : p.WorkingApcPercent];
    public static void Validate(double value, RecipeDefinition d)
    {
        if (!double.IsFinite(value) || value < d.Minimum || value > d.Maximum)
            throw new InvalidOperationException($"{d.Name}允许范围：{d.Minimum}～{d.Maximum} {d.Unit}");
    }
    public static Array ConvertLayer(RecipeLayer layer, Type type, IReadOnlyList<RecipeDefinition> definitions)
    {
        if (layer.Sequence <= 0 || definitions.Count != 18) throw new InvalidOperationException("配方序号或定义无效");
        var values = Values(layer);
        var result = Array.CreateInstance(type, 18);
        for (var i = 0; i < 18; i++)
        {
            var d = definitions[i];
            Validate(values[i], d);
            var parameter = new ParameterDefinition(i, d.Name, d.Unit, $"EQ_Recipe1[{i}]", 0, "", "", d.Name, d.DefaultValue, d.Minimum, d.Maximum);
            result.SetValue(ParameterValueCodec.Parse(values[i].ToString("R", CultureInfo.InvariantCulture), type, parameter), i);
        }
        return result;
    }
}
