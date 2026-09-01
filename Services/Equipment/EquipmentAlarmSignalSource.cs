using Small_square_cavity_coating_machine.Models.Alarms;
using Small_square_cavity_coating_machine.Models.Equipment;
using Small_square_cavity_coating_machine.Services.Alarms;

namespace Small_square_cavity_coating_machine.Services.Equipment;

/// <summary>Compatibility adapter. AlarmMonitorService remains independent of IO/parameter writes.</summary>
public sealed class EquipmentAlarmSignalSource : IAlarmSignalSource
{
    private readonly IOpcUaEquipmentClient _client;
    private EquipmentGroupState? _previous;
    private long _epoch = -1;
    private bool _connected;
    public EquipmentAlarmSignalSource(IOpcUaEquipmentClient client)
    {
        _client = client;
        client.SnapshotChanged += OnSnapshot;
    }
    public bool IsSimulated => _client.IsSimulated;
    public event EventHandler<AlarmSignal>? SignalReceived;
    public event EventHandler<AlarmConnection>? ConnectionChanged;
    public Task StartAsync(IReadOnlyList<AlarmDefinition> definitions, CancellationToken cancellationToken) =>
        _client.StartAsync(cancellationToken);
    private void OnSnapshot(object? sender, EventArgs e)
    {
        var snapshot = _client.Snapshot();
        var group = snapshot.Groups[EquipmentGroups.Alarm];
        if (_connected != snapshot.IsConnected || _epoch != snapshot.Epoch)
        {
            _connected = snapshot.IsConnected; _epoch = snapshot.Epoch;
            ConnectionChanged?.Invoke(this, new(_connected, group.Message));
            _previous = null;
        }
        if (!_connected || ReferenceEquals(group, _previous)) return;
        _previous = group;
        foreach (var point in group.Points.Values)
            SignalReceived?.Invoke(this, new(point.Address, point.Quality == AlarmQuality.Good ? point.Value as bool? : null,
                point.Quality, point.ObservedAt, point.SourceTimestamp));
    }
    public ValueTask DisposeAsync() { _client.SnapshotChanged -= OnSnapshot; return ValueTask.CompletedTask; }
}
