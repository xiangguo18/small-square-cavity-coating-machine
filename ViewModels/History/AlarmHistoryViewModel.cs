using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Small_square_cavity_coating_machine.Models.History;
using Small_square_cavity_coating_machine.Services.Alarms;
using Small_square_cavity_coating_machine.Services.History;
using System.Collections.ObjectModel;

namespace Small_square_cavity_coating_machine.ViewModels.History;

public sealed record AlarmHistoryRow(AlarmLogRecord Record, bool IsLiveNew, bool IsOutsideRange)
{
    public string RowNote => string.Join("；", new[]
    {
        IsOutsideRange ? "实时新增·范围外" : IsLiveNew ? "实时新增" : string.Empty,
        Record.TimingNote, Record.RecoveryNote
    }.Where(text => text.Length > 0));
}

public sealed partial class AlarmHistoryViewModel : HistoryLogViewModelBase, IDisposable
{
    private readonly IAlarmLogRepository _repository;
    private readonly IUiDispatcher _dispatcher;
    private readonly HashSet<Guid> _known = [];
    private readonly HashSet<Guid> _queryIds = [];
    private readonly HashSet<Guid> _liveIds = [];
    private DateTimeOffset _start;
    private DateTimeOffset _end;
    private bool _disposed;

    public AlarmHistoryViewModel(IAlarmLogRepository repository, IUiDispatcher dispatcher,
        AlarmSimulationViewModel? simulation = null)
    {
        _repository = repository;
        _dispatcher = dispatcher;
        Simulation = simulation;
        repository.RecordChanged += OnRecordChanged;
        repository.StorageStatusChanged += OnStorageChanged;
        ReturnToLive();
        StorageError = repository.StorageError;
    }

    public AlarmSimulationViewModel? Simulation { get; }
    public bool IsSimulation => Simulation is not null;
    public ObservableCollection<AlarmHistoryRow> Records { get; } = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeText))]
    private bool isLiveMode = true;
    [ObservableProperty] private string storageError = string.Empty;
    public string ModeText => IsLiveMode ? "实时模式" : "历史模式（同时接收新报警）";

    [RelayCommand]
    protected override void QueryCore()
    {
        if (!TryBuildRange(out var start, out var end)) return;
        IsLiveMode = false;
        _start = start;
        _end = end;
        ResetRecords();
    }

    [RelayCommand]
    private void ReturnToLive()
    {
        IsLiveMode = true;
        ResetRecords();
    }

    private void ResetRecords()
    {
        // One repository snapshot supplies both rows and known IDs, avoiding a query/event race.
        var all = _repository.Query(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
        var liveStart = new DateTimeOffset(DateTime.Today.AddDays(-6));
        var snapshot = all.Where(record => IsLiveMode
            ? record.OccurredAt >= liveStart || !record.ClearedAt.HasValue
            : record.OccurredAt >= _start && record.OccurredAt <= _end).ToArray();
        _known.Clear();
        foreach (var record in all) _known.Add(record.OccurrenceId);
        _queryIds.Clear();
        _liveIds.Clear();
        Records.Clear();
        foreach (var record in snapshot)
        {
            _queryIds.Add(record.OccurrenceId);
            Records.Add(new AlarmHistoryRow(record, false, false));
        }
        UpdateCount();
    }

    private void OnRecordChanged(object? sender, AlarmLogRecord record) => _dispatcher.Post(() =>
    {
        if (_disposed) return;
        // A queued UI callback may run after a manual query: always use the latest version.
        var latest = _repository.Query(record.OccurredAt, record.OccurredAt)
            .FirstOrDefault(r => r.OccurrenceId == record.OccurrenceId) ?? record;
        var isNew = _known.Add(record.OccurrenceId);
        var index = Records.ToList().FindIndex(row => row.Record.OccurrenceId == record.OccurrenceId);
        if (!isNew && index < 0) return;
        if (isNew) _liveIds.Add(record.OccurrenceId);
        var live = _liveIds.Contains(record.OccurrenceId);
        var row = new AlarmHistoryRow(latest, live,
            !IsLiveMode && live && (latest.OccurredAt < _start || latest.OccurredAt > _end));
        if (index >= 0) Records[index] = row;
        else
        {
            var insert = 0;
            while (insert < Records.Count && ComesBefore(Records[insert], row)) insert++;
            Records.Insert(insert, row);
        }
        UpdateCount();
    });

    private bool ComesBefore(AlarmHistoryRow left, AlarmHistoryRow right)
    {
        if (!IsLiveMode && left.IsLiveNew != right.IsLiveNew) return left.IsLiveNew;
        return left.Record.OccurredAt >= right.Record.OccurredAt;
    }

    private void UpdateCount() => StatusMessage = IsLiveMode
        ? $"实时记录 {Records.Count:N0} 条（近7天及未清除报警），自动更新。"
        : $"查询结果 {_queryIds.Count:N0} 条；实时新增 {_liveIds.Count:N0} 条（范围外 {Records.Count(r => r.IsOutsideRange):N0} 条）。";

    private void OnStorageChanged(object? sender, EventArgs args) => _dispatcher.Post(() =>
    { if (!_disposed) StorageError = _repository.StorageError; });

    public void Dispose()
    {
        _disposed = true;
        _repository.RecordChanged -= OnRecordChanged;
        _repository.StorageStatusChanged -= OnStorageChanged;
    }
}
