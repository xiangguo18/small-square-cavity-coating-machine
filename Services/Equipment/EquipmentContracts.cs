using Opc.Ua;
using Small_square_cavity_coating_machine.Models.Equipment;
using Small_square_cavity_coating_machine.Services.Alarms;
using Small_square_cavity_coating_machine.Services.History;
using Small_square_cavity_coating_machine.Models.History;

namespace Small_square_cavity_coating_machine.Services.Equipment;

public sealed record EquipmentBinding(string Group, bool Available, bool CanWrite, Type? ElementType, string Error);

public interface IEquipmentSession : IAsyncDisposable
{
    Task<IReadOnlyDictionary<string, EquipmentBinding>> DiscoverAsync(CancellationToken token);
    Task<DataValue> ReadGroupAsync(string group, int? index, CancellationToken token);
    Task<IReadOnlyDictionary<string, string>> SubscribeGroupsAsync(IReadOnlyList<string> groups,
        Action<string, DataValue> onValue, CancellationToken token);
    Task WriteParameterElementAsync(int index, object value, CancellationToken token);
    Task WriteElementAsync(string group, int? index, object value, CancellationToken token) =>
        group == EquipmentGroups.Parameter && index.HasValue
            ? WriteParameterElementAsync(index.Value, value, token)
            : throw new NotSupportedException($"此会话未实现{group}写入");
    Task WriteArrayElementsAsync(string group, IReadOnlyList<ArrayWriteMutation> mutations, CancellationToken token);
    Task WriteRecipeValueAsync(string group, object value, CancellationToken token) => throw new NotSupportedException("此会话未实现配方写入");
    Task WaitForDisconnectAsync(CancellationToken token);
}

public interface IEquipmentSessionFactory
{
    Task<IEquipmentSession> ConnectAsync(OpcUaAlarmOptions options, CancellationToken token);
}

public interface IOpcUaEquipmentClient : IAlarmConnectionController, IAsyncDisposable
{
    bool IsSimulated { get; }
    bool RecipeAvailable => false;
    string RecipeNodeDiagnostics => "";
    Task<IRecipeEquipmentLease> AcquireRecipeRunAsync(CancellationToken token) => throw new NotSupportedException("尚未实现配方会话");
    event EventHandler? SnapshotChanged;
    EquipmentSnapshot Snapshot();
    Task StartAsync(CancellationToken token);
    Task<ParameterWriteResult> WriteParameterAsync(ParameterWriteRequest request, CancellationToken token = default);
    Task<ControlWriteResult> WriteControlAsync(ControlWriteRequest request, CancellationToken token = default) =>
        Task.FromResult(new ControlWriteResult(ControlWriteOutcome.Rejected, "尚未实现设备控制写入"));
}

public interface IEquipmentRuntimeRepository : IOperationLogRepository
{
    string StorageError { get; }
    IReadOnlyList<ParameterCache> LoadCache(string endpoint);
    void BeginWrite(OperationLogRecord record);
    void FinishWrite(OperationLogRecord record, ParameterCache? cache);
}

public sealed class ParameterWriteRejectedException(string message) : InvalidOperationException(message);

public interface IRecipeEquipmentLease : IAsyncDisposable
{
    string Endpoint { get; }
    string User { get; }
    string Failure { get; }
    Type ArrayType { get; }
    Task TransactionAsync(Func<CancellationToken, Task> action, CancellationToken token);
    object FlagValue(string group, bool value);
    Task WriteAsync(string group, object value, CancellationToken token);
    Task ConfirmAsync(string group, object expected, CancellationToken token);
    Task StartLayerAsync(CancellationToken token);
    Task WaitLayerAsync(CancellationToken token);
}
