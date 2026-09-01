using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Small_square_cavity_coating_machine.Models.History;
using Small_square_cavity_coating_machine.Services.Alarms;
using Small_square_cavity_coating_machine.Services.History;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Small_square_cavity_coating_machine.ViewModels.History;

public abstract partial class HistoryLogViewModelBase : ObservableObject
{
    [ObservableProperty]
    private DateTime? startDate;

    [ObservableProperty]
    private string startTimeText = string.Empty;

    [ObservableProperty]
    private DateTime? endDate;

    [ObservableProperty]
    private string endTimeText = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    protected HistoryLogViewModelBase()
    {
        ResetRange();
    }

    [RelayCommand]
    protected void ClearFilters()
    {
        ResetRange();
        QueryCore();
    }

    protected abstract void QueryCore();

    protected bool TryBuildRange(out DateTimeOffset start, out DateTimeOffset end)
    {
        start = default;
        end = default;
        if (!StartDate.HasValue || !EndDate.HasValue)
        {
            StatusMessage = "请选择开始日期和结束日期。";
            return false;
        }

        if (!TimeSpan.TryParseExact(
                StartTimeText,
                @"hh\:mm\:ss",
                CultureInfo.InvariantCulture,
                out var startTime) ||
            !TimeSpan.TryParseExact(
                EndTimeText,
                @"hh\:mm\:ss",
                CultureInfo.InvariantCulture,
                out var endTime))
        {
            StatusMessage = "时间格式应为 HH:mm:ss。";
            return false;
        }

        var startLocal = DateTime.SpecifyKind(StartDate.Value.Date + startTime, DateTimeKind.Local);
        var endLocal = DateTime.SpecifyKind(EndDate.Value.Date + endTime, DateTimeKind.Local);
        start = new DateTimeOffset(startLocal);
        end = new DateTimeOffset(endLocal);

        if (start > end)
        {
            StatusMessage = "开始时间不能晚于结束时间。";
            return false;
        }

        return true;
    }

    private void ResetRange()
    {
        var today = DateTime.Today;
        StartDate = today.AddDays(-6);
        StartTimeText = "00:00:00";
        EndDate = today;
        EndTimeText = "23:59:59";
        StatusMessage = string.Empty;
    }
}

public sealed partial class OperationHistoryViewModel : HistoryLogViewModelBase, IDisposable
{
    private readonly IOperationLogRepository _repository;

    private readonly IUiDispatcher? _dispatcher;
    private bool _disposed;
    private int _pending;
    public OperationHistoryViewModel(IOperationLogRepository repository, IUiDispatcher? dispatcher = null)
    {
        _repository = repository; _dispatcher = dispatcher;
        QueryCore();
        _repository.RecordAdded += OnRecordAdded;
    }

    private void OnRecordAdded(object? sender, OperationLogRecord record)
    {
        if (Interlocked.Exchange(ref _pending, 1) != 0) return;
        void Update() { Interlocked.Exchange(ref _pending, 0); if (!_disposed) QueryCore(); }
        if (_dispatcher is null) Update(); else _dispatcher.Post(Update);
    }
    public void Dispose() { _disposed = true; _repository.RecordAdded -= OnRecordAdded; }
    public ObservableCollection<OperationLogRecord> Records { get; } = [];

    [RelayCommand]
    protected override void QueryCore()
    {
        if (!TryBuildRange(out var start, out var end))
        {
            return;
        }

        Records.Clear();
        foreach (var record in _repository.Query(start, end))
        {
            Records.Add(record);
        }

        StatusMessage = Records.Count == 0
            ? "当前时间范围内没有操作记录。"
            : $"共 {Records.Count:N0} 条操作记录，已按时间倒序排列。";
    }
}
