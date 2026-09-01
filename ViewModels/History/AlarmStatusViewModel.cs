using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Small_square_cavity_coating_machine.Models.History;
using Small_square_cavity_coating_machine.Services.Alarms;
using Small_square_cavity_coating_machine.Services.History;
using System.Collections.ObjectModel;

namespace Small_square_cavity_coating_machine.ViewModels.History;

public sealed partial class AlarmStatusViewModel : ObservableObject, IDisposable
{
    private readonly IAlarmMonitorService _monitor;
    private readonly IAlarmLogRepository _repository;
    private readonly IUiDispatcher _dispatcher;
    private readonly IAlarmNavigationService _navigation;
    private bool _disposed;
    private int _refreshPending;

    public AlarmStatusViewModel(IAlarmMonitorService monitor, IAlarmLogRepository repository,
        IUiDispatcher dispatcher, IAlarmNavigationService navigation)
    {
        _monitor = monitor;
        _repository = repository;
        _dispatcher = dispatcher;
        _navigation = navigation;
        monitor.StateChanged += OnChanged;
        repository.StorageStatusChanged += OnChanged;
        Refresh();
    }

    public ObservableCollection<AlarmLogRecord> ActiveAlarms { get; } = [];
    [ObservableProperty] private string summaryText = "报警状态未确认";
    [ObservableProperty] private string detailText = string.Empty;
    [ObservableProperty] private string indicatorColor = "#607D8B";
    [ObservableProperty] private string storageError = string.Empty;
    [ObservableProperty] private int activeCount;

    [RelayCommand] private void ShowHistory()
    {
        _navigation.ShowAlarmHistory();
    }

    private void OnChanged(object? sender, EventArgs args)
    {
        if (Interlocked.Exchange(ref _refreshPending, 1) != 0) return;
        _dispatcher.Post(() => { Interlocked.Exchange(ref _refreshPending, 0); if (!_disposed) Refresh(); });
    }

    private void Refresh()
    {
        var state = _monitor.Snapshot();
        ActiveAlarms.Clear();
        foreach (var record in state.ActiveAlarms) ActiveAlarms.Add(record);
        ActiveCount = ActiveAlarms.Count;
        StorageError = _repository.StorageError;
        var first = ActiveAlarms.FirstOrDefault();
        var prefix = state.IsSimulated ? "[模拟] " : string.Empty;
        SummaryText = prefix + (first is not null ? $"⚠ 当前{ActiveCount}条｜{first.Severity}：{first.Message}"
            : state.AllSignalsKnown ? "无活动报警" : "报警状态未确认");
        if (first is not null && !state.AllSignalsKnown) SummaryText += "｜状态未确认";
        if (StorageError.Length > 0) SummaryText += "｜历史记录异常";
        DetailText = $"{state.ConnectionMessage}\n{(state.AllSignalsKnown ? "所有报警信号有效" : "部分或全部报警信号状态未确认")}\n{StorageError}".Trim();
        IndicatorColor = first?.Priority == 7 ? "#C62828" : first?.Priority == 6 ? "#B45A00"
            : !state.AllSignalsKnown || StorageError.Length > 0 ? "#607D8B" : "#287A46";
    }

    public void Dispose()
    {
        _disposed = true;
        _monitor.StateChanged -= OnChanged;
        _repository.StorageStatusChanged -= OnChanged;
    }
}
