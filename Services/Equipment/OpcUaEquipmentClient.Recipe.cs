using Opc.Ua;
using Small_square_cavity_coating_machine.Models.Alarms;
using Small_square_cavity_coating_machine.Models.Equipment;
using Small_square_cavity_coating_machine.Models.Security;
using Small_square_cavity_coating_machine.Services.Alarms;

namespace Small_square_cavity_coating_machine.Services.Equipment;

public sealed partial class OpcUaEquipmentClient
{
    private RecipeLease? _recipeLease;
    private bool _recipePending;
    public bool RecipeAvailable
    {
        get { lock (_gate) return _recipeLease is null && RecipeReady() && RecipeLoadReady(); }
    }
    /// <summary>返回 EQ_Recipe1 的实际元素类型、数组长度与配方覆盖范围，用于诊断写入 BadTypeMismatch。</summary>
    public string RecipeNodeDiagnostics
    {
        get
        {
            lock (_gate)
            {
                if (!_groups.TryGetValue(EquipmentGroups.Recipe, out var recipe) || !recipe.IsReady)
                    return "配方节点未就绪：EQ_Recipe1";
                var type = recipe.Points.Values.Select(p => p.ValueType).Where(t => t is not null).Distinct().FirstOrDefault();
                var count = _addresses.TryGetValue(EquipmentGroups.Recipe, out var addrs) ? addrs.Length : 0;
                var actualLength = _recipeLease?.RecipeArrayLength;
                return $"EQ_Recipe1 元素类型={TypeName(type)}，PLC实际数组长度={actualLength?.ToString() ?? "待写入前复核"}，"
                    + $"配方覆盖项数={count}，最终发送载荷长度={actualLength?.ToString() ?? "待构造"}";
            }
        }
    }
    private static string TypeName(Type? type) => type == typeof(float) ? "FLOAT(REAL)"
        : type == typeof(double) ? "DOUBLE(LREAL)"
        : type == typeof(short) ? "INT16"
        : type == typeof(ushort) ? "UINT16"
        : type == typeof(int) ? "INT32"
        : type == typeof(uint) ? "UINT32"
        : type == typeof(long) ? "INT64"
        : type == typeof(ulong) ? "UINT64"
        : type == typeof(sbyte) ? "SINT8"
        : type == typeof(byte) ? "UINT8"
        : type?.Name ?? "未知";
    private bool RecipeReady() => _phase == AlarmConnectionPhase.Connected && _session is not null
        && EquipmentGroups.RecipePoints.All(g => _groups.TryGetValue(g, out var state) && state.IsReady
            && state.Points.Values.All(p => p.CanWrite));

    private bool RecipeLoadReady() => _groups.TryGetValue(EquipmentGroups.RecipeLoad, out var state)
        && state.IsReady && state.Points.TryGetValue(EquipmentGroups.RecipeLoad, out var point) && point.CanWrite;

    private void ApplyRecipeFlag(string group, DataValue data, EquipmentBinding binding)
    {
        var value = StatusCode.IsGood(data.StatusCode) && !data.StatusCode.Overflow && data.Value?.GetType() == binding.ElementType
            ? AlarmArrayDecoder.DecodeElement(data.Value) : null;
        var good = value is not null;
        DateTimeOffset? source = data.SourceTimestamp == DateTime.MinValue ? null
            : new(DateTime.SpecifyKind(data.SourceTimestamp, DateTimeKind.Utc));
        var previous = _groups[group].Points.GetValueOrDefault(group);
        var point = new EquipmentPoint(group, good ? value : previous?.Value, good ? AlarmQuality.Good : AlarmQuality.Bad,
            DateTimeOffset.Now, source, binding.ElementType, good && binding.CanWrite, _epoch);
        _groups[group] = new(group, good, good ? binding.CanWrite ? "有效，可写" : "有效，只读" : "标志质量或类型无效",
            new Dictionary<string, EquipmentPoint> { [group] = point });
        if (group == EquipmentGroups.RecipeOk && good) _recipeLease?.Observe((bool)value!, data.SourceTimestamp);
        Notify();
    }

    private void CheckRecipeHealth()
    {
        if (_recipeLease is { } lease && (!RecipeReady() || lease.Epoch != _epoch))
            lease.Abort("配方通讯或质量异常；停止后续下发，设备状态需人工核对");
    }

