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

    protected TrendChartViewModel(Dispatcher dispatcher, bool isLive)
    {
        _dispatcher = dispatcher;
        _isLive = isLive;

        VacuumPlotModel = new PlotModel { Title = "真空状态" };
        _highVacuumTimeAxis = CreateTimeAxis(HighVacuumTimeAxisKey, AxisPosition.Bottom, "高真空度计时间");
        _filmVacuumTimeAxis = CreateTimeAxis(FilmVacuumTimeAxisKey, AxisPosition.Top, "薄膜高真空度计时间");
        VacuumPlotModel.Axes.Add(_highVacuumTimeAxis);
        VacuumPlotModel.Axes.Add(_filmVacuumTimeAxis);
        VacuumPlotModel.Axes.Add(CreateValueAxis(
            HighVacuumValueAxisKey,
            AxisPosition.Left,
            "高真空度计 (Pa)",
            OxyColors.Red));
        VacuumPlotModel.Axes.Add(CreateValueAxis(
            FilmVacuumValueAxisKey,
            AxisPosition.Right,
            "薄膜高真空度计 (Pa)",
            OxyColors.Blue));
        _highVacuumSeries = CreateSeries(
            "高真空度计",
            OxyColors.Red,
            LineStyle.Solid,
            HighVacuumTimeAxisKey,
            HighVacuumValueAxisKey);
        _filmVacuumSeries = CreateSeries(
            "薄膜高真空度计",
            OxyColors.Blue,
            LineStyle.Solid,
            FilmVacuumTimeAxisKey,
            FilmVacuumValueAxisKey);
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
        }

        InvalidatePlots();
    }

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
        OxyColor color) =>
        new()
        {
            Key = key,
            Position = position,
            Title = title,
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
        string yAxisKey) =>
        new()
        {
            Title = title,
            Color = color,
            StrokeThickness = 2,
            LineStyle = lineStyle,
            XAxisKey = xAxisKey,
            YAxisKey = yAxisKey,
            TrackerFormatString = "{0}\n时间: {2}\n数值: {4:0.###}"
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

        _highVacuumSeries.Points.Add(new DataPoint(timestamp, sample.HighVacuumPa));
        _filmVacuumSeries.Points.Add(new DataPoint(timestamp, sample.FilmHighVacuumPa));
        _power1VoltageSeries.Points.Add(new DataPoint(timestamp, sample.Power1VoltageV));
        _power1CurrentSeries.Points.Add(new DataPoint(timestamp, sample.Power1CurrentA));
        _power2VoltageSeries.Points.Add(new DataPoint(timestamp, sample.Power2VoltageV));
        _power2CurrentSeries.Points.Add(new DataPoint(timestamp, sample.Power2CurrentA));
        _temperatureSeries.Points.Add(new DataPoint(timestamp, sample.TemperatureC));
    }

    private void ApplyLiveAxes()
    {
        var latest = _highVacuumSeries.Points[^1].X;
        var hasFilledWindow = latest - _liveSessionStart >= LiveWindow.TotalDays;
        var minimum = hasFilledWindow ? latest - LiveWindow.TotalDays : _liveSessionStart;
        var maximum = hasFilledWindow
            ? latest + LiveRightMargin
            : _liveSessionStart + LiveWindow.TotalDays;

        _isApplyingLiveAxisRange = true;
        try
        {
            foreach (var axis in AllTimeAxes())
            {
                axis.Minimum = minimum;
                axis.Maximum = maximum;
                axis.Zoom(minimum, maximum);
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
            axis.Minimum = double.NaN;
            axis.Maximum = double.NaN;
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

public sealed class LiveTrendViewModel : TrendChartViewModel
{
    private readonly ISessionTrendStore _trendStore;
    private readonly ITrendFileService _fileService;
    private readonly IHistoryFileDialogService _fileDialogService;

    public LiveTrendViewModel(
        Dispatcher dispatcher,
        ISessionTrendStore trendStore,
        ITrendFileService fileService,
        IHistoryFileDialogService fileDialogService)
        : base(dispatcher, isLive: true)
    {
        _trendStore = trendStore;
        _fileService = fileService;
        _fileDialogService = fileDialogService;
        ActionCommand = new AsyncRelayCommand(SaveAsync);

        LoadSamples(_trendStore.Snapshot());
        _trendStore.SampleAdded += OnSampleAdded;
    }

    public override string ActionText => "保存";

    public override IAsyncRelayCommand ActionCommand { get; }

    public override bool IsSimulationMode => true;

    private void OnSampleAdded(object? sender, TelemetrySample sample) => AppendSample(sample);

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

public sealed class ProcessTrendViewModel : TrendChartViewModel
{
    private readonly ITrendFileService _fileService;
    private readonly IHistoryFileDialogService _fileDialogService;

    public ProcessTrendViewModel(
        Dispatcher dispatcher,
        ITrendFileService fileService,
        IHistoryFileDialogService fileDialogService)
        : base(dispatcher, isLive: false)
    {
        _fileService = fileService;
        _fileDialogService = fileDialogService;
        ActionCommand = new AsyncRelayCommand(QueryAsync);
        StatusMessage = "请选择一次工艺或手动保存的曲线CSV文件。";
    }

    public override string ActionText => "查询";

    public override IAsyncRelayCommand ActionCommand { get; }

    public override bool IsSimulationMode => false;

    private async Task QueryAsync()
    {
        var path = _fileDialogService.SelectOpenPath();
        if (path is null)
        {
            StatusMessage = "已取消查询。";
            return;
        }

        try
        {
            StatusMessage = "正在读取曲线数据……";
            var samples = await _fileService.LoadAsync(path);
            if (samples.Count == 0)
            {
                StatusMessage = "文件格式有效，但没有采样数据。";
                return;
            }

            const int maximumDisplayedSamples = 10_000;
            var displaySamples = Downsample(samples, maximumDisplayedSamples);
            LoadSamples(displaySamples);
            CurrentFileName = Path.GetFileName(path);
            StatusMessage = displaySamples.Count == samples.Count
                ? $"已载入 {samples.Count:N0} 条采样数据。"
                : $"已载入 {samples.Count:N0} 条数据，图表抽样显示 {displaySamples.Count:N0} 条。";
        }
        catch (Exception exception)
        {
            StatusMessage = $"查询失败：{exception.Message}";
        }
    }

    private static IReadOnlyList<TelemetrySample> Downsample(
        IReadOnlyList<TelemetrySample> samples,
        int maximumCount)
    {
        if (samples.Count <= maximumCount)
        {
            return samples;
        }

        var step = (int)Math.Ceiling(samples.Count / (double)maximumCount);
        var result = new List<TelemetrySample>(maximumCount + 1);
        for (var index = 0; index < samples.Count; index += step)
        {
            result.Add(samples[index]);
        }

        if (result[^1] != samples[^1])
        {
            result.Add(samples[^1]);
        }

        return result;
    }
}
