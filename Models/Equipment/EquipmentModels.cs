using Small_square_cavity_coating_machine.Models.Alarms;
using Small_square_cavity_coating_machine.Models.Security;

namespace Small_square_cavity_coating_machine.Models.Equipment;

public static class EquipmentGroups
{
    public const string Alarm = "EQ_Alarm";
    public const string Io = "EQ_IO";
    public const string Parameter = "EQ_Parameter1";
    public const string Process = "EQ_Process";
    public const string PartCommand = "Part_Command";
    public const string PartCommandEnable = "Part_Command_En";
    public const string PartState = "Part_State";
    public const string PartData = "Part_Data1";
    public const string PartDataSet1 = "Part_Data_Set1";
    public const string PartDataSet2 = "Part_Data_Set2";
    public const string Interlock = "EQ_Interlock";
    public const string PassInterlock = "EQ_PassInterlock";
    public const string Recipe = "EQ_Recipe1", RecipeOk = "EQ_RecipeOK", CoatOk = "EQ_CoatOK";
    public static readonly string[] SystemControlPoints =
    [
        "EQ_Manual", "EQ_Manual_En", "fbButtonManual_Output",
        "EQ_Auto", "EQ_Auto_En", "fbButtonAuto_Output",
        "EQ_Semi", "EQ_Semi_En", "fbButtonSemi_Output",
        "EQ_Start", "EQ_Start_En", "fbButtonStart_Output",
        "EQ_Stop", "EQ_Stop_En", "fbButtonStop_Output",
        "EQ_Reset", "EQ_Reset_En", "fbButtonReset_Output",
        PassInterlock
    ];
    public static readonly string[] ControlArrays =
    [
        PartCommand, PartCommandEnable, PartState, PartData,
        PartDataSet1, PartDataSet2, Interlock
    ];
    public static readonly string[] All = [Alarm, Io, Parameter];
    public static readonly string[] RecipePoints = [Recipe, RecipeOk, CoatOk];
    public static readonly string[] Monitored = [.. All, Process, .. ControlArrays, .. SystemControlPoints, .. RecipePoints];
    public static bool IsScalar(string group) => group == RecipeOk || group == CoatOk || SystemControlPoints.Contains(group, StringComparer.Ordinal);
    public static bool IsFlag(string group) => IsScalar(group);
    public static bool IsWritable(string group) => group == Parameter || group == Recipe
        || group == RecipeOk || group == CoatOk
        || group == PartCommand || group == PartDataSet1 || group == PartDataSet2
        || group == PassInterlock || SystemControlPoints.Contains(group, StringComparer.Ordinal)
            && !group.EndsWith("_En", StringComparison.Ordinal)
            && !group.StartsWith("fbButton", StringComparison.Ordinal);
}

public sealed record IoDefinition(int Id, string Name, string Type, string Location, string Module,
    string Address, string Des, string Chinese, string English)
{
    public string Description => !string.IsNullOrWhiteSpace(Chinese) ? Chinese
        : !string.IsNullOrWhiteSpace(Des) ? Des : !string.IsNullOrWhiteSpace(English) ? English : Name;
}

public sealed record ParameterDefinition(int Id, string Name, string Unit, string Address, double Value,
    string Location, string Type, string Chinese, double DefaultValue, double MinValue, double MaxValue)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Chinese) ? Name : Chinese;
}

public sealed record EquipmentPoint(string Address, object? Value, AlarmQuality Quality, DateTimeOffset ObservedAt,
    DateTimeOffset? SourceTimestamp, Type? ValueType, bool CanWrite, long Epoch);

public sealed record EquipmentGroupState(string Name, bool IsReady, string Message, IReadOnlyDictionary<string, EquipmentPoint> Points);
public sealed record EquipmentSnapshot(string Endpoint, long Epoch, bool IsConnected, bool IsWriting,
    bool IsSimulated, IReadOnlyDictionary<string, EquipmentGroupState> Groups)
{
    public string? WriteAddress { get; init; }
    public string WriteStage { get; init; } = "";
}
public sealed record ParameterWriteRequest(string Address, string Draft, object ExpectedValue, long Epoch);
public enum ParameterWriteOutcome { Confirmed, Rejected, Unknown, Unchanged }
public sealed record ParameterWriteResult(ParameterWriteOutcome Outcome, string Message, object? ConfirmedValue = null);
public sealed record ParameterCache(string Endpoint, string Address, string Value, DateTimeOffset ConfirmedAt, bool IsSimulated);

public sealed record PartDefinition(int Id, string Location, string Name, string StateAddress, string Interlock);

public sealed record PartCommandDefinition(int Id, int PartId, string Command, string DisplayName,
    string Address, string EnableAddress, string Info, string InterlockAddress);

public sealed record PartDataDefinition(int Id, int PartId, string Name, string DisplayName, string Access,
    string Address, string SetAddress, double Minimum, double Maximum, string Unit);

public sealed record SystemCommandDefinition(string Name, string DisplayName, string CommandAddress,
    string EnableAddress, string FeedbackAddress, bool RequiresBuiltInAdministrator);

public sealed record InterlockDefinition(int Id, string DeviceKey, string Action, string DisplayName, string Address);

public sealed record EquipmentControlDefinitions(
    IReadOnlyDictionary<int, PartDefinition> Parts,
    IReadOnlyDictionary<int, PartCommandDefinition> Commands,
    IReadOnlyDictionary<int, PartDataDefinition> Data,
    IReadOnlyDictionary<string, SystemCommandDefinition> SystemCommands,
    IReadOnlyDictionary<int, InterlockDefinition> Interlocks,
    IReadOnlyList<string> Addresses)
{
    public static EquipmentControlDefinitions Empty { get; } = new(
        new Dictionary<int, PartDefinition>(), new Dictionary<int, PartCommandDefinition>(),
        new Dictionary<int, PartDataDefinition>(), new Dictionary<string, SystemCommandDefinition>(),
        new Dictionary<int, InterlockDefinition>(), []);
}

public sealed record ControlWriteRequest(string Address, object Value, long Epoch, string Target, string Action,
    PermissionKey Permission, bool RequiresBuiltInAdministrator = false);

public enum ControlWriteOutcome { Confirmed, Rejected, Unknown }

public sealed record ControlWriteResult(ControlWriteOutcome Outcome, string Message, object? ConfirmedValue = null);
