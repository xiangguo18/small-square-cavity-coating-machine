using Small_square_cavity_coating_machine.Models.Alarms;
using Small_square_cavity_coating_machine.Models.Equipment;
using Small_square_cavity_coating_machine.Models.Security;

namespace Small_square_cavity_coating_machine.Services.Equipment;

public interface IEquipmentControlService
{
    EquipmentControlDefinitions Definitions { get; }
    event EventHandler? Changed;
    EquipmentSnapshot Snapshot();
    EquipmentPoint? Point(string address);
    Task<ControlWriteResult> ExecutePartCommandAsync(int id, string target, string action, CancellationToken token = default);
    Task<ControlWriteResult> ExecutePartCommandBatchAsync(int assertId, IReadOnlyList<int> deassertIds,
        string target, string action, CancellationToken token = default);
    Task<ControlWriteResult> ExecuteSystemCommandAsync(string name, CancellationToken token = default);
    Task<ControlWriteResult> WriteSystemCommandAsync(string name, bool value, string target, string action,
        CancellationToken token = default);
    Task<ControlWriteResult> WriteSetpointAsync(int partDataId, double value, PermissionKey permission,
        CancellationToken token = default);
    Task<ControlWriteResult> SetPassInterlockAsync(bool active, CancellationToken token = default);
}

public sealed class EquipmentControlService : IEquipmentControlService, IDisposable
{
    private readonly IOpcUaEquipmentClient _client;

    public EquipmentControlService(IOpcUaEquipmentClient client, EquipmentControlDefinitions definitions)
    {
        _client = client;
        Definitions = definitions;
        _client.SnapshotChanged += ClientSnapshotChanged;
    }

    public EquipmentControlDefinitions Definitions { get; }
    public event EventHandler? Changed;

    public EquipmentSnapshot Snapshot() => _client.Snapshot();

    public EquipmentPoint? Point(string address)
    {
        var snapshot = _client.Snapshot();
        var group = EquipmentAddress.Group(address);
        return snapshot.Groups.GetValueOrDefault(group)?.Points.GetValueOrDefault(address);
    }

    public async Task<ControlWriteResult> ExecutePartCommandAsync(int id, string target, string action,
        CancellationToken token = default)
    {
        if (!Definitions.Commands.TryGetValue(id, out var command))
            return new(ControlWriteOutcome.Rejected, $"数据库未定义Part_Command[{id}]");
        var snapshot = _client.Snapshot();
        if (!string.IsNullOrWhiteSpace(command.InterlockAddress)
            && !GoodBoolean(snapshot, EquipmentGroups.PassInterlock)
            && !GoodBoolean(snapshot, command.InterlockAddress))
            return new(ControlWriteOutcome.Rejected, $"{target}{action}互锁条件未满足");
        var (confirm, stateAddress, expectOpen) = ConfirmationFor(command);
        return await _client.WriteControlAsync(new(command.Address, true, snapshot.Epoch, target, action,
            PermissionKey.SystemStatus, Confirm: confirm, StateAddress: stateAddress, ExpectOpen: expectOpen),
            token).ConfigureAwait(false);
    }

    public async Task<ControlWriteResult> ExecuteSystemCommandAsync(string name, CancellationToken token = default)
    {
        if (!Definitions.SystemCommands.TryGetValue(name, out var command))
            return new(ControlWriteOutcome.Rejected, $"数据库未定义系统命令{name}");
        var snapshot = _client.Snapshot();
        return await _client.WriteControlAsync(new(command.CommandAddress, true, snapshot.Epoch,
            "系统控制", command.DisplayName, PermissionKey.SystemStatus, command.RequiresBuiltInAdministrator,
            Confirm: ControlConfirm.Sent), token).ConfigureAwait(false);
    }