    public async Task<IRecipeEquipmentLease> AcquireRecipeRunAsync(CancellationToken token)
    {
        lock (_gate)
        {
            if (_recipeLease is not null || _recipePending) throw new InvalidOperationException("已有配方任务");
            _recipePending = true; Notify();
        }
        var acquired = false;
        RecipeLease? lease = null;
        try
        {
            acquired = await _lifecycle.WaitAsync(TimeSpan.FromSeconds(15), token).ConfigureAwait(false);
            if (!acquired) throw new TimeoutException("等待已有参数事务超过15秒；本次未启动");
            lock (_gate)
            {
                if (_disposed || !RecipeReady() || !RecipeLoadReady() || _sessionLifetime is null)
                    throw new InvalidOperationException("配方点或配方加载触发点未就绪、不可写或质量无效");
                if (!_authorization.CanOperate(PermissionKey.ProcessRecipe)) throw new InvalidOperationException("没有工艺配方权限");
                lease = new(this, _session!, _epoch, _options.EndpointUrl, _authorization.CurrentUserName, _sessionLifetime.Token, token);
                _recipeLease = lease;
                _authorization.AccessChanged += RecipeAccessChanged;
            }
            try { await lease.InspectRecipeArrayAsync(token).ConfigureAwait(false); }
            catch { await lease.DisposeAsync().ConfigureAwait(false); throw; }
            return lease;
        }
        finally
        {
            lock (_gate) { _recipePending = false; Notify(); }
            if (acquired) _lifecycle.Release();
        }
    }

    private void RecipeAccessChanged(object? sender, EventArgs e)
    {
        lock (_gate)
            if (_recipeLease is { } lease && (!_authorization.CanOperate(PermissionKey.ProcessRecipe)
                || lease.User != _authorization.CurrentUserName))
                lease.Abort("配方账号或权限变化；停止后续下发，不代表设备停机");
    }

