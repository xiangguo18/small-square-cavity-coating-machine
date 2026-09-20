using Opc.Ua;
using Small_square_cavity_coating_machine.Models.Alarms;
using Small_square_cavity_coating_machine.Models.Equipment;
using Small_square_cavity_coating_machine.Models.History;
using Small_square_cavity_coating_machine.Models.Security;
using Small_square_cavity_coating_machine.Services.Alarms;
using Small_square_cavity_coating_machine.Services.Security;

namespace Small_square_cavity_coating_machine.Services.Equipment;

public sealed partial class OpcUaEquipmentClient : IOpcUaEquipmentClient
{
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly IEquipmentSessionFactory _factory;
    private readonly IAlarmConnectionSettingsStore _settings;
    private readonly IAuthorizationService _authorization;
    private readonly IEquipmentRuntimeRepository _runtime;
    private readonly IReadOnlyDictionary<string, string> _definitionErrors;
    private readonly Dictionary<string, ParameterDefinition> _parameters;
    private readonly Dictionary<string, string[]> _addresses;
    private readonly Dictionary<string, string> _addressGroups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EquipmentGroupState> _groups = [];
    private OpcUaAlarmOptions _options;
    private AlarmConnectionPhase _phase;
    private string _message = "PLC未连接";
    private string? _writeAddress;
    private string _writeStage = "";
    private string? _loadError;
    private long _epoch;
    private bool _started, _disposed, _writing;
    private CancellationToken _shutdown;
    private CancellationTokenSource? _stop, _sessionLifetime;
    private Task? _run;
    private IEquipmentSession? _session;
    private TaskCompletionSource? _writeDone;

    public OpcUaEquipmentClient(IAlarmConnectionSettingsStore settings, IEquipmentSessionFactory factory,
        IReadOnlyList<AlarmDefinition> alarms, IReadOnlyList<IoDefinition> io, IReadOnlyList<ParameterDefinition> parameters,
        IAuthorizationService authorization, IEquipmentRuntimeRepository runtime, bool isSimulated = false, IReadOnlyDictionary<string, string>? definitionErrors = null, bool enableRecipes = false, IReadOnlyList<ProcessDefinition>? processDefinitions = null,
        EquipmentControlDefinitions? controlDefinitions = null)
    {
        _settings = settings; _factory = factory; _authorization = authorization; _runtime = runtime;
        IsSimulated = isSimulated; _definitionErrors = definitionErrors ?? new Dictionary<string, string>();
        _parameters = parameters.ToDictionary(p => p.Address, StringComparer.Ordinal);
        _addresses = new() {
            [EquipmentGroups.Alarm] = alarms.Select(p => p.Address).ToArray(),
            [EquipmentGroups.Io] = io.Select(p => p.Address).ToArray(),
            [EquipmentGroups.Parameter] = parameters.Select(p => p.Address).ToArray()
        };
        if (processDefinitions is not null)
            _addresses[EquipmentGroups.Process] = processDefinitions.Select(p => p.Address).ToArray();
        if (enableRecipes)
        {
            _addresses[EquipmentGroups.Recipe] = Enumerable.Range(0, 18).Select(i => $"EQ_Recipe1[{i}]").ToArray();
            _addresses[EquipmentGroups.RecipeOk] = [EquipmentGroups.RecipeOk];
            _addresses[EquipmentGroups.CoatOk] = [EquipmentGroups.CoatOk];
            _addresses[EquipmentGroups.RecipeLoad] = [EquipmentGroups.RecipeLoad];
        }
        if (controlDefinitions is not null)
        {
            foreach (var group in controlDefinitions.Addresses.GroupBy(EquipmentAddress.Group, StringComparer.Ordinal))
            {
                if (!EquipmentGroups.Monitored.Contains(group.Key, StringComparer.Ordinal))
                    throw new InvalidOperationException($"控制定义包含不支持的OPC UA组：{group.Key}");
                var merged = _addresses.GetValueOrDefault(group.Key, [])
                    .Concat(group).Distinct(StringComparer.Ordinal).ToArray();
                _addresses[group.Key] = merged;
            }
        }
        // 保证所有系统控制标量点（含真空流程 fbButton*_Output 反馈）都被订阅；仅在存在控制定义时补齐，
        // 避免无定义库的简化连接（如回环测试）去订阅服务器不存在的标量节点。
        if (controlDefinitions is not null)
            foreach (var point in EquipmentGroups.SystemControlPoints)
                if (!_addresses.ContainsKey(point))
                    _addresses[point] = [point];
        foreach (var pair in _addresses)
            foreach (var address in pair.Value)
                if (!_addressGroups.TryAdd(address, pair.Key) && _addressGroups[address] != pair.Key)
                    throw new InvalidOperationException($"设备地址跨组重复：{address}");
        try { _options = settings.Load(); }
        catch (Exception ex) { _options = new(); _loadError = $"连接配置读取失败：{ex.Message}"; _phase = AlarmConnectionPhase.Failed; }
        MarkUnknown("未连接");
    }

