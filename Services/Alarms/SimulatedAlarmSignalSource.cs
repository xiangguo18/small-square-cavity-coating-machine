using Small_square_cavity_coating_machine.Models.Alarms;

namespace Small_square_cavity_coating_machine.Services.Alarms;

/// <summary>Explicit opt-in only. No timer-generated fake alarms and no PLC writes.</summary>
public interface ISimulatedAlarmControl
{
    void Set(string address, bool active);
    void SetBadQuality(string address);
    void Disconnect();
    void Connect();
}

public sealed class SimulatedAlarmSignalSource : IAlarmSignalSource, ISimulatedAlarmControl
{
    private readonly Dictionary<string, bool> _values = new(StringComparer.Ordinal);
    private bool _connected;
    public bool IsSimulated => true;
    public event EventHandler<AlarmSignal>? SignalReceived;
    public event EventHandler<AlarmConnection>? ConnectionChanged;

    public Task StartAsync(IReadOnlyList<AlarmDefinition> definitions, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var definition in definitions) _values.TryAdd(definition.Address, false);
        Connect();
        return Task.CompletedTask;
    }

    public void Connect()
    {
        _connected = true;
        ConnectionChanged?.Invoke(this, new AlarmConnection(true, "模拟报警源已连接（非真实PLC）"));
        foreach (var pair in _values) Publish(pair.Key, pair.Value);
    }

    public void Disconnect()
    {
        _connected = false;
        ConnectionChanged?.Invoke(this, new AlarmConnection(false, "模拟报警源已断开"));
    }

    public void Set(string address, bool active)
    {
        if (!_values.ContainsKey(address)) throw new ArgumentException("未定义的报警地址", nameof(address));
        _values[address] = active;
        if (_connected) Publish(address, active);
    }

    public void SetBadQuality(string address)
    {
        if (_connected && _values.ContainsKey(address))
            SignalReceived?.Invoke(this, new AlarmSignal(address, null, AlarmQuality.Bad, DateTimeOffset.Now));
    }

    private void Publish(string address, bool active) => SignalReceived?.Invoke(this,
        new AlarmSignal(address, active, AlarmQuality.Good, DateTimeOffset.Now));

    public ValueTask DisposeAsync() { Disconnect(); return ValueTask.CompletedTask; }
}

public sealed class UnavailableAlarmSignalSource(string message) : IAlarmSignalSource
{
    public bool IsSimulated => false;
    public event EventHandler<AlarmSignal>? SignalReceived { add { } remove { } }
    public event EventHandler<AlarmConnection>? ConnectionChanged;
    public Task StartAsync(IReadOnlyList<AlarmDefinition> definitions, CancellationToken cancellationToken)
    {
        ConnectionChanged?.Invoke(this, new AlarmConnection(false, message));
        return Task.CompletedTask;
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
