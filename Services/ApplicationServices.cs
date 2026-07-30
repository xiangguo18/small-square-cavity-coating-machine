using Small_square_cavity_coating_machine.Models.History;
using Small_square_cavity_coating_machine.Services.History;
using Small_square_cavity_coating_machine.ViewModels;
using Small_square_cavity_coating_machine.ViewModels.History;
using System.Windows.Threading;

namespace Small_square_cavity_coating_machine.Services;

/// <summary>
/// 应用组合根。后续接入 OPC UA 和 SQLite 时在此替换对应接口实现。
/// </summary>
public sealed class ApplicationServices : IDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ITelemetrySource _telemetrySource;
    private bool _isStarted;

    public ApplicationServices(Dispatcher dispatcher)
    {
        SessionTrendStore = new SessionTrendStore();
        OperationLogRepository = new InMemoryOperationLogRepository();
        AlarmLogRepository = new InMemoryAlarmLogRepository();
        var trendFileService = new CsvTrendFileService();
        var fileDialogService = new HistoryFileDialogService();

        SeedSimulationHistory();

        ControlViewModel = new ControlViewModel(OperationLogRepository);
        var liveTrend = new LiveTrendViewModel(
            dispatcher,
            SessionTrendStore,
            trendFileService,
            fileDialogService);
        var processTrend = new ProcessTrendViewModel(
            dispatcher,
            trendFileService,
            fileDialogService);
        var operationHistory = new OperationHistoryViewModel(OperationLogRepository);
        var alarmHistory = new AlarmHistoryViewModel(AlarmLogRepository);
        HistoryViewModel = new HistoryViewModel(
            liveTrend,
            processTrend,
            operationHistory,
            alarmHistory);

        _telemetrySource = new MockTelemetrySource(TimeSpan.FromSeconds(1));
        _telemetrySource.SampleReceived += (_, sample) => SessionTrendStore.Append(sample);
    }

    public ISessionTrendStore SessionTrendStore { get; }

    public IOperationLogRepository OperationLogRepository { get; }

    public IAlarmLogRepository AlarmLogRepository { get; }

    public ControlViewModel ControlViewModel { get; }

    public HistoryViewModel HistoryViewModel { get; }

    public Task StartAsync()
    {
        if (_isStarted)
        {
            return Task.CompletedTask;
        }

        _isStarted = true;
        return _telemetrySource.StartAsync(_shutdown.Token);
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _telemetrySource.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _shutdown.Dispose();
    }

    private void SeedSimulationHistory()
    {
        var now = DateTimeOffset.Now;
        OperationLogRepository.Add(new OperationLogRecord(
            now.AddMinutes(-12),
            "模拟操作员",
            "加热",
            "设置目标温度",
            "200 ℃",
            true,
            false,
            string.Empty,
            true));
        OperationLogRepository.Add(new OperationLogRecord(
            now.AddHours(-5),
            "模拟操作员",
            "电源1",
            "启动电源",
            string.Empty,
            true,
            false,
            string.Empty,
            true));
        OperationLogRepository.Add(new OperationLogRecord(
            now.AddDays(-2),
            "模拟操作员",
            "前级阀",
            "打开阀门",
            string.Empty,
            false,
            true,
            "模拟互锁条件未满足",
            true));

        AlarmLogRepository.Add(new AlarmLogRecord(
            now.AddHours(-3),
            "警告",
            "模拟通信",
            "[模拟] PLC通信短时中断",
            now.AddHours(-3).AddMinutes(2),
            true));
        AlarmLogRepository.Add(new AlarmLogRecord(
            now.AddDays(-1),
            "提示",
            "模拟真空系统",
            "[模拟] 真空采样值暂不可用",
            now.AddDays(-1).AddMinutes(1),
            true));
    }
}
