using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Small_square_cavity_coating_machine.Models.History;
using Small_square_cavity_coating_machine.Services.History;
using Small_square_cavity_coating_machine.ViewModels.History;
using System.Globalization;
using System.Windows.Threading;
using Xunit;

namespace Small_square_cavity_coating_machine.Tests;

public sealed class HistoryTrendViewModelTests
{
    private static readonly TimeSpan LiveWindow = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RefreshedLiveWindow = TimeSpan.FromMinutes(10);
    private static readonly double LiveRightMargin = LiveWindow.TotalDays * 0.02d;

    [Fact]
    public async Task MockTelemetrySource_UsesAcceleratedSimulationClock()
    {
        var samples = new List<TelemetrySample>();
        var completed = new TaskCompletionSource<IReadOnlyList<TelemetrySample>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        await using var source = new MockTelemetrySource(
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMinutes(1));
        source.SampleReceived += (_, sample) =>
        {
            lock (samples)
            {
                samples.Add(sample);
                if (samples.Count == 3)
                {
                    completed.TrySetResult(samples.ToArray());
                }
            }
        };

        await source.StartAsync(cancellation.Token);
        IReadOnlyList<TelemetrySample> result;
        try
        {
            result = await completed.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            cancellation.Cancel();
        }

        Assert.Equal(TimeSpan.FromMinutes(1), result[1].Timestamp - result[0].Timestamp);
        Assert.Equal(TimeSpan.FromMinutes(1), result[2].Timestamp - result[1].Timestamp);
        Assert.All(result, sample => Assert.Equal("Simulation", sample.DataQuality));
    }

    [Fact]
    public void LiveTrend_AfterThirtyMinutesMovesWindowWithLatestSample()
    {
        var store = new SessionTrendStore();
        var viewModel = CreateViewModel(store);
        var start = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.FromHours(8));
        var startValue = DateTimeAxis.ToDouble(start.LocalDateTime);

        store.Append(CreateSample(start));
        store.Append(CreateSample(start.AddMinutes(29)));

        var vacuumAxis = GetTimeAxis(viewModel, "HighVacuumTime");
        AssertClose(startValue, vacuumAxis.ActualMinimum);
        AssertClose(startValue + LiveWindow.TotalDays, vacuumAxis.ActualMaximum);

        var latest = start.AddMinutes(30).AddSeconds(1);
        var latestValue = DateTimeAxis.ToDouble(latest.LocalDateTime);
        store.Append(CreateSample(latest));

        AssertClose(latestValue - LiveWindow.TotalDays, vacuumAxis.ActualMinimum);
        AssertClose(latestValue + LiveRightMargin, vacuumAxis.ActualMaximum);
        Assert.InRange(latestValue, vacuumAxis.ActualMinimum, vacuumAxis.ActualMaximum);

