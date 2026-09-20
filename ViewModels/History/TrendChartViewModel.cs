using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Small_square_cavity_coating_machine.Models.History;
using Small_square_cavity_coating_machine.Services.History;
using System.IO;
using System.Windows.Threading;

namespace Small_square_cavity_coating_machine.ViewModels.History;

public abstract partial class TrendChartViewModel : ObservableObject
{
    private const string VacuumScientificFormat = "0.00E+00";
    private const string HighVacuumTimeAxisKey = "HighVacuumTime";
    private const string FilmVacuumTimeAxisKey = "FilmVacuumTime";
    private const string HighVacuumValueAxisKey = "HighVacuumValue";
    private const string FilmVacuumValueAxisKey = "FilmVacuumValue";
    private const string PowerTimeAxisKey = "PowerTime";
    private const string VoltageAxisKey = "Voltage";
    private const string CurrentAxisKey = "Current";
    private const string TemperatureTimeAxisKey = "TemperatureTime";
    private const string TemperatureAxisKey = "Temperature";

    private static readonly TimeSpan LiveWindow = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RefreshedLiveWindow = TimeSpan.FromMinutes(10);
    private static readonly double LiveRightMargin = LiveWindow.TotalDays * 0.02d;
    private readonly Dispatcher _dispatcher;
    private readonly bool _isLive;
    private readonly DateTimeAxis _highVacuumTimeAxis;
    private readonly DateTimeAxis _filmVacuumTimeAxis;
    private readonly DateTimeAxis _powerTimeAxis;
    private readonly DateTimeAxis _temperatureTimeAxis;
    private readonly LineSeries _highVacuumSeries;
    private readonly LineSeries _filmVacuumSeries;
    private readonly LineSeries _power1VoltageSeries;
    private readonly LineSeries _power1CurrentSeries;
    private readonly LineSeries _power2VoltageSeries;
    private readonly LineSeries _power2CurrentSeries;
    private readonly LineSeries _temperatureSeries;
    private bool _isApplyingLiveAxisRange;
    private double _liveSessionStart = double.NaN;
    private readonly double[] _refreshStarts = [double.NaN, double.NaN, double.NaN];

    private enum TrendChart
    {
        Vacuum,
        Power,
        Temperature
    }

    [ObservableProperty]
    private bool showHighVacuum = true;

    [ObservableProperty]
    private bool showFilmHighVacuum;

    [ObservableProperty]
    private bool showPower1Voltage = true;

    [ObservableProperty]
    private bool showPower1Current;

    [ObservableProperty]
    private bool showPower2Voltage = true;

    [ObservableProperty]
    private bool showPower2Current;

    [ObservableProperty]
    private bool showTemperature = true;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private string currentFileName = string.Empty;

    [ObservableProperty]
    private bool isAutoFollow = true;

    [ObservableProperty]
    private int selectedChartIndex;

    [ObservableProperty] private string acquisitionStatus = "";
    [ObservableProperty] private string recordingError = "";

