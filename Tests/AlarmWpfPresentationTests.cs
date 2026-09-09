using Small_square_cavity_coating_machine.Controls;
using Small_square_cavity_coating_machine.Models.Equipment;
using Small_square_cavity_coating_machine.Services.Equipment;
using Small_square_cavity_coating_machine.ViewModels.Equipment;
using System.Windows.Input;
using Small_square_cavity_coating_machine.Models.Recipes;
using Small_square_cavity_coating_machine.Services.Recipes;
using Small_square_cavity_coating_machine.ViewModels.Recipes;
using Small_square_cavity_coating_machine.Services.Alarms;
using Small_square_cavity_coating_machine.Services.History;
using Small_square_cavity_coating_machine.ViewModels;
using Small_square_cavity_coating_machine.ViewModels.History;
using Small_square_cavity_coating_machine.Views.HistoryModules;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using Xunit;

namespace Small_square_cavity_coating_machine.Tests;

[CollectionDefinition("AlarmWpf", DisableParallelization = true)]
public sealed class AlarmWpfCollection;

[Collection("AlarmWpf")]
public sealed class AlarmWpfPresentationTests
{
    [Fact]
    public async Task Real_WPF_bindings_update_on_UI_thread_and_render_all_alarm_states()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var trace = new StringWriter();
            var listener = new TextWriterTraceListener(trace);
            try
            {
                // Use the real resource styles, but never construct App: its queued startup
                // initializes accounts and may connect to a user-configured PLC.
                var app = new Application();
                XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                var appMarkup = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestAssets", "App.xaml"));
                var resources = new XElement(presentation + "ResourceDictionary",
                    new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"),
                    appMarkup.Root!.Element(presentation + "Application.Resources")!.Elements());
                app.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString());
                PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
                PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
                using var temporary = new AlarmFeatureTests.TemporaryDirectory();
                var path = SqliteAlarmDefinitionRepository.ExtractEmbeddedDatabase(temporary.Path);
                var definitions = new SqliteAlarmDefinitionRepository(path).Load();
                var repository = new InMemoryAlarmLogRepository();
                var source = new SimulatedAlarmSignalSource();
                using var monitor = new AlarmMonitorService(definitions, source, repository);
                var dispatcher = new WpfUiDispatcher(Dispatcher.CurrentDispatcher);
                using var history = new AlarmHistoryViewModel(repository, dispatcher,
                    new AlarmSimulationViewModel(source, definitions));
                using var status = new AlarmStatusViewModel(monitor, repository, dispatcher, new AlarmNavigationService());
                ApplicationStatusViewModel.Instance.AlarmStatus = status;
                ApplicationStatusViewModel.Instance.PlcConnectionText = "模拟报警源（PLC未连接）";
                source.StartAsync(definitions, CancellationToken.None).GetAwaiter().GetResult();
                var view = new AlarmHistoryView { DataContext = history };
                var tabs = new TabControl { Style = (Style)app.Resources["HistoryTabControlStyle"],
                    ItemContainerStyle = (Style)app.Resources["HistoryTabItemStyle"] };
                foreach (var title in new[] { "数据曲线", "工艺记录", "操作记录" }) tabs.Items.Add(new TabItem { Header = title });
                tabs.Items.Add(new TabItem { Header = "报警记录", Content = view });
                tabs.SelectedIndex = 3;
                var root = new HmiPageLayout { PageTitle = "历史记录", PageContent = tabs };
                var connectionStore = new AlarmArrayConnectionTests.Settings();
                var connectionSource = new OpcUaAlarmSignalSource(connectionStore, new AlarmArrayConnectionTests.Factory { Fail = true });
                using var connectionViewModel = new PlcConnectionStatusViewModel(connectionSource, dispatcher);
                ApplicationStatusViewModel.Instance.PlcConnection = connectionViewModel;
                Render(root, "normal", 1440, 900);
                var connectionPopup = (Popup)root.FindName("PlcSettingsPopup");
                var connectionDetails = (FrameworkElement)connectionPopup.Child;
                Assert.Same(connectionViewModel, connectionDetails.DataContext);
                connectionViewModel.BeginEditCommand.Execute(null);
                Render(connectionDetails, "plc-connection-settings", 480, 330);
                var editor = (TextBox)root.FindName("PlcEndpointEditor");
                Assert.Equal(OpcUaAlarmOptions.DefaultEndpoint, editor.Text);
                editor.Text = "bad endpoint";
                editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                connectionViewModel.ApplyCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                Assert.NotEmpty(connectionViewModel.ValidationError);
                Render(connectionDetails, "plc-connection-validation", 480, 370);
                connectionViewModel.BeginEditCommand.Execute(null);

