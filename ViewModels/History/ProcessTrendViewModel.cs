using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Small_square_cavity_coating_machine.Models.History;
using Small_square_cavity_coating_machine.Services.History;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;

namespace Small_square_cavity_coating_machine.ViewModels.History;

public sealed partial class ProcessTrendViewModel : TrendChartViewModel, IDisposable
{
    private readonly ITrendFileService _files;
    private readonly IHistoryFileDialogService _dialogs;
    private readonly IProcessTrendRecorder? _recorder;
    private IReadOnlyList<TelemetrySample> _loaded = [];
    private Guid? _loadedRunId;
    private bool _simulation, _disposed, _refreshing;
    private int _loadVersion, _queryVersion, _refreshPending;

    [ObservableProperty] private DateTime? startDate = DateTime.Today.AddDays(-6);
    [ObservableProperty] private DateTime? endDate = DateTime.Today;
    [ObservableProperty] private ProcessTrendRun? selectedRun;
    public ObservableCollection<ProcessTrendRun> Runs { get; } = [];

    public ProcessTrendViewModel(Dispatcher dispatcher, ITrendFileService fileService,
        IHistoryFileDialogService fileDialogService, IProcessTrendRecorder? recorder = null)
        : base(dispatcher, isLive: false)
    {
        _files = fileService; _dialogs = fileDialogService; _recorder = recorder;
        ActionCommand = new AsyncRelayCommand(ImportAsync);
        QueryCommand = new AsyncRelayCommand(QueryAsync);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => _loadedRunId.HasValue || _loaded.Count > 0);
        StatusMessage = "按日期查询并选择工艺任务，或导入已保存的CSV。";
        if (_recorder is not null)
        {
            _recorder.Changed += OnRecorderChanged;
            _ = QueryAsync();
        }
    }

    public override string ActionText => "导入 CSV";
    public override IAsyncRelayCommand ActionCommand { get; }
    public IAsyncRelayCommand QueryCommand { get; }
    public IAsyncRelayCommand ExportCommand { get; }
    public override bool IsSimulationMode => _simulation;

    partial void OnSelectedRunChanged(ProcessTrendRun? value)
    {
        if (!_refreshing) _ = LoadRunAsync(value);
    }

    private void OnRecorderChanged(object? sender, EventArgs e)
    {
        if (_disposed || Interlocked.Exchange(ref _refreshPending, 1) != 0) return;
        OnUi(() => { Interlocked.Exchange(ref _refreshPending, 0); if (!_disposed) _ = QueryAsync(); });
    }

    private async Task QueryAsync()
    {
        if (_recorder is null || _disposed) return;
        var version = ++_queryVersion;
        if (StartDate is null || EndDate is null || StartDate.Value.Date > EndDate.Value.Date || EndDate.Value.Date == DateTime.MaxValue.Date)
        { StatusMessage = "请选择有效的起止日期，开始日期不能晚于结束日期。"; return; }
        var start = new DateTimeOffset(StartDate.Value.Date);
        var end = new DateTimeOffset(EndDate.Value.Date.AddDays(1));
        try
        {
            var runs = await _recorder.QueryAsync(start, end).ConfigureAwait(false);
            await OnUiAsync(() =>
            {
                if (_disposed || version != _queryVersion) return;
                var previous = SelectedRun;
                _refreshing = true;
                try
                {
                    Runs.Clear();
                    foreach (var run in runs) Runs.Add(run);
                    SelectedRun = runs.FirstOrDefault(r => r.RunId == previous?.RunId);
                }
                finally { _refreshing = false; }
                RecordingError = _recorder.RecordingError;
                if (previous is not null && (SelectedRun is null || previous != SelectedRun)) _ = LoadRunAsync(SelectedRun);
            }).ConfigureAwait(false);
        }
        catch (Exception ex) { await OnUiAsync(() => { if (version == _queryVersion) StatusMessage = "查询失败：" + ex.Message; }); }
    }

    private void ClearLoaded()
    {
        _loaded = []; _loadedRunId = null;
        _simulation = false; OnPropertyChanged(nameof(IsSimulationMode));
        CurrentFileName = "";
        LoadSamples([]);
        ExportCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadRunAsync(ProcessTrendRun? run)
    {
        var version = ++_loadVersion;
        ClearLoaded();
        if (run is null || _recorder is null) return;
        StatusMessage = "正在加载工艺曲线……";
        try
        {
            var samples = await _recorder.LoadSamplesAsync(run.RunId).ConfigureAwait(false);
            var display = await Task.Run(() => Downsample(samples, 10_000)).ConfigureAwait(false);
            await OnUiAsync(() =>
            {
                if (_disposed || version != _loadVersion) return;
                _loadedRunId = run.RunId;
                SetLoaded(samples, display, run.RecipeName, run.IsSimulated);
                StatusMessage += $" · {run.Status}" + (run.FailureReason.Length == 0 ? "" : " · " + run.FailureReason);
                RecordingError = _recorder.RecordingError;
            }).ConfigureAwait(false);
        }
        catch (Exception ex) { await OnUiAsync(() => { if (version == _loadVersion) StatusMessage = "加载失败：" + ex.Message; }); }
    }

    private async Task ImportAsync()
    {
        var path = _dialogs.SelectOpenPath();
        if (path is null) return;
        _refreshing = true;
        SelectedRun = null;
        _refreshing = false;
        var version = ++_loadVersion;
        ClearLoaded();
        StatusMessage = "正在读取CSV……";
        try
        {
            var samples = await _files.LoadAsync(path).ConfigureAwait(false);
            var ordered = samples.OrderBy(s => s.Timestamp).ToArray();
            var display = await Task.Run(() => Downsample(ordered, 10_000)).ConfigureAwait(false);
            await OnUiAsync(() =>
            {
                if (_disposed || version != _loadVersion) return;
                SetLoaded(ordered, display, Path.GetFileName(path), ordered.Any(s => s.DataQuality.StartsWith("Simulation", StringComparison.Ordinal)));
            }).ConfigureAwait(false);
        }
        catch (Exception ex) { await OnUiAsync(() => { if (version == _loadVersion) StatusMessage = "CSV读取失败：" + ex.Message; }); }
    }

    private void SetLoaded(IReadOnlyList<TelemetrySample> samples, IReadOnlyList<TelemetrySample> display, string name, bool simulated)
    {
        _loaded = samples; _simulation = simulated;
        OnPropertyChanged(nameof(IsSimulationMode));
        LoadSamples(display);
        CurrentFileName = name;
        StatusMessage = $"已载入 {samples.Count:N0} 条数据，显示 {display.Count:N0} 条（保留缺失断点）";
        ExportCommand.NotifyCanExecuteChanged();
    }

    private async Task ExportAsync()
    {
        var id = _loadedRunId;
        var samples = _loaded;
        var path = _dialogs.SelectSavePath($"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_process-curves.csv");
        if (path is null) return;
        try
        {
            if (id.HasValue && _recorder is not null) await _recorder.ExportAsync(id.Value, path);
            else await _files.SaveAsync(path, samples);
            await OnUiAsync(() => StatusMessage = "导出成功：" + path);
        }
        catch (Exception ex) { await OnUiAsync(() => StatusMessage = "导出失败：" + ex.Message); }
    }

    public static IReadOnlyList<TelemetrySample> Downsample(IReadOnlyList<TelemetrySample> samples, int maximumCount)
    {
        if (maximumCount < 2) throw new ArgumentOutOfRangeException(nameof(maximumCount));
        if (samples.Count <= maximumCount) return samples;
        var indices = new SortedSet<int> { 0, samples.Count - 1 };
        var step = (int)Math.Ceiling(samples.Count / (double)maximumCount);
        for (var i = 0; i < samples.Count; i += step) indices.Add(i);
        for (var i = 1; i < samples.Count; i++)
            if (Validity(samples[i - 1]) != Validity(samples[i])) { indices.Add(i - 1); indices.Add(i); }
        // Gap boundaries take precedence over the display target; otherwise downsampling could invent continuity.
        return indices.Select(i => samples[i]).ToArray();
    }

    private static int Validity(TelemetrySample s)
    {
        double[] values = [s.HighVacuumPa, s.FilmHighVacuumPa, s.Power1VoltageV, s.Power1CurrentA,
            s.Power2VoltageV, s.Power2CurrentA, s.TemperatureC];
        var mask = 0;
        for (var i = 0; i < values.Length; i++) if (double.IsFinite(values[i])) mask |= 1 << i;
        return mask;
    }

    public void Dispose()
    {
        _disposed = true; ++_loadVersion; ++_queryVersion;
        if (_recorder is not null) _recorder.Changed -= OnRecorderChanged;
    }
}