    protected TrendChartViewModel(Dispatcher dispatcher, bool isLive)
    {
        _dispatcher = dispatcher;
        _isLive = isLive;

        VacuumPlotModel = new PlotModel { Title = "真空状态" };
        _highVacuumTimeAxis = CreateTimeAxis(HighVacuumTimeAxisKey, AxisPosition.Bottom, "高真空度计时间");
        _filmVacuumTimeAxis = CreateTimeAxis(FilmVacuumTimeAxisKey, AxisPosition.Top, "薄膜高真空度计时间");
        VacuumPlotModel.Axes.Add(_highVacuumTimeAxis);
        VacuumPlotModel.Axes.Add(_filmVacuumTimeAxis);
        VacuumPlotModel.Axes.Add(CreateLogarithmicValueAxis(
            HighVacuumValueAxisKey,
            AxisPosition.Left,
            "高真空度计 (Pa)",
            OxyColors.Red,
            VacuumScientificFormat));
        VacuumPlotModel.Axes.Add(CreateLogarithmicValueAxis(
            FilmVacuumValueAxisKey,
            AxisPosition.Right,
            "薄膜高真空度计 (Pa)",
            OxyColors.Blue,
            VacuumScientificFormat));
        _highVacuumSeries = CreateSeries(
            "高真空度计",
            OxyColors.Red,
            LineStyle.Solid,
            HighVacuumTimeAxisKey,
            HighVacuumValueAxisKey,
            VacuumScientificFormat);
        _filmVacuumSeries = CreateSeries(
            "薄膜高真空度计",
            OxyColors.Blue,
            LineStyle.Solid,
            FilmVacuumTimeAxisKey,
            FilmVacuumValueAxisKey,
            VacuumScientificFormat);
        VacuumPlotModel.Series.Add(_highVacuumSeries);
        VacuumPlotModel.Series.Add(_filmVacuumSeries);

        PowerPlotModel = new PlotModel { Title = "电源电压与电流" };
        _powerTimeAxis = CreateTimeAxis(PowerTimeAxisKey, AxisPosition.Bottom, "时间");
        PowerPlotModel.Axes.Add(_powerTimeAxis);
        PowerPlotModel.Axes.Add(CreateValueAxis(
            VoltageAxisKey,
            AxisPosition.Left,
            "电压 (V)",
            OxyColors.DarkRed));
        PowerPlotModel.Axes.Add(CreateValueAxis(
            CurrentAxisKey,
            AxisPosition.Right,
            "电流 (A)",
            OxyColors.DarkBlue));
        _power1VoltageSeries = CreateSeries(
            "电源1电压",
            OxyColors.Red,
            LineStyle.Solid,
            PowerTimeAxisKey,
            VoltageAxisKey);
        _power1CurrentSeries = CreateSeries(
            "电源1电流",
            OxyColors.Red,
            LineStyle.Dash,
            PowerTimeAxisKey,
            CurrentAxisKey);
        _power2VoltageSeries = CreateSeries(
            "电源2电压",
            OxyColors.Blue,
            LineStyle.Solid,
            PowerTimeAxisKey,
            VoltageAxisKey);
        _power2CurrentSeries = CreateSeries(
            "电源2电流",
            OxyColors.Blue,
            LineStyle.Dash,
            PowerTimeAxisKey,
            CurrentAxisKey);
        PowerPlotModel.Series.Add(_power1VoltageSeries);
        PowerPlotModel.Series.Add(_power1CurrentSeries);
        PowerPlotModel.Series.Add(_power2VoltageSeries);
        PowerPlotModel.Series.Add(_power2CurrentSeries);

        TemperaturePlotModel = new PlotModel { Title = "温度变化" };
        _temperatureTimeAxis = CreateTimeAxis(TemperatureTimeAxisKey, AxisPosition.Bottom, "时间");
        TemperaturePlotModel.Axes.Add(_temperatureTimeAxis);
        TemperaturePlotModel.Axes.Add(CreateValueAxis(
            TemperatureAxisKey,
            AxisPosition.Left,
            "温度 (℃)",
            OxyColors.Red));
        _temperatureSeries = CreateSeries(
            "温度",
            OxyColors.Red,
            LineStyle.Solid,
            TemperatureTimeAxisKey,
            TemperatureAxisKey);
        TemperaturePlotModel.Series.Add(_temperatureSeries);

        if (_isLive)
        {
            foreach (var axis in AllTimeAxes())
            {
#pragma warning disable CS0618 // OxyPlot 2.2 只有 AxisChanged 能区分用户的 Zoom/Pan。
                axis.AxisChanged += OnLiveTimeAxisChanged;
#pragma warning restore CS0618
            }
        }

        ApplySeriesVisibility();
    }

    public PlotModel VacuumPlotModel { get; }

    public PlotModel PowerPlotModel { get; }

    public PlotModel TemperaturePlotModel { get; }

