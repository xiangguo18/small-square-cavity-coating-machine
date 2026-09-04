using Opc.Ua;
using Small_square_cavity_coating_machine.Models.History;
using Small_square_cavity_coating_machine.Models.Equipment;
using Small_square_cavity_coating_machine.Services.Alarms;

namespace Small_square_cavity_coating_machine.Services.Equipment;

/// <summary>Explicit offline mode; values never enter a real PLC transport.</summary>
public sealed class SimulatedEquipmentSessionFactory : IEquipmentSessionFactory, ISimulatedAlarmControl
{
    private readonly Lock _gate = new();
    private readonly bool[] _alarms = new bool[552];
    private readonly bool[] _io = new bool[532];
    private readonly float[] _parameters = new float[21];
    private readonly float[] _recipe = new float[24];
    private readonly float[] _process;
    private readonly bool[] _partCommands = new bool[1000];
    private readonly ushort[] _partStates = Enumerable.Repeat((ushort)1, 500).ToArray();
    private readonly float[] _partData = new float[27];
    private readonly float[] _partDataSet1 = new float[21];
    private readonly float[] _partDataSet2 = new float[3];
    private readonly bool[] _interlocks = Enumerable.Repeat(true, 16).ToArray();
    private readonly Dictionary<string, bool> _scalars = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<ProcessDefinition> _processDefinitions;
    private bool _recipeOk = true, _coatOk = true;
    private Session? _current;
    private bool _online = true;
    public SimulatedEquipmentSessionFactory(IReadOnlyList<ParameterDefinition> parameters, IReadOnlyList<ProcessDefinition>? processDefinitions = null)
    {
        _processDefinitions = processDefinitions ?? [];
        _process = new float[Math.Max(8, _processDefinitions.Select(p => EquipmentAddress.Index(p.Address, EquipmentGroups.Process)).DefaultIfEmpty(7).Max() + 1)];
        foreach (var definition in parameters) _parameters[EquipmentAddress.Index(definition.Address, EquipmentGroups.Parameter)] = (float)definition.Value;
        foreach (var point in EquipmentGroups.SystemControlPoints) _scalars[point] = false;
        _partData[0] = _partDataSet1[0] = 80f;
        _partData[1] = _partDataSet1[1] = 45f;
        _partData[2] = _partDataSet1[2] = 186.5f;
        _partData[3] = _partDataSet2[2] = 12f;
        _partData[7] = _partDataSet1[4] = 0.6f;
        _partData[8] = _partDataSet1[5] = 0.6f;
        _partData[9] = _partDataSet1[6] = 0.6f;
        _partData[10] = _partData[11] = _partData[12] = 13.6f;
        _partData[21] = _partDataSet2[0] = 1000f;
        _partData[22] = 480f; _partData[23] = 1.8f;
        _partData[24] = _partDataSet2[1] = 1000f;
        _partData[25] = 480f; _partData[26] = 1.8f;
    }
    public Task<IEquipmentSession> ConnectAsync(OpcUaAlarmOptions options, CancellationToken token)
    {
        lock (_gate)
        {
            if (!_online) throw new InvalidOperationException("模拟PLC已断开");
            _current = new Session(this);
            return Task.FromResult<IEquipmentSession>(_current);
        }
    }
    public void Set(string address, bool active)
    {
        lock (_gate) { _alarms[EquipmentAddress.Index(address, EquipmentGroups.Alarm)] = active; _current?.Publish(EquipmentGroups.Alarm); }
    }
    public void SetBadQuality(string address)
    {
        EquipmentAddress.Index(address, EquipmentGroups.Alarm);
        lock (_gate) _current?.Publish(EquipmentGroups.Alarm, StatusCodes.BadCommunicationError);
    }
    public void Disconnect() { lock (_gate) { _online = false; _current?.Failure.TrySetResult(); } }
    public void Connect() { lock (_gate) _online = true; }

    private Array Values(string group) => group switch {
        EquipmentGroups.Alarm => _alarms, EquipmentGroups.Io => _io,
        EquipmentGroups.Parameter => _parameters, EquipmentGroups.Process => _process, EquipmentGroups.Recipe => _recipe,
        EquipmentGroups.PartCommand => _partCommands, EquipmentGroups.PartState => _partStates,
        EquipmentGroups.PartData => _partData,
        EquipmentGroups.PartDataSet1 => _partDataSet1, EquipmentGroups.PartDataSet2 => _partDataSet2,
        EquipmentGroups.Interlock => _interlocks, _ => throw new ArgumentException(group) };