        var series = GetHighVacuumSeries(viewModel);
        Assert.Equal(3, series.Points.Count);
        AssertClose(startValue, series.Points[0].X);
    }

    [Fact]
    public void LiveTrend_KeepsFullProcessCurveWhileViewportContinuesScrolling()
    {
        var store = new SessionTrendStore();
        var viewModel = CreateViewModel(store);
        var start = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.FromHours(8));

        for (var minute = 0; minute <= 60; minute++)
        {
            store.Append(CreateSample(start.AddMinutes(minute)));
        }

        var series = GetHighVacuumSeries(viewModel);
        Assert.Equal(61, series.Points.Count);
        AssertClose(DateTimeAxis.ToDouble(start.LocalDateTime), series.Points[0].X);
        AssertClose(DateTimeAxis.ToDouble(start.AddMinutes(60).LocalDateTime), series.Points[^1].X);

        var historicalMinimum = DateTimeAxis.ToDouble(start.LocalDateTime);
        var historicalMaximum = DateTimeAxis.ToDouble(start.AddMinutes(10).LocalDateTime);
        GetTimeAxis(viewModel, "HighVacuumTime").Zoom(historicalMinimum, historicalMaximum);

        Assert.True(viewModel.IsLiveFollowPaused);
        AssertAllTimeAxes(viewModel, historicalMinimum, historicalMaximum);

        store.Append(CreateSample(start.AddMinutes(61)));

        Assert.Equal(62, series.Points.Count);
        AssertClose(DateTimeAxis.ToDouble(start.LocalDateTime), series.Points[0].X);
        AssertClose(DateTimeAxis.ToDouble(start.AddMinutes(61).LocalDateTime), series.Points[^1].X);
        AssertAllTimeAxes(viewModel, historicalMinimum, historicalMaximum);
    }

    [Fact]
    public void LiveTrend_UserZoomPausesAndResumeSynchronizesAllTimeAxes()
    {
        var store = new SessionTrendStore();
        var viewModel = CreateViewModel(store);
        var start = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.FromHours(8));
        store.Append(CreateSample(start));
        store.Append(CreateSample(start.AddMinutes(31)));

        var changedAxis = GetTimeAxis(viewModel, "HighVacuumTime");
        var inspectionMinimum = DateTimeAxis.ToDouble(start.AddMinutes(5).LocalDateTime);
        var inspectionMaximum = DateTimeAxis.ToDouble(start.AddMinutes(10).LocalDateTime);
        changedAxis.Zoom(inspectionMinimum, inspectionMaximum);

        Assert.False(viewModel.IsAutoFollow);
        Assert.True(viewModel.IsLiveFollowPaused);
        AssertAllTimeAxes(viewModel, inspectionMinimum, inspectionMaximum);

        store.Append(CreateSample(start.AddMinutes(32)));
        AssertAllTimeAxes(viewModel, inspectionMinimum, inspectionMaximum);

        viewModel.ResumeLiveFollowCommand.Execute(null);

        var latestValue = DateTimeAxis.ToDouble(start.AddMinutes(32).LocalDateTime);
        Assert.True(viewModel.IsAutoFollow);
        Assert.False(viewModel.IsLiveFollowPaused);
        AssertAllTimeAxes(
            viewModel,
            latestValue - LiveWindow.TotalDays,
            latestValue + LiveRightMargin);
    }

    [Fact]
    public void AutomaticDisplay_RestoresLiveXAxisAndAutomaticVisibleSeriesYAxis()
    {
        var store = new SessionTrendStore();
        var viewModel = CreateViewModel(store);
        var start = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.FromHours(8));
        store.Append(CreateSample(start));
        store.Append(CreateSample(start.AddMinutes(31)));

        var timeAxis = GetTimeAxis(viewModel, "HighVacuumTime");
        var valueAxis = (LogarithmicAxis)viewModel.VacuumPlotModel.Axes.Single(axis => axis.Key == "HighVacuumValue");
        timeAxis.Zoom(
            DateTimeAxis.ToDouble(start.AddMinutes(5).LocalDateTime),
            DateTimeAxis.ToDouble(start.AddMinutes(10).LocalDateTime));
        valueAxis.Zoom(1e-6d, 1e6d);

        viewModel.ResumeLiveFollowCommand.Execute(null);

        var latestValue = DateTimeAxis.ToDouble(start.AddMinutes(31).LocalDateTime);
        Assert.True(viewModel.IsAutoFollow);
        AssertAllTimeAxes(
            viewModel,
            latestValue - LiveWindow.TotalDays,
            latestValue + LiveRightMargin);
        Assert.True(double.IsNaN(valueAxis.Minimum));
        Assert.True(double.IsNaN(valueAxis.Maximum));
        ((IPlotModel)viewModel.VacuumPlotModel).Update(updateData: true);
        Assert.InRange(13.6d, valueAxis.ActualMinimum, valueAxis.ActualMaximum);
        Assert.True(valueAxis.ActualMaximum / valueAxis.ActualMinimum < 100d,
            $"Automatic logarithmic range was {valueAxis.ActualMinimum}..{valueAxis.ActualMaximum}.");
    }

    [Fact]
    public void VacuumCurves_UseBaseTenLogarithmicAxesWithoutChangingOtherCurves()
    {
        var viewModel = CreateViewModel(new SessionTrendStore());
        var vacuumAxes = viewModel.VacuumPlotModel.Axes
            .OfType<LogarithmicAxis>()
            .ToArray();
        var vacuumSeries = viewModel.VacuumPlotModel.Series.OfType<LineSeries>().ToArray();
        var powerSeries = viewModel.PowerPlotModel.Series.OfType<LineSeries>().ToArray();

        Assert.Equal(2, vacuumAxes.Length);
        Assert.All(vacuumAxes, axis => Assert.Equal(10d, axis.Base));
        Assert.All(vacuumAxes, axis => Assert.Equal("0.00E+00", axis.StringFormat));
        Assert.Equal("1.23E-04", 0.000123d.ToString(vacuumAxes[0].StringFormat, CultureInfo.InvariantCulture));
        Assert.All(vacuumSeries, series => Assert.Contains("数值: {4:0.00E+00}", series.TrackerFormatString));
        Assert.Empty(viewModel.PowerPlotModel.Axes.OfType<LogarithmicAxis>());
        Assert.Empty(viewModel.TemperaturePlotModel.Axes.OfType<LogarithmicAxis>());
        Assert.All(powerSeries, series => Assert.Contains("数值: {4:0.###}", series.TrackerFormatString));
        Assert.Contains("数值: {4:0.###}", ((LineSeries)viewModel.TemperaturePlotModel.Series.Single()).TrackerFormatString);
    }

    [Fact]
    public void VacuumCurves_UseGapsForNonPositiveOrInvalidReadingsWithoutChangingStoredSamples()
    {
        var store = new SessionTrendStore();
        var viewModel = CreateViewModel(store);
        var start = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.FromHours(8));
        var valid = CreateSample(start) with { HighVacuumPa = 1e-5d, FilmHighVacuumPa = 1e4d };
        var invalid = CreateSample(start.AddSeconds(1)) with { HighVacuumPa = 0d, FilmHighVacuumPa = -1d };
        var missing = CreateSample(start.AddSeconds(2)) with { HighVacuumPa = double.NaN, FilmHighVacuumPa = double.PositiveInfinity };

        store.Append(valid);
        store.Append(invalid);
        store.Append(missing);

        Assert.Equal(0d, store.Snapshot()[1].HighVacuumPa);
        Assert.Equal(-1d, store.Snapshot()[1].FilmHighVacuumPa);
        var series = viewModel.VacuumPlotModel.Series.OfType<LineSeries>().ToArray();
        Assert.Equal(1e-5d, series[0].Points[0].Y);
        Assert.Equal(1e4d, series[1].Points[0].Y);
        Assert.All(series.SelectMany(line => line.Points.Skip(1)), point => Assert.True(double.IsNaN(point.Y)));

        ((IPlotModel)viewModel.VacuumPlotModel).Update(updateData: true);
        Assert.All(viewModel.VacuumPlotModel.Axes.OfType<LogarithmicAxis>(), axis =>
        {
            Assert.True(axis.ActualMinimum > 0d);
            Assert.True(axis.ActualMaximum > axis.ActualMinimum);
        });
    }

    [Fact]
    public void RefreshCurrentChart_OnlyResetsTheSelectedChartAndPreservesExistingSamples()
    {
        var store = new SessionTrendStore();
        var viewModel = CreateViewModel(store);
        var start = DateTimeOffset.Now.AddMinutes(-5);
        store.Append(CreateSample(start));

        var powerAxis = GetTimeAxis(viewModel, "PowerTime");
        var powerMinimum = powerAxis.ActualMinimum;
        var powerMaximum = powerAxis.ActualMaximum;
        viewModel.SelectedChartIndex = 0;
        viewModel.RefreshCurrentChartCommand.Execute(null);

        var refreshStart = GetTimeAxis(viewModel, "HighVacuumTime").ActualMinimum;
        Assert.InRange(Math.Abs(refreshStart - DateTimeAxis.ToDouble(DateTime.Now)), 0d, TimeSpan.FromSeconds(2).TotalDays);
        AssertClose(refreshStart + RefreshedLiveWindow.TotalDays, GetTimeAxis(viewModel, "HighVacuumTime").ActualMaximum);
        AssertAllVacuumTimeAxes(viewModel, refreshStart, refreshStart + RefreshedLiveWindow.TotalDays);
        AssertClose(powerMinimum, powerAxis.ActualMinimum);
        AssertClose(powerMaximum, powerAxis.ActualMaximum);
        Assert.Single(GetHighVacuumSeries(viewModel).Points);

        var latest = DateTimeOffset.Now.AddMinutes(10).AddSeconds(1);
        store.Append(CreateSample(latest));
        var latestValue = DateTimeAxis.ToDouble(latest.LocalDateTime);
        AssertClose(latestValue - RefreshedLiveWindow.TotalDays, GetTimeAxis(viewModel, "HighVacuumTime").ActualMinimum);
        AssertClose(latestValue + RefreshedLiveWindow.TotalDays * 0.02d, GetTimeAxis(viewModel, "HighVacuumTime").ActualMaximum);
        Assert.NotEqual(GetTimeAxis(viewModel, "HighVacuumTime").ActualMinimum, powerAxis.ActualMinimum);
    }

    [Theory]
    [InlineData(0, "HighVacuumTime", "FilmVacuumTime")]
    [InlineData(1, "PowerTime")]
    [InlineData(2, "TemperatureTime")]
    public void RefreshCurrentChart_ResetsOnlyTheSelectedTab(int selectedIndex, params string[] refreshedAxisKeys)
    {
        var store = new SessionTrendStore();
        var viewModel = CreateViewModel(store);
        store.Append(CreateSample(DateTimeOffset.Now.AddMinutes(-5)));
        var before = GetAllTimeAxes(viewModel).ToDictionary(axis => axis.Key!, axis => (axis.ActualMinimum, axis.ActualMaximum));

        viewModel.SelectedChartIndex = selectedIndex;
        viewModel.RefreshCurrentChartCommand.Execute(null);

        foreach (var axis in GetAllTimeAxes(viewModel))
        {
            if (refreshedAxisKeys.Contains(axis.Key))
            {
                Assert.InRange(
                    axis.ActualMaximum - axis.ActualMinimum,
                    RefreshedLiveWindow.TotalDays - 0.00000001d,
                    RefreshedLiveWindow.TotalDays + 0.00000001d);
            }
            else
            {
                AssertClose(before[axis.Key!].ActualMinimum, axis.ActualMinimum);
                AssertClose(before[axis.Key!].ActualMaximum, axis.ActualMaximum);
            }
        }
    }

    [Fact]
    public void RefreshCurrentChart_ContinuesOnlyTheRefreshedChartAfterGlobalFollowIsPaused()
    {
        var store = new SessionTrendStore();
        var viewModel = CreateViewModel(store);
        var start = DateTimeOffset.Now.AddMinutes(-5);
        store.Append(CreateSample(start));
        store.Append(CreateSample(start.AddMinutes(1)));

        var powerAxis = GetTimeAxis(viewModel, "PowerTime");
        powerAxis.Zoom(
            DateTimeAxis.ToDouble(start.AddSeconds(10).LocalDateTime),
            DateTimeAxis.ToDouble(start.AddSeconds(20).LocalDateTime));
        var pausedPowerMinimum = powerAxis.ActualMinimum;
        var pausedPowerMaximum = powerAxis.ActualMaximum;
        Assert.False(viewModel.IsAutoFollow);

        viewModel.SelectedChartIndex = 0;
        viewModel.RefreshCurrentChartCommand.Execute(null);
        var refreshedVacuumMinimum = GetTimeAxis(viewModel, "HighVacuumTime").ActualMinimum;
        store.Append(CreateSample(DateTimeOffset.Now.AddSeconds(1)));

        AssertClose(pausedPowerMinimum, powerAxis.ActualMinimum);
        AssertClose(pausedPowerMaximum, powerAxis.ActualMaximum);
        AssertClose(refreshedVacuumMinimum, GetTimeAxis(viewModel, "HighVacuumTime").ActualMinimum);
    }

    [Fact]
    public void LiveTrend_UserPanPausesAndSynchronizesAllTimeAxes()
    {
        var store = new SessionTrendStore();
        var viewModel = CreateViewModel(store);
        var start = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.FromHours(8));
        store.Append(CreateSample(start));
        store.Append(CreateSample(start.AddMinutes(31)));

        var changedAxis = GetTimeAxis(viewModel, "PowerTime");
        changedAxis.Pan(25d);

        Assert.True(viewModel.IsLiveFollowPaused);
        AssertAllTimeAxes(viewModel, changedAxis.ActualMinimum, changedAxis.ActualMaximum);
    }

    private static LiveTrendViewModel CreateViewModel(SessionTrendStore store) =>
        new(
            Dispatcher.CurrentDispatcher,
            store,
            new NullTrendFileService(),
            new NullHistoryFileDialogService());

    private static DateTimeAxis GetTimeAxis(LiveTrendViewModel viewModel, string key) =>
        viewModel.VacuumPlotModel.Axes
            .Concat(viewModel.PowerPlotModel.Axes)
            .Concat(viewModel.TemperaturePlotModel.Axes)
            .OfType<DateTimeAxis>()
            .Single(axis => axis.Key == key);

    private static LineSeries GetHighVacuumSeries(LiveTrendViewModel viewModel) =>
        viewModel.VacuumPlotModel.Series
            .OfType<LineSeries>()
            .Single(series => series.Title == "高真空度计");

    private static IEnumerable<DateTimeAxis> GetAllTimeAxes(LiveTrendViewModel viewModel) =>
        viewModel.VacuumPlotModel.Axes
            .Concat(viewModel.PowerPlotModel.Axes)
            .Concat(viewModel.TemperaturePlotModel.Axes)
            .OfType<DateTimeAxis>();

    private static void AssertAllVacuumTimeAxes(
        LiveTrendViewModel viewModel,
        double expectedMinimum,
        double expectedMaximum)
    {
        foreach (var axis in viewModel.VacuumPlotModel.Axes.OfType<DateTimeAxis>())
        {
            AssertClose(expectedMinimum, axis.ActualMinimum);
            AssertClose(expectedMaximum, axis.ActualMaximum);
        }
    }

    private static void AssertAllTimeAxes(
        LiveTrendViewModel viewModel,
        double expectedMinimum,
        double expectedMaximum)
    {
        foreach (var axis in GetAllTimeAxes(viewModel))
        {
            AssertClose(expectedMinimum, axis.ActualMinimum);
            AssertClose(expectedMaximum, axis.ActualMaximum);
        }
    }

    private static void AssertClose(double expected, double actual) =>
        Assert.InRange(Math.Abs(expected - actual), 0d, 0.00000001d);

    private static TelemetrySample CreateSample(DateTimeOffset timestamp) =>
        new(
            timestamp,
            "test-session",
            string.Empty,
            string.Empty,
            13.6,
            12.9,
            480,
            1.8,
            472,
            1.65,
            186.5,
            "Test");

    private sealed class NullTrendFileService : ITrendFileService
    {
        public Task SaveAsync(
            string path,
            IReadOnlyList<TelemetrySample> samples,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<TelemetrySample>> LoadAsync(
            string path,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TelemetrySample>>([]);
    }

    private sealed class NullHistoryFileDialogService : IHistoryFileDialogService
    {
        public string? SelectSavePath(string suggestedFileName) => null;

        public string? SelectOpenPath() => null;
    }
}