    public abstract string ActionText { get; }

    public abstract IAsyncRelayCommand ActionCommand { get; }

    public abstract bool IsSimulationMode { get; }

    public bool IsLiveFollowPaused => _isLive && !IsAutoFollow;

    public bool IsAutomaticDisplayAvailable => _isLive;

    protected void LoadSamples(IEnumerable<TelemetrySample> samples)
    {
        ClearSeries();
        foreach (var sample in samples.OrderBy(sample => sample.Timestamp))
        {
            AddSampleCore(sample);
        }

        if (_isLive)
        {
            if (IsAutoFollow && _highVacuumSeries.Points.Count > 0)
            {
                ApplyLiveAxes();
            }
        }
        else
        {
            ResetArchiveAxes();
            ResetValueAxes();
        }

        InvalidatePlots();
    }

    protected void OnUi(Action action)
    {
        if (_dispatcher.HasShutdownStarted) return;
        if (_dispatcher.CheckAccess()) action();
        else _ = _dispatcher.InvokeAsync(action);
    }

    protected Task OnUiAsync(Action action) => _dispatcher.HasShutdownStarted
        ? Task.CompletedTask : _dispatcher.InvokeAsync(action).Task;

    protected void AppendSample(TelemetrySample sample)
    {
        if (_dispatcher.CheckAccess())
        {
            AppendSampleOnUiThread(sample);
            return;
        }

        _ = _dispatcher.InvokeAsync(() => AppendSampleOnUiThread(sample));
    }

    partial void OnShowHighVacuumChanged(bool value) => ApplySeriesVisibility();

    partial void OnShowFilmHighVacuumChanged(bool value) => ApplySeriesVisibility();

    partial void OnShowPower1VoltageChanged(bool value) => ApplySeriesVisibility();

    partial void OnShowPower1CurrentChanged(bool value) => ApplySeriesVisibility();

    partial void OnShowPower2VoltageChanged(bool value) => ApplySeriesVisibility();

    partial void OnShowPower2CurrentChanged(bool value) => ApplySeriesVisibility();

    partial void OnShowTemperatureChanged(bool value) => ApplySeriesVisibility();