    /// <summary>One run owns endpoint/epoch; only short write transactions own the parameter mutex.</summary>
    public sealed class RecipeLease : IRecipeEquipmentLease
    {
        private readonly OpcUaEquipmentClient _owner;
        private readonly IEquipmentSession _session;
        private readonly CancellationTokenSource _stop;
        private readonly Lock _signals = new();
        private TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _requested, _zeroSeen, _oneSeen, _writeSucceeded, _disposed;
        private DateTime _baseline;
        public long Epoch { get; }
        public string Endpoint { get; }
        public string User { get; }
        public string Failure { get; private set; } = "";
        public CancellationToken Token => _stop.Token;
        public Type ArrayType { get { lock (_owner._gate) return _owner._groups[EquipmentGroups.Recipe].Points.Values.First().ValueType!; } }
        internal int RecipeArrayLength { get; private set; }
        internal RecipeLease(OpcUaEquipmentClient owner, IEquipmentSession session, long epoch, string endpoint,
            string user, CancellationToken connection, CancellationToken caller)
        {
            _owner = owner; _session = session; Epoch = epoch; Endpoint = endpoint; User = user;
            _stop = CancellationTokenSource.CreateLinkedTokenSource(connection, caller);
        }
        internal void Abort(string reason) { Failure = reason; _stop.Cancel(); }
        internal async Task InspectRecipeArrayAsync(CancellationToken caller)
        {
            Ensure();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token, caller);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var data = await _session.ReadGroupAsync(EquipmentGroups.Recipe, null, timeout.Token).ConfigureAwait(false);
            Ensure();
            if (!StatusCode.IsGood(data.StatusCode) || data.StatusCode.Overflow)
                throw new InvalidOperationException($"EQ_Recipe1读取质量无效：{data.StatusCode}");
            if (data.Value is not Array { Rank: 1 } array || array.GetLowerBound(0) != 0)
                throw new InvalidOperationException($"EQ_Recipe1当前值不是下界为0的一维数组：{data.Value?.GetType().FullName ?? "null"}");
            if (array.GetType().GetElementType() != ArrayType)
                throw new InvalidOperationException($"EQ_Recipe1元数据类型={TypeName(ArrayType)}，当前值类型={array.GetType().FullName}，类型不一致");
            if (array.Length < 18)
                throw new InvalidOperationException($"EQ_Recipe1实际数组长度={array.Length}，不足配方所需18项");
            RecipeArrayLength = array.Length;
        }
        private void Ensure()
        {
            Token.ThrowIfCancellationRequested();
            lock (_owner._gate)
                if (_disposed || _owner._recipeLease != this || _owner._session != _session || Epoch != _owner._epoch
                    || !_owner.RecipeReady() || !_owner._authorization.CanOperate(PermissionKey.ProcessRecipe)
                    || User != _owner._authorization.CurrentUserName)
                    throw new InvalidOperationException("配方会话、权限或点位发生变化，不继续写入");
        }
        public async Task TransactionAsync(Func<CancellationToken, Task> action, CancellationToken caller)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(Token, caller);
            lock (_owner._gate) { Ensure(); _owner._recipePending = true; _owner.Notify(); }
            var acquired = false;
            try
            {
                acquired = await _owner._lifecycle.WaitAsync(TimeSpan.FromSeconds(15), linked.Token).ConfigureAwait(false);
                if (!acquired) throw new TimeoutException("等待参数写入事务超过15秒");
                lock (_owner._gate)
                {
                    Ensure(); _owner._writing = true; _owner._writeAddress = EquipmentGroups.Recipe;
                    _owner._writeStage = "配方写入及回读确认中";
                    _owner._writeDone = new(TaskCreationOptions.RunContinuationsAsynchronously); _owner.Notify();
                }
                await action(linked.Token).ConfigureAwait(false);
            }
            finally
            {
                lock (_owner._gate)
                {
                    _owner._recipePending = false;
                    if (acquired) { _owner._writing = false; _owner._writeAddress = null; _owner._writeStage = "";
                        _owner._writeDone?.TrySetResult(); _owner._writeDone = null; }
                    _owner.Notify();
                }
                if (acquired) _owner._lifecycle.Release();
            }
        }
        public object FlagValue(string group, bool value)
        {
            lock (_owner._gate)
            {
                var type = _owner._groups[group].Points[group].ValueType!;
                return type == typeof(bool) ? value : System.Convert.ChangeType(value ? 1 : 0, type);
            }
        }
        public async Task<DataValue> ReadAsync(string group, CancellationToken token)
        {
            Ensure();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token, token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var data = await _session.ReadGroupAsync(group, null, timeout.Token).ConfigureAwait(false);
            Ensure();
            if (EquipmentGroups.IsFlag(group) && AlarmArrayDecoder.DecodeElement(data.Value) is null)
                throw new InvalidOperationException($"{group}回读标志不是Boolean或严格0/1整数");
            if (!StatusCode.IsGood(data.StatusCode) || data.StatusCode.Overflow)
                throw new InvalidOperationException($"{group}回读质量无效");
            return data;
        }
        public async Task WriteAsync(string group, object value, CancellationToken token)
        {
            Ensure();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token, token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await _session.WriteRecipeValueAsync(group, value, timeout.Token).ConfigureAwait(false);
            Ensure();
        }
        public async Task ConfirmAsync(string group, object expected, CancellationToken token)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token, token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            while (true)
            {
                var value = (await ReadAsync(group, deadline.Token).ConfigureAwait(false)).Value;
                if (expected is Array wanted)
                {
                    if (value is Array { Rank: 1 } actual && actual.GetLowerBound(0) == 0 && actual.Length >= 18
                        && Enumerable.Range(0, 18).All(i => ParameterValueCodec.Same(wanted.GetValue(i), actual.GetValue(i)))) return;
                }
                else if (ParameterValueCodec.Same(expected, value)) return;
                await Task.Delay(50, deadline.Token).ConfigureAwait(false);
            }
        }
        public async Task StartLayerAsync(CancellationToken token)
        {
            // A new waiter is prepared before the actual reset. Existing 1 is never a completion.
            var before = await ReadAsync(EquipmentGroups.RecipeOk, token).ConfigureAwait(false);
            lock (_signals)
            {
                _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _requested = true; _zeroSeen = _oneSeen = _writeSucceeded = false; _baseline = before.SourceTimestamp;
            }
            await WriteAsync(EquipmentGroups.RecipeOk, FlagValue(EquipmentGroups.RecipeOk, false), token).ConfigureAwait(false);
            lock (_signals) { _writeSucceeded = true; CompleteIfReady(); }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token, token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            while (true)
            {
                lock (_signals) if (_zeroSeen) return; // A post-reset good subscription 0 also confirms reset.
                var data = await ReadAsync(EquipmentGroups.RecipeOk, timeout.Token).ConfigureAwait(false);
                var bit = AlarmArrayDecoder.DecodeElement(data.Value);
                if (bit == false) { lock (_signals) { _zeroSeen = true; CompleteIfReady(); } return; }
                // Seeing 1 without this reset's 0 is never sufficient evidence.
                await Task.Delay(30, timeout.Token).ConfigureAwait(false);
            }
        }
        internal void Observe(bool bit, DateTime timestamp)
        {
            lock (_signals)
            {
                if (!_requested || timestamp == DateTime.MinValue || timestamp <= _baseline) return;
                if (!bit) _zeroSeen = true;
                else if (_zeroSeen) _oneSeen = true;
                CompleteIfReady();
            }
        }
        private void CompleteIfReady() { if (_writeSucceeded && _zeroSeen && _oneSeen) _completed.TrySetResult(); }
        public async Task WaitLayerAsync(CancellationToken caller)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(Token, caller);
            // Subscription carries fast transitions; periodic read also covers unchanged/no-timestamp servers.
            while (!_completed.Task.IsCompleted)
            {
                await Task.WhenAny(_completed.Task, Task.Delay(100, linked.Token)).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
                if (_completed.Task.IsCompleted) break;
                var bit = AlarmArrayDecoder.DecodeElement((await ReadAsync(EquipmentGroups.RecipeOk, linked.Token).ConfigureAwait(false)).Value);
                if (bit is null) throw new InvalidOperationException("RecipeOK标志类型异常");
                lock (_signals) { if (bit == true && _zeroSeen && _writeSucceeded) _completed.TrySetResult(); }
            }
            Ensure();
            lock (_signals) _requested = false;
        }
        public ValueTask DisposeAsync()
        {
            lock (_owner._gate)
            {
                if (_disposed) return ValueTask.CompletedTask;
                _disposed = true; _stop.Cancel();
                _owner._authorization.AccessChanged -= _owner.RecipeAccessChanged;
                if (_owner._recipeLease == this) _owner._recipeLease = null;
                _owner.Notify();
            }
            _stop.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