    public bool IsSimulated { get; }
    public bool IsWriteInProgress { get { lock (_gate) return _writing || _recipePending || _recipeLease is not null; } }
    public event EventHandler? StateChanged;
    public event EventHandler? SnapshotChanged;
    public AlarmConnectionStatus Status
    {
        get { lock (_gate) return new(_options.EndpointUrl, _phase, (_loadError ?? _message) + "\n" +
            string.Join("\n", _groups.Values.Select(g => $"{g.Name}：{g.Message}")) + (_recipeLease is not null ? "\n配方运行中，禁止切换连接" : _writing ? "\n写入处理中，禁止切换连接" : "")); }
    }

    public EquipmentSnapshot Snapshot()
    {
        lock (_gate) return new(_options.EndpointUrl, _epoch, _phase == AlarmConnectionPhase.Connected, _writing || _recipePending,
            IsSimulated, new Dictionary<string, EquipmentGroupState>(_groups)) { WriteAddress = _writeAddress, WriteStage = _writeStage };
    }

    public async Task StartAsync(CancellationToken token)
    {
        await _lifecycle.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) return;
            _started = true; _shutdown = token;
            if (_loadError is not null) SetPhase(AlarmConnectionPhase.Failed, _loadError);
            else if (_options.Enabled) StartRun();
            else SetPhase(AlarmConnectionPhase.Disconnected, "连接已禁用，可在左下角保存并连接");
        }
        finally { _lifecycle.Release(); }
    }

    public async Task ApplyEndpointAsync(string endpoint, CancellationToken token = default)
    {
        endpoint = OpcUaAlarmOptions.ValidateEndpoint(endpoint);
        if (!await _lifecycle.WaitAsync(0, token).ConfigureAwait(false))
            throw new InvalidOperationException("正在写入参数或切换连接，请等待完成");
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            lock (_gate) if (_recipeLease is not null || _recipePending) throw new InvalidOperationException("配方运行中禁止切换Endpoint");
            if (IsSimulated) throw new InvalidOperationException("模拟模式不能切换到真实PLC；请去掉模拟参数后重启");
            _shutdown.ThrowIfCancellationRequested();
            await StopRunAsync().ConfigureAwait(false);
            var next = _options with { EndpointUrl = endpoint, Enabled = true };
            try { await _settings.SaveAsync(next, token).ConfigureAwait(false); }
            catch (Exception ex) { SetPhase(AlarmConnectionPhase.Failed, $"连接配置保存失败：{ex.Message}"); throw; }
            _options = next; _loadError = null;
            if (_started) StartRun();
        }
        finally { _lifecycle.Release(); }
    }

    private void StartRun()
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(_shutdown);
        var token = _stop.Token;
        SetPhase(AlarmConnectionPhase.Connecting, "正在连接PLC并发现设备数组");
        _run = Task.Run(() => RunAsync(token), CancellationToken.None);
    }

    private async Task StopRunAsync()
    {
        if (_stop is null) return;
        await _stop.CancelAsync().ConfigureAwait(false);
        SetPhase(AlarmConnectionPhase.Disconnected, "PLC未连接");
        if (_run is not null) await _run.ConfigureAwait(false);
        _stop.Dispose(); _stop = null; _run = null;
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            _options.Validate();
            while (!token.IsCancellationRequested)
            {
                try
                {
                    lock (_gate) { _epoch++; SetPhase(AlarmConnectionPhase.Connecting, "正在连接PLC并发现设备数组"); }
                    await using var session = await _factory.ConnectAsync(_options, token).ConfigureAwait(false);
                    using var sessionLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
                    var initial = new Dictionary<string, DataValue>();
                    var bindings = await session.DiscoverAsync(token).ConfigureAwait(false);
                    var enabled = new List<string>();
                    foreach (var group in _addresses.Keys)
                    {
                        if (_definitionErrors.TryGetValue(group, out var definitionError))
                        { SetGroupError(group, "定义读取失败：" + definitionError); continue; }
                        var binding = bindings.GetValueOrDefault(group) ?? new EquipmentBinding(group, false, false, null, "未发现节点");
                        if (!binding.Available) { SetGroupError(group, binding.Error); continue; }
                        if (EquipmentGroups.IsWriteOnlyRecipeTrigger(group))
                        {
                            ApplyWriteOnlyRecipeTrigger(group, binding);
                            continue;
                        }
                        try
                        {
                            initial[group] = await session.ReadGroupAsync(group, null, token).ConfigureAwait(false);
                            enabled.Add(group);
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                        catch (Exception ex) { SetGroupError(group, $"初次读取失败：{ex.Message}"); }
                    }
                    var rejectedGroups = _addresses.Keys.Except(enabled).ToHashSet();
                    var ready = false;
                    var accepting = true;
                    var pending = new Queue<(string Group, DataValue Value)>();
                    try
                    {
                        var errors = await session.SubscribeGroupsAsync(enabled, (group, value) =>
                        {
                            lock (_gate)
                            {
                                if (!accepting || token.IsCancellationRequested || rejectedGroups.Contains(group)) return;
                                if (!ready)
                                {
                                    if (pending.Count >= 1000) { accepting = false; return; }
                                    pending.Enqueue((group, value));
                                }
                                else ApplyValue(group, value, bindings[group]);
                            }
                        }, token).ConfigureAwait(false);
                        lock (_gate)
                        {
                            token.ThrowIfCancellationRequested();
                            if (!accepting) throw new InvalidOperationException("设备通知初始缓冲溢出");
                            rejectedGroups.UnionWith(errors.Keys);
                            _session = session; _sessionLifetime = sessionLifetime;
                            _phase = AlarmConnectionPhase.Connected; _message = "PLC已连接";
                            foreach (var group in enabled)
                                if (errors.TryGetValue(group, out var error)) SetGroupError(group, error);
                                else ApplyValue(group, initial[group], bindings[group]);
                            while (pending.TryDequeue(out var entry))
                                if (!errors.ContainsKey(entry.Group)) ApplyValue(entry.Group, entry.Value, bindings[entry.Group]);
                            ready = true;
                            Notify();
                        }
                        await session.WaitForDisconnectAsync(sessionLifetime.Token).ConfigureAwait(false);
                        throw new InvalidOperationException("PLC会话已结束");
                    }
                    finally
                    {
                        Task? write;
                        lock (_gate)
                        {
                            accepting = false; _session = null; _sessionLifetime = null;
                            SetPhase(AlarmConnectionPhase.Disconnected, "PLC订阅已断开");
                            write = _writeDone?.Task;
                        }
                        await sessionLifetime.CancelAsync().ConfigureAwait(false);
                        if (write is not null) await write.ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex) { SetPhase(AlarmConnectionPhase.Failed, $"PLC连接失败：{ex.Message}"); }
                await Task.Delay(_options.ReconnectDelayMs, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { SetPhase(AlarmConnectionPhase.Failed, ex.Message); }
        finally { if (token.IsCancellationRequested) SetPhase(AlarmConnectionPhase.Disconnected, "PLC未连接"); }
    }

    private void ApplyValue(string group, DataValue data, EquipmentBinding binding)
    {
        if (group == EquipmentGroups.RecipeOk || group == EquipmentGroups.CoatOk)
        { ApplyRecipeFlag(group, data, binding); return; }
        if (EquipmentGroups.IsScalar(group)) { ApplyScalarValue(group, data, binding); return; }
        var array = data.Value as Array;
        var arrayValid = StatusCode.IsGood(data.StatusCode) && !data.StatusCode.Overflow && array is { Rank: 1 }
            && array.GetLowerBound(0) == 0 && binding.ElementType is not null
            && array.GetType().GetElementType() == binding.ElementType
            && (group != EquipmentGroups.Process || binding.ElementType == typeof(float) || binding.ElementType == typeof(double));
        var now = DateTimeOffset.Now;
        DateTimeOffset? source = data.SourceTimestamp == DateTime.MinValue ? null
            : new(DateTime.SpecifyKind(data.SourceTimestamp, DateTimeKind.Utc));
        var points = new Dictionary<string, EquipmentPoint>(StringComparer.Ordinal);
        foreach (var address in _addresses[group])
        {
            var index = EquipmentAddress.Index(address, group);
            object? value = arrayValid && index < array!.Length ? array.GetValue(index) : null;
            if (group == EquipmentGroups.Alarm || group == EquipmentGroups.Io) value = AlarmArrayDecoder.DecodeElement(value);
            else if (value is not bool && !ParameterValueCodec.IsValid(value)) value = null;
            var good = value is not null;
            var previous = _groups.GetValueOrDefault(group)?.Points.GetValueOrDefault(address);
            points[address] = new(address, good ? value : previous?.Value,
                good ? AlarmQuality.Good : AlarmQuality.Bad, now, source, binding.ElementType,
                good && binding.CanWrite && EquipmentGroups.IsWritable(group), _epoch);
        }
        var validCount = points.Values.Count(p => p.Quality == AlarmQuality.Good);
        var isReady = validCount > 0 && validCount == points.Count;
        var message = isReady ? $"{validCount}点有效" + (EquipmentGroups.IsWritable(group) ? binding.CanWrite ? "，可写" : "，只读" : "，只读")
            : $"{validCount}/{points.Count}点有效；数组长度{array?.Length ?? 0}，需覆盖下标{_addresses[group].Select(a => EquipmentAddress.Index(a, group)).DefaultIfEmpty(-1).Max()}；质量/类型/越界异常";
        _groups[group] = new(group, isReady, message, points);
        Notify();
    }

    private void ApplyScalarValue(string group, DataValue data, EquipmentBinding binding)
    {
        var now = DateTimeOffset.Now;
        DateTimeOffset? source = data.SourceTimestamp == DateTime.MinValue ? null
            : new(DateTime.SpecifyKind(data.SourceTimestamp, DateTimeKind.Utc));
        var value = StatusCode.IsGood(data.StatusCode) && !data.StatusCode.Overflow
            && binding.ElementType is not null && data.Value?.GetType() == binding.ElementType
            && (data.Value is bool || ParameterValueCodec.IsValid(data.Value)) ? data.Value : null;
        var previous = _groups.GetValueOrDefault(group)?.Points.GetValueOrDefault(group);
        var point = new EquipmentPoint(group, value ?? previous?.Value,
            value is null ? AlarmQuality.Bad : AlarmQuality.Good, now, source, binding.ElementType,
            value is not null && binding.CanWrite && EquipmentGroups.IsWritable(group), _epoch);
        _groups[group] = new(group, value is not null,
            value is null ? "标量质量/类型异常" : binding.CanWrite ? "1点有效，可写" : "1点有效，只读",
            new Dictionary<string, EquipmentPoint>(StringComparer.Ordinal) { [group] = point });
        Notify();
    }

    private void ApplyWriteOnlyRecipeTrigger(string group, EquipmentBinding binding)
    {
        var ready = binding.CanWrite && binding.ElementType is not null;
        var point = new EquipmentPoint(group, null, ready ? AlarmQuality.Good : AlarmQuality.Bad,
            DateTimeOffset.Now, null, binding.ElementType, ready, _epoch);
        _groups[group] = new(group, ready,
            ready ? "仅写触发点有效，可写" : "仅写触发点不可写或类型无效",
            new Dictionary<string, EquipmentPoint>(StringComparer.Ordinal) { [group] = point });
        Notify();
    }

    private void SetGroupError(string group, string message)
    {
        lock (_gate)
        {
            var points = _groups[group].Points.ToDictionary(p => p.Key,
                p => p.Value with { Quality = AlarmQuality.Unknown, CanWrite = false, Epoch = _epoch });
            _groups[group] = new(group, false, message, points);
            Notify();
        }
    }

    private void MarkUnknown(string message)
    {
        foreach (var group in _addresses.Keys)
        {
            var old = _groups.GetValueOrDefault(group);
            _groups[group] = new(group, false, message, _addresses[group].ToDictionary(address => address, address =>
                old?.Points.GetValueOrDefault(address) is { } p ? p with { Quality = AlarmQuality.Unknown, CanWrite = false, Epoch = _epoch }
                    : new EquipmentPoint(address, null, AlarmQuality.Unknown, DateTimeOffset.MinValue, null, null, false, _epoch)));
        }
    }

    private void SetPhase(AlarmConnectionPhase phase, string message)
    {
        lock (_gate)
        {
            _phase = phase; _message = message;
            if (phase != AlarmConnectionPhase.Connected) MarkUnknown(message);
            Notify();
        }
    }

    private void Notify()
    {
        CheckRecipeHealth();
        StateChanged?.Invoke(this, EventArgs.Empty);
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<ParameterWriteResult> WriteParameterAsync(ParameterWriteRequest request, CancellationToken token = default)
    {
        if (!await _lifecycle.WaitAsync(0, token).ConfigureAwait(false))
            return new(ParameterWriteOutcome.Rejected, "已有参数写入或连接切换正在处理，本次未提交");
        OperationLogRecord? audit = null;
        var sent = false;
        var writeAccepted = false;
        object? lastReadback = null;
        CancellationToken connectionToken = default;
        ParameterWriteResult result;
        object? target = null;
        string endpoint = "";
        try
        {
            IEquipmentSession session;
            ParameterDefinition definition;
            EquipmentPoint point;
            string user;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_recipePending) throw new InvalidOperationException("配方写入事务正在等待，本次参数未提交");
                if (!_authorization.CanOperate(PermissionKey.ParameterSettings)) throw new InvalidOperationException("请登录具有参数设置权限的账号");
                if (!_parameters.TryGetValue(request.Address, out definition!)) throw new InvalidOperationException("不是已定义的参数地址");
                point = _groups[EquipmentGroups.Parameter].Points[request.Address];
                if (_phase != AlarmConnectionPhase.Connected || _session is null || _sessionLifetime is null
                    || point.Quality != AlarmQuality.Good || !point.CanWrite || point.ValueType is null)
                    throw new InvalidOperationException("PLC参数未就绪、质量无效或不可写");
                if (request.Epoch != _epoch) throw new InvalidOperationException("连接已变化，请重新核对参数后输入");
                session = _session; connectionToken = _sessionLifetime.Token; endpoint = _options.EndpointUrl;
                user = _authorization.CurrentUserName;
                target = ParameterValueCodec.Parse(request.Draft, point.ValueType, definition);
                _writeAddress = request.Address; _writeStage = "正在校验并保存待处理审计";
                _writing = true; _writeDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Notify();
            }
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, connectionToken);
            var index = EquipmentAddress.Index(request.Address, EquipmentGroups.Parameter);
            var original = await ReadElementAsync(session, index, point.ValueType!, lifetime.Token).ConfigureAwait(false);
            if (!ParameterValueCodec.Same(original, request.ExpectedValue))
                throw new InvalidOperationException("PLC原值在编辑期间已变化；本次未写入，请Esc后重新核对输入");
            if (ParameterValueCodec.Same(original, target))
                return new(ParameterWriteOutcome.Unchanged, "与PLC当前值相同，未发送写入", original);
            var pending = new OperationLogRecord(DateTimeOffset.Now, user, definition.DisplayName,
                $"参数写入 {request.Address}", false, false, "") { Outcome = "待处理" };
            await Task.Run(() => _runtime.BeginWrite(pending), lifetime.Token).ConfigureAwait(false);
            audit = pending;
            lifetime.Token.ThrowIfCancellationRequested();
            if (!_authorization.CanOperate(PermissionKey.ParameterSettings) || _authorization.CurrentUserName != pending.UserName)
                throw new InvalidOperationException("账号或参数写入权限已变化，本次未发送");
            lock (_gate)
                if (_epoch != request.Epoch || _session != session || _phase != AlarmConnectionPhase.Connected
                    || !_groups[EquipmentGroups.Parameter].Points[request.Address].CanWrite)
                    throw new InvalidOperationException("参数状态已变化，本次未发送");
            using (var writeDeadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                writeDeadline.CancelAfter(TimeSpan.FromSeconds(5));
                sent = true;
                await session.WriteParameterElementAsync(index, target, writeDeadline.Token).ConfigureAwait(false);
            }
            writeAccepted = true;
            lock (_gate) { _message = "参数已发送，等待回读确认"; _writeStage = "已发送，等待回读"; Notify(); }
            using var readbackDeadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            readbackDeadline.CancelAfter(TimeSpan.FromSeconds(5));
            while (true)
            {
                var actual = await ReadElementAsync(session, index, point.ValueType!, readbackDeadline.Token).ConfigureAwait(false);
                lastReadback = actual;
                if (ParameterValueCodec.Same(actual, target)) break;
                await Task.Delay(100, readbackDeadline.Token).ConfigureAwait(false);
            }
            result = new(ParameterWriteOutcome.Confirmed, "写入已确认（PLC变量值）", target);
        }
        catch (ParameterWriteRejectedException ex) { result = new(ParameterWriteOutcome.Rejected, ex.Message); }
        catch (Exception ex)
        {
            var reason = connectionToken.IsCancellationRequested ? "通讯中断或程序关闭"
                : ex is OperationCanceledException && token.IsCancellationRequested ? "操作取消，可能已经发送"
                : ex is OperationCanceledException && writeAccepted && lastReadback is not null
                    ? $"5秒回读超时：PLC为{ParameterValueCodec.Format(lastReadback)}，目标为{ParameterValueCodec.Format(target)}"
                : ex is OperationCanceledException && writeAccepted ? "5秒内未获得有效回读"
                : ex is OperationCanceledException ? "写入请求超时，服务端执行结果未知"
                : writeAccepted ? $"回读异常：{ex.Message}" : $"通讯或执行异常：{ex.Message}";
            result = new(sent ? ParameterWriteOutcome.Unknown : ParameterWriteOutcome.Rejected,
                sent ? $"结果未确认：{reason}；未自动重试" : $"未写入：{ex.Message}");
        }
        finally
        {
            // Completion is finalized below; the outer lifecycle gate still prevents a second submit.
            // Unchanged operations have no audit and return through this path too.
            if (audit is null) EndWrite();
        }
        try
        {
            if (audit is not null)
            {
                var final = audit with { IsSuccessful = result.Outcome == ParameterWriteOutcome.Confirmed,
                    Outcome = result.Outcome == ParameterWriteOutcome.Confirmed ? "已确认"
                        : result.Outcome == ParameterWriteOutcome.Unknown ? "结果未确认" : "已拒绝",
                    FailureReason = result.Outcome == ParameterWriteOutcome.Confirmed ? "" : result.Message };
                var cache = result.Outcome == ParameterWriteOutcome.Confirmed
                    ? new ParameterCache(endpoint, request.Address, ParameterValueCodec.Format(target), DateTimeOffset.Now, IsSimulated) : null;
                try { await Task.Run(() => _runtime.FinishWrite(final, cache), CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { result = result with { Message = result.Message + "；本机记录保存失败：" + ex.Message }; }
            }
            return result;
        }
        finally { if (audit is not null) EndWrite(); }
    }

    private void EndWrite()
    {
        lock (_gate)
        {
            _writing = false; _writeAddress = null; _writeStage = "";
            _writeDone?.TrySetResult(); _writeDone = null;
            if (_phase == AlarmConnectionPhase.Connected) _message = "PLC已连接";
            Notify();
        }
        _lifecycle.Release();
    }

    private static async Task<object> ReadElementAsync(IEquipmentSession session, int index, Type type, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var data = await session.ReadGroupAsync(EquipmentGroups.Parameter, index, deadline.Token).ConfigureAwait(false);
        object? value = data.Value is Array { Rank: 1, Length: 1 } array ? array.GetValue(0) : data.Value;
        if (!StatusCode.IsGood(data.StatusCode) || data.StatusCode.Overflow || !ParameterValueCodec.IsValid(value) || value!.GetType() != type)
            throw new InvalidOperationException("参数回读质量或数据类型无效");
        return value;
    }

    public async ValueTask DisposeAsync()
    {
        try { _stop?.Cancel(); } catch (ObjectDisposedException) { }
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try { if (_disposed) return; _disposed = true; await StopRunAsync().ConfigureAwait(false); }
        finally { _lifecycle.Release(); }
    }
}