    partial void OnIsAutoFollowChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLiveFollowPaused));
        if (value && _isLive && _highVacuumSeries.Points.Count > 0)
        {
            ApplyLiveAxes();
            InvalidatePlots();
        }
    }

    [RelayCommand]
    private void ResumeLiveFollow()
    {
        if (!_isLive)
        {
            return;
        }

        ResetValueAxes();
        IsAutoFollow = true;
        if (_highVacuumSeries.Points.Count > 0)
        {
            ApplyLiveAxes();
        }

        InvalidatePlots();
    }

    [RelayCommand]
    private void RefreshCurrentChart()
    {
        if (!_isLive)
        {
            return;
        }

        var chart = SelectedChartIndex switch
        {
            1 => TrendChart.Power,
            2 => TrendChart.Temperature,
            _ => TrendChart.Vacuum
        };
        _refreshStarts[(int)chart] = DateTimeAxis.ToDouble(DateTime.Now);
        ApplyLiveAxes([chart]);
        InvalidatePlots();
    }

    private static DateTimeAxis CreateTimeAxis(string key, AxisPosition position, string title) =>
        new()
        {
            Key = key,
            Position = position,
            Title = title,
            StringFormat = "HH:mm:ss",
            IntervalType = DateTimeIntervalType.Auto,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromAColor(70, OxyColors.Gray),
            MinorGridlineColor = OxyColor.FromAColor(35, OxyColors.Gray)
        };

    private static LinearAxis CreateValueAxis(
        string key,
        AxisPosition position,
        string title,
        OxyColor color,
        string? stringFormat = null) =>
        new()
        {
            Key = key,
            Position = position,
            Title = title,
            StringFormat = stringFormat,
            AxislineColor = color,
            TextColor = color,
            TitleColor = color,
            MajorGridlineStyle = position == AxisPosition.Left ? LineStyle.Solid : LineStyle.None,
            MinorGridlineStyle = position == AxisPosition.Left ? LineStyle.Dot : LineStyle.None,
            MajorGridlineColor = OxyColor.FromAColor(70, OxyColors.Gray),
            MinorGridlineColor = OxyColor.FromAColor(35, OxyColors.Gray)
        };

    private static LogarithmicAxis CreateLogarithmicValueAxis(
        string key,
        AxisPosition position,
        string title,
        OxyColor color,
        string stringFormat) =>
        new()
        {
            Key = key,
            Position = position,
            Title = title,
            Base = 10,
            PowerPadding = true,
            StringFormat = stringFormat,
            AxislineColor = color,
            TextColor = color,
            TitleColor = color,
            MajorGridlineStyle = position == AxisPosition.Left ? LineStyle.Solid : LineStyle.None,
            MinorGridlineStyle = position == AxisPosition.Left ? LineStyle.Dot : LineStyle.None,
            MajorGridlineColor = OxyColor.FromAColor(70, OxyColors.Gray),
            MinorGridlineColor = OxyColor.FromAColor(35, OxyColors.Gray)
        };

    private static LineSeries CreateSeries(
        string title,
        OxyColor color,
        LineStyle lineStyle,
        string xAxisKey,
        string yAxisKey,
        string valueFormat = "0.###") =>
        new()
        {
            Title = title,
            Color = color,
            StrokeThickness = 2,
            LineStyle = lineStyle,
            XAxisKey = xAxisKey,
            YAxisKey = yAxisKey,
            TrackerFormatString = $"{{0}}\n时间: {{2}}\n数值: {{4:{valueFormat}}}"
        };

    private void AppendSampleOnUiThread(TelemetrySample sample)
    {
        AddSampleCore(sample);
        if (_isLive)
        {
            if (IsAutoFollow)
            {
                ApplyLiveAxes();
            }
            else
            {
                var refreshedCharts = Enum.GetValues<TrendChart>()
                    .Where(chart => double.IsFinite(_refreshStarts[(int)chart]));
                ApplyLiveAxes(refreshedCharts);
            }
        }

        InvalidatePlots();
    }

    private void AddSampleCore(TelemetrySample sample)
    {
        var timestamp = DateTimeAxis.ToDouble(sample.Timestamp.LocalDateTime);
        if (_isLive && double.IsNaN(_liveSessionStart))
        {
            _liveSessionStart = timestamp;
        }

        _highVacuumSeries.Points.Add(new DataPoint(timestamp, ToLogarithmicPlotValue(sample.HighVacuumPa)));
        _filmVacuumSeries.Points.Add(new DataPoint(timestamp, ToLogarithmicPlotValue(sample.FilmHighVacuumPa)));
        _power1VoltageSeries.Points.Add(new DataPoint(timestamp, sample.Power1VoltageV));
        _power1CurrentSeries.Points.Add(new DataPoint(timestamp, sample.Power1CurrentA));
        _power2VoltageSeries.Points.Add(new DataPoint(timestamp, sample.Power2VoltageV));
        _power2CurrentSeries.Points.Add(new DataPoint(timestamp, sample.Power2CurrentA));
        _temperatureSeries.Points.Add(new DataPoint(timestamp, sample.TemperatureC));
    }

    private static double ToLogarithmicPlotValue(double value) =>
        double.IsFinite(value) && value > 0d ? value : double.NaN;

    private void ApplyLiveAxes() => ApplyLiveAxes(Enum.GetValues<TrendChart>());

    private void ApplyLiveAxes(IEnumerable<TrendChart> charts)
    {
        var latest = _highVacuumSeries.Points.Count > 0
            ? _highVacuumSeries.Points[^1].X
            : DateTimeAxis.ToDouble(DateTime.Now);

        _isApplyingLiveAxisRange = true;
        try
        {
            foreach (var chart in charts)
            {
                var start = double.IsFinite(_refreshStarts[(int)chart])
                    ? _refreshStarts[(int)chart]
                    : _liveSessionStart;
                if (!double.IsFinite(start))
                {
                    continue;
                }

                var window = double.IsFinite(_refreshStarts[(int)chart]) ? RefreshedLiveWindow : LiveWindow;
                var hasFilledWindow = latest - start >= window.TotalDays;
                var minimum = hasFilledWindow ? latest - window.TotalDays : start;
                var maximum = hasFilledWindow
                    ? latest + window.TotalDays * 0.02d
                    : start + window.TotalDays;
                foreach (var axis in TimeAxesFor(chart))
                {
                    axis.Minimum = minimum;
                    axis.Maximum = maximum;
                    axis.Zoom(minimum, maximum);
                }
            }
        }
        finally
        {
            _isApplyingLiveAxisRange = false;
        }
    }

    private void OnLiveTimeAxisChanged(object? sender, AxisChangedEventArgs args)
    {
        if (_isApplyingLiveAxisRange ||
            args.ChangeType is not (AxisChangeTypes.Zoom or AxisChangeTypes.Pan) ||
            sender is not DateTimeAxis changedAxis)
        {
            return;
        }

        IsAutoFollow = false;
        Array.Fill(_refreshStarts, double.NaN);
        _isApplyingLiveAxisRange = true;
        try
        {
            foreach (var axis in AllTimeAxes())
            {
                if (!ReferenceEquals(axis, changedAxis))
                {
                    axis.Zoom(changedAxis.ActualMinimum, changedAxis.ActualMaximum);
                }
            }
        }
        finally
        {
            _isApplyingLiveAxisRange = false;
        }

        InvalidatePlots();
    }

    private void ResetArchiveAxes()
    {
        foreach (var axis in new[]
                 {
                     _highVacuumTimeAxis,
                     _filmVacuumTimeAxis,
                     _powerTimeAxis,
                     _temperatureTimeAxis
                 })
        {
            // Hidden vacuum series still share the archive's real time range.
            var hasSamples = _highVacuumSeries.Points.Count > 0;
            axis.Minimum = hasSamples ? _highVacuumSeries.Points[0].X : double.NaN;
            axis.Maximum = hasSamples ? Math.Max(_highVacuumSeries.Points[^1].X,
                axis.Minimum + TimeSpan.FromSeconds(1).TotalDays) : double.NaN;
            axis.Reset();
        }
    }

    private void ResetValueAxes()
    {
        foreach (var axis in AllValueAxes())
        {
            axis.Minimum = double.NaN;
            axis.Maximum = double.NaN;
            axis.Reset();
        }
    }

    private void ApplySeriesVisibility()
    {
        _highVacuumSeries.IsVisible = ShowHighVacuum;
        _filmVacuumSeries.IsVisible = ShowFilmHighVacuum;
        _power1VoltageSeries.IsVisible = ShowPower1Voltage;
        _power1CurrentSeries.IsVisible = ShowPower1Current;
        _power2VoltageSeries.IsVisible = ShowPower2Voltage;
        _power2CurrentSeries.IsVisible = ShowPower2Current;
        _temperatureSeries.IsVisible = ShowTemperature;
        InvalidatePlots();
    }

    private void ClearSeries()
    {
        _liveSessionStart = double.NaN;
        Array.Fill(_refreshStarts, double.NaN);
        foreach (var series in AllSeries())
        {
            series.Points.Clear();
        }
    }

    private DateTimeAxis[] AllTimeAxes() =>
    [
        _highVacuumTimeAxis,
        _filmVacuumTimeAxis,
        _powerTimeAxis,
        _temperatureTimeAxis
    ];

    private DateTimeAxis[] TimeAxesFor(TrendChart chart) => chart switch
    {
        TrendChart.Vacuum => [_highVacuumTimeAxis, _filmVacuumTimeAxis],
        TrendChart.Power => [_powerTimeAxis],
        TrendChart.Temperature => [_temperatureTimeAxis],
        _ => throw new ArgumentOutOfRangeException(nameof(chart))
    };

    private IEnumerable<Axis> AllValueAxes() =>
        VacuumPlotModel.Axes
            .Concat(PowerPlotModel.Axes)
            .Concat(TemperaturePlotModel.Axes)
            .Where(axis => axis is not DateTimeAxis);

    private LineSeries[] AllSeries() =>
    [
        _highVacuumSeries,
        _filmVacuumSeries,
        _power1VoltageSeries,
        _power1CurrentSeries,
        _power2VoltageSeries,
        _power2CurrentSeries,
        _temperatureSeries
    ];

    private void InvalidatePlots()
    {
        VacuumPlotModel.InvalidatePlot(updateData: true);
        PowerPlotModel.InvalidatePlot(updateData: true);
        TemperaturePlotModel.InvalidatePlot(updateData: true);
    }
}

