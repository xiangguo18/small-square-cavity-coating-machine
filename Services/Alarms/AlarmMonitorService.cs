using Small_square_cavity_coating_machine.Models.Alarms;
using Small_square_cavity_coating_machine.Models.History;
using Small_square_cavity_coating_machine.Services.History;

namespace Small_square_cavity_coating_machine.Services.Alarms;

public sealed class AlarmMonitorService : IAlarmMonitorService
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, AlarmDefinition> _definitions;
    private readonly Dictionary<string, AlarmLogRecord> _active = new(StringComparer.Ordinal);
    private readonly HashSet<string> _known = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _lastObservation = new(StringComparer.Ordinal);
    private readonly IAlarmSignalSource _source;
    private readonly IAlarmLogRepository _repository;
    private bool _connected;
    private bool _disposed;
    private string _connectionMessage = "报警信号未连接";

    public AlarmMonitorService(IReadOnlyList<AlarmDefinition> definitions,
        IAlarmSignalSource source, IAlarmLogRepository repository)
    {
        _definitions = definitions.ToDictionary(d => d.Address, StringComparer.Ordinal);
        _source = source;
        _repository = repository;
        foreach (var record in repository.LoadUncleared().Where(r => r.IsSimulated == source.IsSimulated)
                     .OrderBy(r => r.OccurredAt))
        {
            var unknown = record with { Quality = AlarmQuality.Unknown };
            _active[record.Address] = unknown;
            repository.Upsert(unknown);
        }
        source.ConnectionChanged += OnConnection;
        source.SignalReceived += OnSignal;
    }

    public event EventHandler? StateChanged;

    public AlarmMonitorSnapshot Snapshot()
    {
        lock (_gate) return new AlarmMonitorSnapshot(_active.Values
            .OrderByDescending(r => r.Priority).ThenByDescending(r => r.OccurredAt).ToArray(),
            _connected, _connected && _definitions.Count > 0 && _known.Count == _definitions.Count
                && _active.Values.All(r => r.Quality == AlarmQuality.Good),
            _source.IsSimulated, _connectionMessage);
    }

    private void OnConnection(object? sender, AlarmConnection connection)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _connected = connection.IsConnected;
            _connectionMessage = connection.Message;
            _known.Clear();
            _lastObservation.Clear();
            foreach (var key in _active.Keys.ToArray())
            {
                var record = _active[key] with { Quality = AlarmQuality.Unknown };
                _active[key] = record;
                _repository.Upsert(record);
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnSignal(object? sender, AlarmSignal signal)
    {
        lock (_gate)
        {
            if (_disposed || !_connected || !_definitions.TryGetValue(signal.Address, out var definition)) return;
            var hadObservation = _lastObservation.ContainsKey(signal.Address);
            if (_lastObservation.TryGetValue(signal.Address, out var previous) && signal.ObservedAt < previous) return;
            _lastObservation[signal.Address] = signal.ObservedAt;
            var wasKnown = _known.Contains(signal.Address);
            _active.TryGetValue(signal.Address, out var existing);
            if (signal.Quality != AlarmQuality.Good || !signal.IsActive.HasValue)
            {
                _known.Remove(signal.Address);
                if (existing is not null)
                {
                    var unknown = existing with { Quality = AlarmQuality.Unknown };
                    _active[signal.Address] = unknown;
                    _repository.Upsert(unknown);
                }
            }
            else
            {
                _known.Add(signal.Address);
                if (signal.IsActive.Value)
                {
                    if (existing is null)
                    {
                        var record = new AlarmLogRecord(signal.ObservedAt, definition.Severity, definition.Name,
                            definition.DisplayMessage, null, _source.IsSimulated)
                        {
                            EventDefinitionId = definition.Id, Address = definition.Address,
                            Priority = definition.Priority, ObservedOnConnect = !wasKnown && !hadObservation,
                            ObservedAfterQualityGap = !wasKnown && hadObservation,
                            SourceTimestamp = signal.SourceTimestamp
                        };
                        _active[signal.Address] = record;
                        _repository.Upsert(record);
                    }
                    else if (existing.Quality != AlarmQuality.Good)
                    {
                        var restored = existing with { Quality = AlarmQuality.Good };
                        _active[signal.Address] = restored;
                        _repository.Upsert(restored);
                    }
                }
                else if (existing is not null)
                {
                    _active.Remove(signal.Address);
                    _repository.Upsert(existing with { ClearedAt = signal.ObservedAt,
                        Quality = AlarmQuality.Good, RecoveryObservedAfterGap = !wasKnown });
                }
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _source.ConnectionChanged -= OnConnection;
            _source.SignalReceived -= OnSignal;
        }
    }
}
