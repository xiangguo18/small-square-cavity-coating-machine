using Opc.Ua;
using Small_square_cavity_coating_machine.Models.Alarms;

namespace Small_square_cavity_coating_machine.Services.Alarms;

public enum AlarmConnectionPhase { Disconnected, Connecting, Connected, Failed }
public sealed record AlarmConnectionStatus(string EndpointUrl, AlarmConnectionPhase Phase, string Message);
public interface IAlarmConnectionController
{
    bool IsWriteInProgress => false;
    AlarmConnectionStatus Status { get; }
    event EventHandler? StateChanged;
    Task ApplyEndpointAsync(string endpoint, CancellationToken token = default);
}

/// <summary>One stable source for the monitor. Reconfiguration never replaces event subscribers.</summary>
public sealed class OpcUaAlarmSignalSource : IAlarmSignalSource, IAlarmConnectionController
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly Lock _events = new();
    private readonly IAlarmConnectionSettingsStore _store;
    private readonly IAlarmArraySessionFactory _factory;
    private OpcUaAlarmOptions _options;
    private AlarmConnectionStatus _status;
    private string? _loadError;
    private IReadOnlyList<AlarmDefinition>? _definitions;
    private CancellationToken _shutdown;
    private CancellationTokenSource? _stop;
    private Task? _run;
    private bool _disposed;
    private bool _connected;

    public OpcUaAlarmSignalSource(IAlarmConnectionSettingsStore store, IAlarmArraySessionFactory factory)
    {
        _store = store;
        _factory = factory;
        try { _options = store.Load(); }
        catch (Exception ex) { _options = new OpcUaAlarmOptions(); _loadError = $"连接配置读取失败：{ex.Message}"; }
        _status = new(_options.EndpointUrl, _loadError is null ? AlarmConnectionPhase.Disconnected : AlarmConnectionPhase.Failed,
            _loadError ?? "PLC未连接");
    }

    public bool IsSimulated => false;
    public AlarmConnectionStatus Status { get { lock (_events) return _status; } }
    public event EventHandler? StateChanged;
    public event EventHandler<AlarmSignal>? SignalReceived;
    public event EventHandler<AlarmConnection>? ConnectionChanged;

    public async Task StartAsync(IReadOnlyList<AlarmDefinition> definitions, CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_definitions is not null) return;
            _definitions = definitions;
            _shutdown = cancellationToken;
            if (_loadError is not null) SetStatus(AlarmConnectionPhase.Failed, _loadError);
            else if (_options.Enabled) StartRun();
            else SetStatus(AlarmConnectionPhase.Disconnected, "PLC连接已禁用，可在左下角保存并连接");
        }
        finally { _lifecycle.Release(); }
    }

    public async Task ApplyEndpointAsync(string endpoint, CancellationToken token = default)
    {
        endpoint = OpcUaAlarmOptions.ValidateEndpoint(endpoint);
        await _lifecycle.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _shutdown.ThrowIfCancellationRequested();
            await StopRunAsync().ConfigureAwait(false);
            var next = _options with { EndpointUrl = endpoint, Enabled = true };
            try { await _store.SaveAsync(next, token).ConfigureAwait(false); }
            catch (Exception ex)
            {
                SetStatus(AlarmConnectionPhase.Failed, $"连接配置保存失败，未切换地址：{ex.Message}");
                throw;
            }
            _options = next;
            _loadError = null;
            if (_definitions is not null) StartRun();
            else SetStatus(AlarmConnectionPhase.Disconnected, "地址已保存，等待应用启动连接");
        }
        finally { _lifecycle.Release(); }
    }

    private void StartRun()
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(_shutdown);
        var token = _stop.Token;
        var options = _options;
        SetStatus(AlarmConnectionPhase.Connecting, "正在连接PLC并查找EQ_Alarm数组");
        _run = Task.Run(() => RunAsync(options, token), CancellationToken.None);
    }

    private async Task StopRunAsync()
    {
        if (_stop is not null)
        {
            await _stop.CancelAsync().ConfigureAwait(false);
            // Notification handlers check this token while holding the same event gate.
            SetStatus(AlarmConnectionPhase.Disconnected, "PLC未连接，正在停止旧订阅");
            if (_run is not null) await _run.ConfigureAwait(false);
            _stop.Dispose();
            _stop = null;
            _run = null;
        }
    }

    private async Task RunAsync(OpcUaAlarmOptions options, CancellationToken token)
    {
        try
        {
            options.Validate();
            var decoder = new AlarmArrayDecoder(_definitions!);
            while (!token.IsCancellationRequested)
            {
                try
                {
                    SetStatus(AlarmConnectionPhase.Connecting, "正在连接PLC并查找EQ_Alarm数组");
                    await using var session = await _factory.ConnectAsync(options, token).ConfigureAwait(false);
                    await session.FindArrayAsync(options.ArrayBrowseName, token).ConfigureAwait(false);
                    var initial = await session.ReadAsync(token).ConfigureAwait(false);
                    var pending = new Queue<DataValue>();
                    var ready = false;
                    var accepting = true;
                    try
                    {
                        await session.SubscribeAsync(value =>
                        {
                            lock (_events)
                            {
                                if (token.IsCancellationRequested || !accepting) return;
                                if (!ready)
                                {
                                    if (pending.Count >= 1000) { accepting = false; return; }
                                    pending.Enqueue(value);
                                }
                                else Publish(decoder, value);
                            }
                        }, token).ConfigureAwait(false);
                        lock (_events)
                        {
                            token.ThrowIfCancellationRequested();
                            if (!accepting) throw new InvalidOperationException("初始报警通知缓冲溢出");
                            SetStatus(AlarmConnectionPhase.Connected, "PLC已连接（报警只读）");
                            Publish(decoder, initial);
                            while (pending.TryDequeue(out var value)) Publish(decoder, value);
                            ready = true;
                        }
                        await session.WaitForDisconnectAsync(token).ConfigureAwait(false);
                    }
                    finally
                    {
                        lock (_events)
                        {
                            accepting = false;
                            SetStatus(AlarmConnectionPhase.Disconnected, "PLC报警订阅已断开");
                        }
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex) { SetStatus(AlarmConnectionPhase.Failed, $"PLC报警连接失败：{ex.Message}"); }
                await Task.Delay(options.ReconnectDelayMs, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { SetStatus(AlarmConnectionPhase.Failed, $"报警配置无效：{ex.Message}"); }
        finally { if (token.IsCancellationRequested) SetStatus(AlarmConnectionPhase.Disconnected, "PLC未连接"); }
    }

    private void Publish(AlarmArrayDecoder decoder, DataValue value)
    {
        // Caller holds _events; all array elements share one observation timestamp.
        var sample = decoder.Decode(value, DateTimeOffset.Now);
        foreach (var signal in sample.Signals) SignalReceived?.Invoke(this, signal);
        SetStatus(AlarmConnectionPhase.Connected, sample.Error.Length == 0
            ? "PLC已连接（报警只读）" : sample.Error);
    }

    private void SetStatus(AlarmConnectionPhase phase, string message)
    {
        lock (_events)
        {
            var next = new AlarmConnectionStatus(_options.EndpointUrl, phase, message);
            var connected = phase == AlarmConnectionPhase.Connected;
            if (_connected != connected)
            {
                _connected = connected;
                ConnectionChanged?.Invoke(this, new AlarmConnection(connected, message));
            }
            if (_status == next) return;
            _status = next;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            await StopRunAsync().ConfigureAwait(false);
        }
        finally { _lifecycle.Release(); }
    }
}
