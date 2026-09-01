using Small_square_cavity_coating_machine.Models.Alarms;
using Small_square_cavity_coating_machine.Models.History;

namespace Small_square_cavity_coating_machine.Services.Alarms;

public interface IAlarmDefinitionRepository
{
    IReadOnlyList<AlarmDefinition> Load();
}

public interface IAlarmSignalSource : IAsyncDisposable
{
    bool IsSimulated { get; }
    event EventHandler<AlarmSignal>? SignalReceived;
    event EventHandler<AlarmConnection>? ConnectionChanged;
    Task StartAsync(IReadOnlyList<AlarmDefinition> definitions, CancellationToken cancellationToken);
}

public sealed record AlarmMonitorSnapshot(IReadOnlyList<AlarmLogRecord> ActiveAlarms,
    bool IsConnected, bool AllSignalsKnown, bool IsSimulated, string ConnectionMessage);

public interface IAlarmMonitorService : IDisposable
{
    event EventHandler? StateChanged;
    AlarmMonitorSnapshot Snapshot();
}