                var uiThread = Environment.CurrentManagedThreadId;
                var changedThread = 0;
                history.Records.CollectionChanged += (_, _) => changedThread = Environment.CurrentManagedThreadId;
                var watch = Stopwatch.StartNew();
                Task.Run(() => source.Set("EQ_Alarm[0]", true)).GetAwaiter().GetResult();
                Pump();
                Assert.Single(history.Records);
                Assert.Equal(1, status.ActiveCount);
                Assert.Equal(uiThread, changedThread);
                Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(500), $"UI notification latency: {watch.Elapsed}");
                source.Set("EQ_Alarm[519]", true);
                Render(root, "active", 1440, 900);
                var toggle = (ToggleButton)root.FindName("AlarmDetailsToggle");
                var popup = ((Grid)toggle.Parent).Children.OfType<Popup>().Single();
                var details = (FrameworkElement)popup.Child;
                Assert.Same(status, details.DataContext);
                Render(details, "details", 520, 420);

                history.StartDate = DateTime.Today.AddDays(-10);
                history.EndDate = DateTime.Today.AddDays(-8);
                history.QueryCoreCommand.Execute(null);
                source.Set("EQ_Alarm[19]", true);
                Assert.True(Assert.Single(history.Records).IsOutsideRange);
                Render(root, "historical-new", 1440, 900);
                source.Disconnect();
                Render(root, "disconnected", 1280, 800);
                source.Connect();
                source.Set("EQ_Alarm[0]", false);
                source.Set("EQ_Alarm[519]", false);
                source.Set("EQ_Alarm[19]", false);
                history.ReturnToLiveCommand.Execute(null);
                Render(root, "cleared", 1440, 900);
                Assert.Equal(3, history.Records.Count);
                Assert.All(history.Records, row => Assert.Equal("已清除", row.Record.StatusText));
                VerifyEquipmentPages(dispatcher);
                VerifyRecipePage(dispatcher);
                VerifyTrendLegends();
                VerifyProcessArchivePage();
                listener.Flush();
                Assert.True(string.IsNullOrWhiteSpace(trace.ToString()), trace.ToString());
                ApplicationStatusViewModel.Instance.AlarmStatus = null;
                ApplicationStatusViewModel.Instance.PlcConnection = null;
                connectionSource.DisposeAsync().AsTask().GetAwaiter().GetResult();
                completion.TrySetResult();
            }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally
            {
                PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
                listener.Dispose();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static void VerifyEquipmentPages(IUiDispatcher dispatcher)
    {
        var h = new EquipmentFeatureTests.Harness();
        try
        {
            h.Session.Values[EquipmentGroups.Parameter] = h.Parameters.Select(p => (float)p.Value).ToArray();
            ApplicationStatusViewModel.Instance.PlcConnectionText = "本机测试源（非现场PLC）";
            using var io = new IoStatusViewModel(h.Io, h.Client, dispatcher);
            using var parameters = new ParameterSettingsViewModel(h.Parameters, h.Client, h.Runtime, h.Auth, dispatcher);
            using var connection = new PlcConnectionStatusViewModel(h.Client, dispatcher);
            using var operationHistory = new OperationHistoryViewModel(h.Runtime, dispatcher);
            ApplicationStatusViewModel.Instance.PlcConnection = connection;
            var ioPage = new Views.IO(io);
            var parameterPage = new Views.Parameter(parameters);
            using var controlViewModel = new ControlViewModel
            {
                SampleStageIsRunning = true,
                Power1IsRunning = true,
                Power2IsFaulted = true,
                SampleShutterIsOn = true,
                Target1ShutterIsFaulted = true,
                ForelineGaugeIsReadingEnabled = true,
                HighVacuumGaugeIsFaulted = true,
                FilmGaugeIsReadingEnabled = true,
                FilmGaugeValveIsOpen = true
            };
            var controlPage = new Views.Controlview(controlViewModel);
            Render(ioPage, "io-disconnected", 1440, 900);
            Render(parameterPage, "parameters-disconnected", 1440, 900);
            Render(controlPage, "control-part-state", 1440, 900);
            Assert.Equal(3, Descendants<VacuumGaugeControl>(controlPage).Count());
            var cgValve = Assert.Single(Descendants<RoundToggleButton>(controlPage));
            Assert.True(cgValve.IsOn);
            var mfcControls = Descendants<MfcFlowMeterControl>(controlPage).ToArray();
            Assert.Equal(3, mfcControls.Length);
            Assert.All(mfcControls, control => Assert.NotNull(control.SetpointCommand));
            Assert.All(mfcControls, control =>
                Assert.Single(Descendants<TextBox>(control), box => box.Name == "SetpointTextBox"));
            Assert.Contains(Descendants<SampleStageSpeedControl>(controlPage), c => c.ShowStatusIndicator);
            Assert.Equal(64, io.Rows.Count); Assert.Equal(21, parameters.Rows.Count);
            Assert.All(parameters.Rows, r => { Assert.Equal("—", r.CurrentText); Assert.False(r.CanEdit); });
            Task.Run(h.Start).GetAwaiter().GetResult(); Pump();
            var changedOn = 0; var uiThread = Environment.CurrentManagedThreadId;
            io.Rows.Single(r => r.Definition.Address == "EQ_IO[500]").PropertyChanged += (_, _) => changedOn = Environment.CurrentManagedThreadId;
            var watch = Stopwatch.StartNew();
            Task.Run(() => { ((bool[])h.Session.Values[EquipmentGroups.Io])[0] = true; ((bool[])h.Session.Values[EquipmentGroups.Io])[500] = true; h.Session.Send(EquipmentGroups.Io); }).GetAwaiter().GetResult();
            Pump(); Assert.Equal(uiThread, changedOn); Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(500));
            Render(ioPage, "io-live", 1440, 900);
            Render(parameterPage, "parameters-live", 1440, 900);
            var editors = Descendants<TextBox>(parameterPage).Where(e => e.DataContext is ParameterRowViewModel).ToArray();
            Assert.NotEmpty(editors);
            var box = editors.Single(e => ReferenceEquals(e.DataContext, parameters.Rows[0]));
            var row = parameters.Rows[0];
            row.BeginEditCommand.Execute(null); box.Text = "8";
            box.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            row.EndEditCommand.Execute(null);
            Assert.Empty(h.Session.Writes); // loss of focus only ends edit mode
            Assert.True(ParameterEditorBehavior.HandleKey(box, Key.Enter, true));
            Assert.Empty(h.Session.Writes);
            Assert.True(ParameterEditorBehavior.HandleKey(box, Key.Enter, false));
            var limit = Stopwatch.StartNew();
            while (row.SubmitCommand.IsRunning && limit.Elapsed < TimeSpan.FromSeconds(5)) { Pump(); Thread.Sleep(1); }
            Assert.False(row.SubmitCommand.IsRunning); Pump();
            Assert.Single(h.Session.Writes);
            Assert.Equal(0, h.Session.Writes[0].Index);
            Assert.Contains("写入已确认", row.StatusText);
            Assert.Equal("已确认", Assert.Single(operationHistory.Records).Outcome);
            Render(parameterPage, "parameters-confirmed", 1440, 900);
            box.Text = "9"; box.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            ParameterEditorBehavior.HandleKey(box, Key.Escape, false); Pump();
            Assert.Equal("8", box.Text); Assert.Single(h.Session.Writes);
            Render(parameterPage, "parameters-cancelled", 1440, 900);
            h.Auth.SetAllowed(false); Pump();
            Assert.All(parameters.Rows, r => Assert.False(r.CanEdit));
            Render(parameterPage, "parameters-guest", 1280, 800);
            Render(((HmiPageLayout)ioPage.Content).FindName("PlcSettingsPopup") is Popup popup
                ? (FrameworkElement)popup.Child : throw new InvalidOperationException(), "equipment-connection", 480, 430);
        }
        finally { h.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    private sealed class RecipeDialogs : IRecipeUserDialogService
    {
        public readonly List<RecipeRunResult> Results = [];
        public string? SelectRecipeFile() => null;
        public bool ConfirmReplaceExistingRecipe() => true;
        public bool ConfirmClearRecipe() => true;
        public RecipeLayer? ShowNewLayerDialog(int sequence) => null;
        public void ShowImportErrors(IReadOnlyList<RecipeImportError> errors) { }
        public void ShowInformation(string message, string title) { }
        public void ShowRunFinished(RecipeRunResult result) => Results.Add(result);
    }

    private static void VerifyRecipePage(IUiDispatcher dispatcher)
    {
        var h = new RecipeProtocolTests.Harness(new RecipeProtocolTests.Session { AutoComplete = false });
        var oldContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            using var connection = new PlcConnectionStatusViewModel(h.Client, dispatcher);
            ApplicationStatusViewModel.Instance.PlcConnection = connection;
            var dialogs = new RecipeDialogs();
            using var vm = new ProcessViewModel(new ExcelRecipeImporter(new SqliteRecipeDefinitionRepository(h.Definitions.DefinitionsPath).Load()),
                h.Dispatch, h.Gateway, dialogs, h.Definitions.Runtime, ApplicationStatusViewModel.Instance, h.Definitions.Auth, dispatcher);
            vm.LoadedFileName = "模拟分层测试.xlsx";
            vm.Layers.Add(new RecipeLayer { Sequence = 1, CathodeAPower = 230, StageSpeedRpm = 10 });
            vm.Layers.Add(new RecipeLayer { Sequence = 2, CathodeAPower = 250 });
            vm.Layers.Add(new RecipeLayer { Sequence = 3, CathodeBPower = 320 });
            var page = new Views.Process(vm);
            Render(page, "recipe-disconnected", 1440, 900);
            Task.Run(h.Start).GetAwaiter().GetResult(); Pump();
            vm.SetSelectedLayers([vm.Layers[2], vm.Layers[0]]);
            var run = vm.SendSelectedCommand.ExecuteAsync(null);
            void Until(Func<bool> condition)
            {
                var timer = Stopwatch.StartNew();
                while (!condition() && timer.Elapsed < TimeSpan.FromSeconds(5)) { Pump(); Thread.Sleep(1); }
                Assert.True(condition());
            }
            Until(() => vm.Layers[0].IsCurrent);
            Assert.False(vm.NewRecipeLayerCommand.CanExecute(null)); Assert.False(connection.CanApply);
            Render(page, "recipe-first-layer", 1440, 900);
            var timer = Stopwatch.StartNew();
            Task.Run(h.Session.Complete).GetAwaiter().GetResult();
            Until(() => vm.Layers[2].IsCurrent);
            Assert.True(timer.Elapsed < TimeSpan.FromMilliseconds(500));
            Assert.False(vm.Layers[1].IsCurrent);
            Render(page, "recipe-selected-last-layer", 1280, 800);
            Task.Run(h.Session.Complete).GetAwaiter().GetResult();
            Until(() => run.IsCompleted);
            run.GetAwaiter().GetResult(); Pump();
            var completed = Assert.Single(dialogs.Results);
            Assert.True(completed.IsCompleted, completed.FailureReason);
            Assert.Equal("模拟分层测试", completed.RecipeName);
            Assert.Equal(2, completed.CompletedLayers);
            Assert.False(vm.IsRunning);
            Render(page, "recipe-completed", 1440, 900);
            var blocks = h.Session.Writes.Where(w => w.Group == EquipmentGroups.Recipe).Select(w => (float[])w.Value).ToArray();
            Assert.Equal(230, blocks[0][0]); Assert.Equal(320, blocks[1][1]);
        }
        finally
        {
            Task.Run(() => h.DisposeAsync().AsTask()).GetAwaiter().GetResult();
            SynchronizationContext.SetSynchronizationContext(oldContext);
            ApplicationStatusViewModel.Instance.PlcConnection = null;
        }
    }

    private static void VerifyTrendLegends()
    {
        var store = new SessionTrendStore();
        for (var minute = 0; minute <= 30; minute++)
        {
            store.Append(new Models.History.TelemetrySample(
                DateTimeOffset.Now.AddMinutes(minute), "legend-test", "", "",
                13.6 + minute * 0.02, 13.2 + minute * 0.01,
                480 + minute * 0.2, 1.8 - minute * 0.002,
                472 + minute * 0.2, 1.65 - minute * 0.002, 186.5 + minute, "Test"));
        }

        TrendChartViewModel[] viewModels =
        [
            new LiveTrendViewModel(Dispatcher.CurrentDispatcher, store,
                new CsvTrendFileService(), new HistoryFileDialogService()),
            new ProcessTrendViewModel(Dispatcher.CurrentDispatcher,
                new CsvTrendFileService(), new HistoryFileDialogService())
        ];
        foreach (var vm in viewModels)
        {
            var view = new TrendChartView { DataContext = vm, Background = Brushes.White,
                FontSize = 16, FontWeight = FontWeights.SemiBold };
            var tabs = ((Grid)view.Content).Children.OfType<TabControl>().Single();
            var plots = new[] { vm.VacuumPlotModel, vm.PowerPlotModel, vm.TemperaturePlotModel };
            var mode = vm is LiveTrendViewModel ? "live" : "process";
            for (var tabIndex = 0; tabIndex < tabs.Items.Count; tabIndex++)
            {
                tabs.SelectedIndex = tabIndex;
                Render(view, $"trend-{mode}-{tabIndex}", 1400, 600);
                var checkBoxes = Descendants<CheckBox>(view).ToArray();
                Assert.Equal(plots[tabIndex].Series.Count, checkBoxes.Length);
                foreach (var checkBox in checkBoxes)
                {
                    var content = Assert.IsType<StackPanel>(checkBox.Content);
                    var line = Assert.Single(content.Children.OfType<System.Windows.Shapes.Line>());
                    var label = Assert.Single(content.Children.OfType<TextBlock>());
                    var series = plots[tabIndex].Series.OfType<OxyPlot.Series.LineSeries>()
                        .Single(item => item.Title == label.Text);
                    var color = Assert.IsType<SolidColorBrush>(line.Stroke).Color;
                    Assert.Equal(series.Color.ToString(), color.ToString(), ignoreCase: true);
                    Assert.Equal(series.LineStyle == OxyPlot.LineStyle.Dash, line.StrokeDashArray.Count > 0);
                    Assert.Equal(series.IsVisible, checkBox.IsChecked);

                    // Exercise the real two-way binding in both directions, including hidden series.
                    checkBox.SetCurrentValue(CheckBox.IsCheckedProperty, false);
                    Pump();
                    Assert.False(series.IsVisible);
                    checkBox.SetCurrentValue(CheckBox.IsCheckedProperty, true);
                    Pump();
                    Assert.True(series.IsVisible);
                }
                Render(view, $"trend-{mode}-{tabIndex}-all", 1400, 600);
                if (tabIndex == 1)
                {
                    Render(view, $"trend-{mode}-power-narrow", 680, 600);
                    var first = checkBoxes[0].TranslatePoint(new Point(), view);
                    var last = checkBoxes[^1].TranslatePoint(new Point(), view);
                    Assert.True(last.Y > first.Y, "Power legends should wrap in a narrow view.");
                }
            }
        }
    }

    private static void VerifyProcessArchivePage()
    {
        using var temp = new AlarmFeatureTests.TemporaryDirectory();
        var repository = new SqliteProcessTrendRepository(Path.Combine(temp.Path, "trends.db"));
        var recorder = new ProcessTrendRecorder(repository, new CsvTrendFileService(), Path.Combine(temp.Path, "csv"), []);
        var delayed = new DelayedTrendRecorder(recorder);
        try
        {
            var request1 = new RecipeRunRequest(RecipeDispatchMode.All, [new RecipeLayer { Sequence = 1 }], "1") { RecipeName = "真空镀膜测试A" };
            var request2 = request1 with { RunId = Guid.NewGuid(), RecipeName = "选中层镀膜测试B" };
            foreach (var request in new[] { request1, request2 })
            {
                recorder.BeginAsync(request).GetAwaiter().GetResult();
                for (var i = 0; i < 60; i++)
                    recorder.RecordSample(ProcessTrendFeatureTests.Sample(DateTimeOffset.Now.AddSeconds(i)) with {
                        HighVacuumPa = i is >= 20 and < 30 ? double.NaN : 13.6 + Math.Sin(i / 10d),
                        Power1VoltageV = 480 + Math.Sin(i / 10d) * 10 });
                recorder.EndAsync(RecipeRunResult.Completed(1), true, false).GetAwaiter().GetResult();
            }
            using var vm = new ProcessTrendViewModel(Dispatcher.CurrentDispatcher, new CsvTrendFileService(), new HistoryFileDialogService(), delayed);
            var page = new ProcessHistoryView { DataContext = vm, FontSize = 16, Background = Brushes.White };
            var historyTabs = new TabControl { Style = (Style)Application.Current.Resources["HistoryTabControlStyle"],
                ItemContainerStyle = (Style)Application.Current.Resources["HistoryTabItemStyle"] };
            foreach (var title in new[] { "数据曲线", "工艺记录", "操作记录", "报警记录" })
                historyTabs.Items.Add(new TabItem { Header = title, Content = title == "工艺记录" ? page : null });
            historyTabs.SelectedIndex = 1;
            var root = new HmiPageLayout { PageTitle = "历史记录", PageContent = historyTabs };
            UntilUi(() => vm.Runs.Count == 2);
            var first = vm.Runs.Single(r => r.RunId == request1.RunId);
            var second = vm.Runs.Single(r => r.RunId == request2.RunId);
            delayed.DelayedId = request1.RunId;
            vm.SelectedRun = first;
            vm.SelectedRun = second;
            UntilUi(() => vm.CurrentFileName == request2.RecipeName);
            delayed.Pending.SetResult(recorder.LoadSamplesAsync(request1.RunId).GetAwaiter().GetResult());
            Pump();
            Assert.Equal(request2.RecipeName, vm.CurrentFileName);
            Assert.True(vm.ExportCommand.CanExecute(null));
            Assert.False(vm.IsSimulationMode);
            Assert.Contains(vm.VacuumPlotModel.Series.OfType<OxyPlot.Series.LineSeries>().First().Points, p => double.IsNaN(p.Y));
            Render(root, "process-archive-selected", 1440, 900);
            Render(root, "process-archive-compact", 1280, 800);
            var tabs = Descendants<TabControl>(page).Single();
            tabs.SelectedIndex = 1;
            vm.ShowPower1Current = vm.ShowPower2Current = true;
            Render(root, "process-archive-power", 1440, 900);
            vm.StartDate = DateTime.Today.AddDays(-10); vm.EndDate = DateTime.Today.AddDays(-8);
            var query = vm.QueryCommand.ExecuteAsync(null);
            UntilUi(() => query.IsCompleted && vm.Runs.Count == 0);
            Assert.Null(vm.SelectedRun);
            Assert.Empty(vm.VacuumPlotModel.Series.OfType<OxyPlot.Series.LineSeries>().First().Points);
            vm.StartDate = DateTime.Today.AddDays(1); vm.EndDate = DateTime.Today;
            vm.QueryCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Assert.Contains("有效", vm.StatusMessage);
            ApplicationStatusViewModel.Instance.HistoryRecordingError = "数据库保存失败：磁盘空间不足；内存数据可手动导出";
            Render(root, "process-archive-storage-failure", 1440, 900);
            var warning = Assert.Single(Descendants<TextBlock>(root), t => t.Text.Contains("不影响配方执行"));
            Assert.Equal(Visibility.Visible, ((Border)warning.Parent).Visibility);
            Assert.True(warning.ActualHeight > 0);
        }
        finally
        {
            ApplicationStatusViewModel.Instance.HistoryRecordingError = "";
            recorder.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static void UntilUi(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(5)) { Pump(); Thread.Sleep(1); }
        Assert.True(condition());
    }

    private sealed class DelayedTrendRecorder(IProcessTrendRecorder inner) : IProcessTrendRecorder
    {
        public Guid? DelayedId;
        public readonly TaskCompletionSource<IReadOnlyList<Models.History.TelemetrySample>> Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event EventHandler? Changed { add => inner.Changed += value; remove => inner.Changed -= value; }
        public string RecordingError => inner.RecordingError;
        public Task BeginAsync(RecipeRunRequest request) => inner.BeginAsync(request);
        public Task EndAsync(RecipeRunResult result, bool began, bool cancelled) => inner.EndAsync(result, began, cancelled);
        public Models.History.TelemetrySample RecordSample(Models.History.TelemetrySample sample) => inner.RecordSample(sample);
        public Task<IReadOnlyList<Models.History.ProcessTrendRun>> QueryAsync(DateTimeOffset start, DateTimeOffset end) => inner.QueryAsync(start, end);
        public Task<IReadOnlyList<Models.History.TelemetrySample>> LoadSamplesAsync(Guid id) =>
            id == DelayedId ? Pending.Task : inner.LoadSamplesAsync(id);
        public Task ExportAsync(Guid id, string path) => inner.ExportAsync(id, path);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void Render(FrameworkElement view, string name, int width, int height)
    {
        view.Width = width;
        view.Height = height;
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        Pump();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.Combine(AppContext.BaseDirectory, "alarm-snapshots");
        Directory.CreateDirectory(directory);
        using var output = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(output);
    }
}