    private sealed class Session(SimulatedEquipmentSessionFactory owner) : IEquipmentSession
    {
        public TaskCompletionSource Failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Action<string, DataValue>? _callback;
        private bool _disposed;
        private readonly CancellationTokenSource _stop = new();
        private Task? _processLoop;
        public Task<IReadOnlyDictionary<string, EquipmentBinding>> DiscoverAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyDictionary<string, EquipmentBinding>>(EquipmentGroups.Monitored.ToDictionary(g => g,
                g => new EquipmentBinding(g, true, EquipmentGroups.IsWritable(g), ElementType(g), "")));
        private static Type ElementType(string group) => group == EquipmentGroups.PartState ? typeof(ushort)
            : group == EquipmentGroups.Parameter || group == EquipmentGroups.Recipe || group == EquipmentGroups.Process
                || group == EquipmentGroups.PartData || group == EquipmentGroups.PartDataSet1 || group == EquipmentGroups.PartDataSet2
                ? typeof(float) : typeof(bool);
        public Task<DataValue> ReadGroupAsync(string group, int? index, CancellationToken token)
        {
            lock (owner._gate)
            {
                token.ThrowIfCancellationRequested();
                if (_disposed || !owner._online) throw new InvalidOperationException("模拟连接已断开");
                if (EquipmentGroups.IsScalar(group)) return Task.FromResult(new DataValue(new Variant(ScalarValue(group))) { SourceTimestamp = DateTime.UtcNow });
                var values = owner.Values(group);
                var result = index.HasValue ? Array.CreateInstance(values.GetType().GetElementType()!, 1) : (Array)values.Clone();
                if (index.HasValue) result.SetValue(values.GetValue(index.Value), 0);
                return Task.FromResult(new DataValue(new Variant(result)) { SourceTimestamp = DateTime.UtcNow });
            }
        }
        private object ScalarValue(string group) => group == EquipmentGroups.RecipeOk ? owner._recipeOk
            : group == EquipmentGroups.CoatOk ? owner._coatOk : owner._scalars[group];
        public Task<IReadOnlyDictionary<string, string>> SubscribeGroupsAsync(IReadOnlyList<string> groups, Action<string, DataValue> onValue, CancellationToken token)
        {
            _callback = onValue;
            if (groups.Contains(EquipmentGroups.Process)) _processLoop = SimulateProcessAsync(_stop.Token);
            return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
        }
        private async Task SimulateProcessAsync(CancellationToken token)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            var tick = 0;
            try
            {
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                    lock (owner._gate)
                    {
                        var wave = Math.Sin(++tick / 30d);
                        float[] values = [(float)(13.6 + wave), (float)(13.2 + wave * 0.5),
                            (float)(480 + wave * 10), (float)(1.8 + wave * 0.1),
                            (float)(472 + wave * 10), (float)(1.65 + wave * 0.1), (float)(186.5 + wave * 5)];
                        foreach (var p in owner._processDefinitions)
                            owner._process[EquipmentAddress.Index(p.Address, EquipmentGroups.Process)] = values[p.Id];
                        if (owner._online) Publish(EquipmentGroups.Process);
                    }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }
        public Task WriteParameterElementAsync(int index, object value, CancellationToken token)
            => WriteElementAsync(EquipmentGroups.Parameter, index, value, token);

        public Task WriteElementAsync(string group, int? index, object value, CancellationToken token)
        {
            lock (owner._gate)
            {
                token.ThrowIfCancellationRequested();
                if (_disposed || !owner._online) throw new InvalidOperationException("模拟连接已断开");
                if (EquipmentGroups.IsScalar(group)) owner._scalars[group] = (bool)value;
                else if (index.HasValue) owner.Values(group).SetValue(value, index.Value);
                else throw new InvalidOperationException("数组写入缺少下标");
                ApplyFeedback(group, index, value);
                Publish(group);
                return Task.CompletedTask;
            }
        }

        public Task WriteArrayElementsAsync(string group, IReadOnlyList<ArrayWriteMutation> mutations, CancellationToken token)
        {
            lock (owner._gate)
            {
                token.ThrowIfCancellationRequested();
                if (_disposed || !owner._online) throw new InvalidOperationException("模拟连接已断开");
                foreach (var mutation in mutations)
                {
                    owner.Values(group).SetValue(mutation.Value, mutation.Index);
                    ApplyFeedback(group, mutation.Index, mutation.Value);
                }
                Publish(group);
                return Task.CompletedTask;
            }
        }