    /// <summary>
    /// 对 Part_Command 数组做一次“读取整组 → 修改多个元素 → 整组写回”：断言位置 1、互斥位写 0。
    /// 互锁/管理员校验失败时整组不写（含互斥位）。
    /// </summary>
    public async Task<ControlWriteResult> ExecutePartCommandBatchAsync(int assertId, IReadOnlyList<int> deassertIds,
        string target, string action, CancellationToken token = default)
    {
        if (!Definitions.Commands.TryGetValue(assertId, out var command))
            return new(ControlWriteOutcome.Rejected, $"数据库未定义Part_Command[{assertId}]");
        var snapshot = _client.Snapshot();
        if (!string.IsNullOrWhiteSpace(command.InterlockAddress)
            && !GoodBoolean(snapshot, EquipmentGroups.PassInterlock)
            && !GoodBoolean(snapshot, command.InterlockAddress))
            return new(ControlWriteOutcome.Rejected, $"{target}{action}互锁条件未满足");
        var (confirm, stateAddress, expectOpen) = ConfirmationFor(command);
        var deasserts = new List<ArrayWriteMutation>(deassertIds?.Count ?? 0);
        if (deassertIds is not null)
            foreach (var id in deassertIds)
            {
                if (!Definitions.Commands.TryGetValue(id, out var deassert))
                    return new(ControlWriteOutcome.Rejected, $"数据库未定义Part_Command[{id}]");
                deasserts.Add(new(EquipmentAddress.Index(deassert.Address, EquipmentGroups.PartCommand), false));
            }
        return await _client.WriteControlAsync(new(command.Address, true, snapshot.Epoch, target, action,
            PermissionKey.SystemStatus, Confirm: confirm, StateAddress: stateAddress, ExpectOpen: expectOpen,
            ArrayMutations: deasserts), token).ConfigureAwait(false);
    }

    /// <summary>
    /// 向某个系统命令地址写入布尔值（互斥时用于把同组其余命令置 0）。
    /// </summary>
    public async Task<ControlWriteResult> WriteSystemCommandAsync(string name, bool value, string target, string action,
        CancellationToken token = default)
    {
        if (!Definitions.SystemCommands.TryGetValue(name, out var command))
            return new(ControlWriteOutcome.Rejected, $"数据库未定义系统命令{name}");
        var snapshot = _client.Snapshot();
        return await _client.WriteControlAsync(new(command.CommandAddress, value, snapshot.Epoch, target, action,
            PermissionKey.SystemStatus, command.RequiresBuiltInAdministrator, Confirm: ControlConfirm.Sent),
            token).ConfigureAwait(false);
    }

    public async Task<ControlWriteResult> WriteSetpointAsync(int partDataId, double value, PermissionKey permission,
        CancellationToken token = default)
    {
        if (!Definitions.Data.TryGetValue(partDataId, out var data) || string.IsNullOrWhiteSpace(data.SetAddress))
            return new(ControlWriteOutcome.Rejected, $"数据库未定义PartData[{partDataId}]设定地址");
        if (!double.IsFinite(value) || value < data.Minimum || value > data.Maximum)
            return new(ControlWriteOutcome.Rejected, $"允许范围：{data.Minimum}～{data.Maximum} {data.Unit}");
        var snapshot = _client.Snapshot();
        return await _client.WriteControlAsync(new(data.SetAddress, value, snapshot.Epoch, data.DisplayName,
            "设定值写入", permission), token).ConfigureAwait(false);
    }

    public async Task<ControlWriteResult> SetPassInterlockAsync(bool active, CancellationToken token = default)
    {
        if (!Definitions.SystemCommands.TryGetValue("PassInterlock", out var command))
            return new(ControlWriteOutcome.Rejected, "数据库未定义EQ_PassInterlock");
        var snapshot = _client.Snapshot();
        return await _client.WriteControlAsync(new(command.CommandAddress, active, snapshot.Epoch,
            "维修互锁", active ? "解除全部互锁" : "恢复全部互锁", PermissionKey.UserManagement, true), token)
            .ConfigureAwait(false);
    }

    private static bool GoodBoolean(EquipmentSnapshot snapshot, string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return false;
        var group = EquipmentAddress.Group(address);
        return snapshot.Groups.GetValueOrDefault(group)?.Points.GetValueOrDefault(address) is
        { Quality: AlarmQuality.Good, Value: true };
    }

    private (ControlConfirm Confirm, string? StateAddress, bool ExpectOpen) ConfirmationFor(
        PartCommandDefinition command)
    {
        var stateAddress = Definitions.Parts.GetValueOrDefault(command.PartId)?.StateAddress;
        var hasState = !string.IsNullOrWhiteSpace(stateAddress)
            && command.Command is "Open" or "Close" or "Start" or "Stop";
        return hasState
            ? (ControlConfirm.State, stateAddress, command.Command is "Open" or "Start")
            : (ControlConfirm.Sent, null, false);
    }

    private void ClientSnapshotChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose() => _client.SnapshotChanged -= ClientSnapshotChanged;
}
