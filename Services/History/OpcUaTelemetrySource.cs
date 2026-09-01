using Small_square_cavity_coating_machine.Models.Alarms;
using Small_square_cavity_coating_machine.Models.Equipment;
using Small_square_cavity_coating_machine.Models.History;
using Small_square_cavity_coating_machine.Services.Equipment;

namespace Small_square_cavity_coating_machine.Services.History;

/// <summary>Samples the shared read-only process snapshot; never creates a PLC session.</summary>
public sealed class OpcUaTelemetrySource : ITelemetrySource
{
    private readonly IOpcUaEquipmentClient _client;
    private readonly IReadOnlyList<ProcessDefinition> _definitions;
    private readonly string _definitionError;
    private readonly TimeSpan _interval;
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private CancellationTokenSource? _stop;
    private Task? _run;

    public OpcUaTelemetrySource(IOpcUaEquipmentClient client, IReadOnlyList<ProcessDefinition> definitions,
        string definitionError = "", TimeSpan? interval = null)
    {
        _client = client; _definitions = definitions; _definitionError = definitionError;
        _interval = interval ?? TimeSpan.FromSeconds(1);
        if (_interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        if (definitionError.Length == 0) SqliteProcessDefinitionRepository.Validate(definitions);
    }

    public event EventHandler<TelemetrySample>? SampleReceived;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_run is not null) return Task.CompletedTask;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _run = Task.Run(() => RunAsync(_stop.Token));
        return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                SampleReceived?.Invoke(this, Capture(DateTimeOffset.Now));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public TelemetrySample Capture(DateTimeOffset timestamp)
    {
        var snapshot = _client.Snapshot();
        var group = snapshot.Groups.GetValueOrDefault(EquipmentGroups.Process);
        var values = Enumerable.Repeat(double.NaN, 7).ToArray();
        if (_definitionError.Length == 0 && snapshot.IsConnected && group is not null)
            foreach (var definition in _definitions)
                if (group.Points.TryGetValue(definition.Address, out var point)
                    && point.Epoch == snapshot.Epoch && point.Quality == AlarmQuality.Good)
                {
                    var value = point.Value switch { float f => (double)f, double d => d, _ => double.NaN };
                    if (double.IsFinite(value)) values[definition.Id] = value;
                }
        var quality = values.All(double.IsFinite) ? "Good"
            : _definitionError.Length > 0 ? "定义读取失败：" + _definitionError
            : !snapshot.IsConnected ? "PLC未连接，过程数据缺失"
            : group?.Message ?? "未发现EQ_Process";
        if (values.Any(v => !double.IsFinite(v)) && quality == "Good") quality = "过程点质量无效";
        if (snapshot.IsSimulated) quality = "Simulation:" + quality;
        return new(timestamp, _sessionId, "", "", values[0], values[1], values[2], values[3],
            values[4], values[5], values[6], quality);
    }

    public async ValueTask DisposeAsync()
    {
        if (_stop is null) return;
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_run is not null) await _run.ConfigureAwait(false);
        _stop.Dispose(); _stop = null;
    }
}
