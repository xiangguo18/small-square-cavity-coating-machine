using Small_square_cavity_coating_machine.Models.History;

namespace Small_square_cavity_coating_machine.Services.History;

/// <summary>
/// 离线界面开发使用的确定性模拟数据源，不包含任何安全阈值判断。
/// </summary>
public sealed class MockTelemetrySource : ITelemetrySource
{
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private readonly TimeSpan _samplingInterval;
    private readonly TimeSpan _simulatedTimeStep;
    private readonly DateTimeOffset _simulationStart;
    private Task? _samplingTask;
    private int _sampleIndex;

    public MockTelemetrySource(
        TimeSpan samplingInterval,
        TimeSpan? simulatedTimeStep = null)
    {
        if (samplingInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(samplingInterval));
        }

        var timeStep = simulatedTimeStep ?? samplingInterval;
        if (timeStep <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(simulatedTimeStep));
        }

        _samplingInterval = samplingInterval;
        _simulatedTimeStep = timeStep;
        _simulationStart = DateTimeOffset.Now;
    }

    public event EventHandler<TelemetrySample>? SampleReceived;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_samplingTask is null)
        {
            _samplingTask = RunAsync(cancellationToken);
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_samplingTask is null)
        {
            return;
        }

        try
        {
            await _samplingTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_samplingInterval);

        PublishSample();
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            PublishSample();
        }
    }

    private void PublishSample()
    {
        var sampleIndex = _sampleIndex++;
        var phase = sampleIndex / 18d;
        var simulatedTimestamp = _simulationStart.AddTicks(_simulatedTimeStep.Ticks * sampleIndex);
        var sample = new TelemetrySample(
            simulatedTimestamp,
            _sessionId,
            string.Empty,
            string.Empty,
            13.6 + Math.Sin(phase) * 0.65,
            12.9 + Math.Sin(phase * 0.82 + 0.7) * 0.52,
            480 + Math.Sin(phase * 1.1) * 7.5,
            1.8 + Math.Sin(phase * 1.15 + 0.4) * 0.12,
            472 + Math.Sin(phase * 0.92 + 1.2) * 6.2,
            1.65 + Math.Sin(phase * 0.88 + 0.9) * 0.1,
            186.5 + Math.Sin(phase * 0.28) * 4.8,
            "Simulation");

        SampleReceived?.Invoke(this, sample);
    }
}