        private void ApplyFeedback(string group, int? index, object value)
        {
            if (group == EquipmentGroups.PartCommand && value is true && index.HasValue)
            {
                (int Part, bool Open) state = index.Value switch
                {
                    0 => (0, true), 1 => (0, false), 2 => (1, true), 3 => (1, false),
                    6 => (2, true), 7 => (2, false),
                    8 => (3, true), 9 => (3, false),
                    10 => (4, true), 11 => (4, true), 12 => (4, false),
                    13 => (5, true), 14 => (5, false),
                    15 => (24, true), 16 => (24, false), 17 => (25, true), 18 => (25, false),
                    21 => (6, true), 22 => (6, false), 23 => (7, true), 24 => (7, false),
                    27 => (11, true), 28 => (11, false), 29 => (12, true), 30 => (12, false),
                    31 => (15, true), 32 => (15, false), 33 => (14, true), 34 => (14, false),
                    35 => (13, true), 36 => (13, false), 37 => (16, true), 38 => (16, false),
                    39 => (20, true), 40 => (20, false),
                    41 => (21, true), 42 => (21, false), 43 => (22, true), 44 => (22, false),
                    45 => (23, true), 46 => (23, false), _ => (-1, false)
                };
                if (state.Part >= 0)
                {
                    owner._partStates[state.Part] = state.Open ? (ushort)2 : (ushort)1;
                    Publish(EquipmentGroups.PartState);
                }
            }
            else if (group == EquipmentGroups.PartDataSet1 && index.HasValue)
            {
                var readIndex = index.Value switch { 0 => 0, 1 => 1, 2 => 2, 4 => 7, 5 => 8, 6 => 9, _ => -1 };
                if (readIndex >= 0) { owner._partData[readIndex] = Convert.ToSingle(value); Publish(EquipmentGroups.PartData); }
                if (index.Value == 1)
                {
                    owner._partStates[2] = Convert.ToDouble(value) > 0d ? (ushort)2 : (ushort)1;
                    Publish(EquipmentGroups.PartState);
                }
            }
            else if (group == EquipmentGroups.PartDataSet2 && index.HasValue)
            {
                var readIndex = index.Value switch { 0 => 21, 1 => 24, 2 => 3, _ => -1 };
                if (readIndex >= 0) { owner._partData[readIndex] = Convert.ToSingle(value); Publish(EquipmentGroups.PartData); }
            }
            else if (group.StartsWith("EQ_", StringComparison.Ordinal) && group != EquipmentGroups.PassInterlock)
            {
                var output = group switch
                {
                    "EQ_Manual" => "fbButtonManual_Output", "EQ_Auto" => "fbButtonAuto_Output",
                    "EQ_Semi" => "fbButtonSemi_Output", "EQ_Start" => "fbButtonStart_Output",
                    "EQ_Stop" => "fbButtonStop_Output", "EQ_Reset" => "fbButtonReset_Output", _ => ""
                };
                if (output.Length > 0) { owner._scalars[output] = (bool)value; Publish(output); }
            }
        }
        public Task WriteRecipeValueAsync(string group, object value, CancellationToken token)
        {
            lock (owner._gate)
            {
                token.ThrowIfCancellationRequested();
                if (_disposed || !owner._online) throw new InvalidOperationException("模拟连接已断开");
                if (group == EquipmentGroups.Recipe) Array.Copy((Array)value, owner._recipe, 18);
                else if (group == EquipmentGroups.CoatOk) owner._coatOk = (bool)value;
                else if (group == EquipmentGroups.RecipeOk)
                {
                    owner._recipeOk = (bool)value;
                    if (!owner._recipeOk) _ = CompleteSimulatedLayerAsync();
                }
                else throw new InvalidOperationException("不是配方点");
                Publish(group);
                return Task.CompletedTask;
            }
        }
        private async Task CompleteSimulatedLayerAsync()
        {
            await Task.Delay(900).ConfigureAwait(false);
            lock (owner._gate)
                if (!_disposed && owner._online) { owner._recipeOk = true; Publish(EquipmentGroups.RecipeOk); }
        }
        public void Publish(string group, StatusCode? quality = null)
        {
            if (_disposed || _callback is null) return;
            _callback(group, new DataValue(new Variant(EquipmentGroups.IsScalar(group) ? ScalarValue(group) : owner.Values(group).Clone())) {
                StatusCode = quality ?? StatusCodes.Good, SourceTimestamp = DateTime.UtcNow });
        }
        public Task WaitForDisconnectAsync(CancellationToken token) => Failure.Task.WaitAsync(token);
        public async ValueTask DisposeAsync()
        {
            lock (owner._gate) { _disposed = true; _callback = null; }
            await _stop.CancelAsync().ConfigureAwait(false);
            if (_processLoop is not null) await _processLoop.ConfigureAwait(false);
            _stop.Dispose();
        }
    }
}

public sealed class SimulationConnectionSettings : IAlarmConnectionSettingsStore
{
    public OpcUaAlarmOptions Load() => new();
    public Task SaveAsync(OpcUaAlarmOptions options, CancellationToken token) => throw new InvalidOperationException("模拟连接不保存真实地址");
}