public sealed class LiveTrendViewModel : TrendChartViewModel, IDisposable
{
    private readonly ISessionTrendStore _trendStore;
    private readonly ITrendFileService _fileService;
    private readonly IHistoryFileDialogService _fileDialogService;

    private readonly IProcessTrendRecorder? _recorder;
    private bool _disposed;

    public LiveTrendViewModel(
        Dispatcher dispatcher,
        ISessionTrendStore trendStore,
        ITrendFileService fileService,
        IHistoryFileDialogService fileDialogService, bool isSimulated = false, IProcessTrendRecorder? recorder = null)
        : base(dispatcher, isLive: true)
    {
        _trendStore = trendStore;
        IsSimulationMode = isSimulated;
        _recorder = recorder;
        AcquisitionStatus = "等待PLC过程数据（每1秒记录）";
        if (_recorder is not null) _recorder.Changed += OnRecordingChanged;
        _fileService = fileService;
        _fileDialogService = fileDialogService;
        ActionCommand = new AsyncRelayCommand(SaveAsync);

        LoadSamples(_trendStore.Snapshot());
        _trendStore.SampleAdded += OnSampleAdded;
    }

    public override string ActionText => "保存";

    public override IAsyncRelayCommand ActionCommand { get; }

    public override bool IsSimulationMode { get; }

    private void OnSampleAdded(object? sender, TelemetrySample sample) => OnUi(() =>
    {
        if (_disposed) return;
        AcquisitionStatus = sample.DataQuality is "Good" or "Simulation:Good"
            ? "每1秒记录一组 · " + (IsSimulationMode ? "模拟数据" : "PLC实时数据")
            : "过程数据缺失：" + sample.DataQuality;
        AppendSample(sample);
    });

    private void OnRecordingChanged(object? sender, EventArgs e) => OnUi(() =>
    {
        if (!_disposed) RecordingError = _recorder?.RecordingError ?? "";
    });

    public void Dispose()
    {
        _disposed = true;
        _trendStore.SampleAdded -= OnSampleAdded;
        if (_recorder is not null) _recorder.Changed -= OnRecordingChanged;
    }

    private async Task SaveAsync()
    {
        var samples = _trendStore.Snapshot();
        if (samples.Count == 0)
        {
            StatusMessage = "当前没有可保存的曲线数据。";
            return;
        }

        var suggestedName = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_session-curves.csv";
        var path = _fileDialogService.SelectSavePath(suggestedName);
        if (path is null)
        {
            StatusMessage = "已取消保存。";
            return;
        }

        try
        {
            StatusMessage = "正在保存全部会话数据……";
            await _fileService.SaveAsync(path, samples);
            CurrentFileName = Path.GetFileName(path);
            StatusMessage = $"已保存 {samples.Count:N0} 条采样数据。";
        }
        catch (Exception exception)
        {
            StatusMessage = $"保存失败：{exception.Message}";
        }
    }
}
