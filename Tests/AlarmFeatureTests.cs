using Opc.Ua;
using Small_square_cavity_coating_machine.Models.Alarms;
using Small_square_cavity_coating_machine.Models.History;
using Small_square_cavity_coating_machine.Services.Alarms;
using Small_square_cavity_coating_machine.Services.History;
using Small_square_cavity_coating_machine.ViewModels.History;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Xunit;

namespace Small_square_cavity_coating_machine.Tests;

public sealed class AlarmFeatureTests
{
    internal static readonly AlarmDefinition[] Definitions =
    [
        new(0, "Alarm0", 7, "EQ_Alarm[0]", "PCC1_TP报警", "PCC1_TP报警", "PCC1_TP_Error"),
        new(500, "Alarm500", 6, "EQ_Alarm[500]", "PCC1_TP警告", "PCC1_TP警告", "PCC1_TP_Warning")
    ];

    internal sealed class InlineDispatcher : IUiDispatcher { public void Post(Action action) => action(); }
    internal sealed class QueueDispatcher : IUiDispatcher
    {
        private readonly ConcurrentQueue<Action> _actions = new();
        public void Post(Action action) => _actions.Enqueue(action);
        public void Drain() { while (_actions.TryDequeue(out var action)) action(); }
    }

    internal sealed class TestSource(bool simulated = true) : IAlarmSignalSource
    {
        public bool IsSimulated => simulated;
        public event EventHandler<AlarmSignal>? SignalReceived;
        public event EventHandler<AlarmConnection>? ConnectionChanged;
        public Task StartAsync(IReadOnlyList<AlarmDefinition> definitions, CancellationToken cancellationToken) => Task.CompletedTask;
        public void Connect(bool connected = true) => ConnectionChanged?.Invoke(this, new AlarmConnection(connected, connected ? "测试已连接" : "测试断线"));
        public void Send(string address, bool? active, DateTimeOffset? time = null, AlarmQuality quality = AlarmQuality.Good)
            => SignalReceived?.Invoke(this, new AlarmSignal(address, active, quality, time ?? DateTimeOffset.Now));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "coating-alarm-tests-" + Guid.NewGuid());
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }

    [Fact]
    public void Embedded_definition_is_readonly_complete_and_keeps_sparse_addresses()
    {
        using var temp = new TemporaryDirectory();
        var path = SqliteAlarmDefinitionRepository.ExtractEmbeddedDatabase(temp.Path);
        var before = SHA256.HashData(File.ReadAllBytes(path));
        var definitions = new SqliteAlarmDefinitionRepository(path).Load();
        Assert.Equal(104, definitions.Count);
        Assert.Equal("EQ_Alarm[500]", definitions.Single(d => d.Id == 500).Address);
        Assert.Equal("PCC1_TP报警", definitions[0].DisplayMessage);
        Assert.StartsWith("预留报警点触发", definitions.Single(d => d.Id == 22).DisplayMessage);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
        Assert.Equal(path, SqliteAlarmDefinitionRepository.ExtractEmbeddedDatabase(temp.Path));
    }

    [Fact]
    public void Message_fallback_and_unknown_priority_do_not_invent_semantics()
    {
        var d = new AlarmDefinition(1, "A", 99, "X", "message", "", "english");
        Assert.Equal("message", d.DisplayMessage);
        Assert.Equal("等级 99", d.Severity);
        Assert.Equal("A", (d with { Message = "", English = "" }).DisplayMessage);
        Assert.Contains("X", (d with { Chinese = "空", Message = "空", English = "Space" }).DisplayMessage);
    }

    [Fact]
    public void Trigger_repeat_clear_retrigger_updates_both_views_without_query()
    {
        var source = new TestSource();
        var repository = new InMemoryAlarmLogRepository();
        using var monitor = new AlarmMonitorService(Definitions, source, repository);
        using var history = new AlarmHistoryViewModel(repository, new InlineDispatcher());
        using var status = new AlarmStatusViewModel(monitor, repository, new InlineDispatcher(), new AlarmNavigationService());
        source.Connect();
        foreach (var definition in Definitions) source.Send(definition.Address, false);
        Assert.Contains("无活动报警", status.SummaryText);
        var clock = Stopwatch.StartNew();
        source.Send(Definitions[0].Address, true);
        Assert.Single(history.Records);
        Assert.Equal(1, status.ActiveCount);
        Assert.Contains(Definitions[0].Chinese, status.SummaryText);
        Assert.True(clock.Elapsed < TimeSpan.FromMilliseconds(500));
        var occurrence = history.Records[0].Record.OccurrenceId;
        source.Send(Definitions[0].Address, true);
        Assert.Single(history.Records);
        Assert.False(history.Records[0].Record.ObservedOnConnect);
        source.Send(Definitions[0].Address, false);
        Assert.Equal(occurrence, history.Records[0].Record.OccurrenceId);
        Assert.Equal("已清除", history.Records[0].Record.StatusText);
        Assert.NotNull(history.Records[0].Record.ClearedAt);
        Assert.Equal(0, status.ActiveCount);
        source.Send(Definitions[0].Address, true);
        Assert.Equal(2, history.Records.Count);
        Assert.NotEqual(occurrence, history.Records[0].Record.OccurrenceId);
    }

    [Fact]
    public void Footer_prioritizes_severity_then_recency_and_navigation_is_a_command()
    {
        var source = new TestSource();
        var repository = new InMemoryAlarmLogRepository();
        using var monitor = new AlarmMonitorService(Definitions, source, repository);
        var navigation = new AlarmNavigationService();
        var requested = false;
        navigation.HistoryRequested += (_, _) => requested = true;
        using var status = new AlarmStatusViewModel(monitor, repository, new InlineDispatcher(), navigation);
        source.Connect();
        source.Send(Definitions[0].Address, true);
        source.Send(Definitions[1].Address, true);
        Assert.Equal(2, status.ActiveCount);
        Assert.Contains("报警：PCC1_TP报警", status.SummaryText);
        Assert.Equal("#C62828", status.IndicatorColor);
        source.Send(Definitions[0].Address, false);
        Assert.Contains("警告：PCC1_TP警告", status.SummaryText);
        Assert.Equal("#B45A00", status.IndicatorColor);
        status.ShowHistoryCommand.Execute(null);
        Assert.True(requested);
    }

    [Fact]
    public void Initial_active_is_observed_not_invented_and_disconnect_never_clears_it()
    {
        var source = new TestSource();
        var repository = new InMemoryAlarmLogRepository();
        using var monitor = new AlarmMonitorService(Definitions, source, repository);
        source.Connect();
        source.Send(Definitions[0].Address, true);
        var initial = Assert.Single(repository.LoadUncleared());
        Assert.True(initial.ObservedOnConnect);
        source.Connect(false);
        source.Send(Definitions[0].Address, false); // stale callback after disconnect
        Assert.Null(Assert.Single(repository.LoadUncleared()).ClearedAt);
        Assert.Equal(AlarmQuality.Unknown, monitor.Snapshot().ActiveAlarms[0].Quality);
        source.Connect();
        source.Send(Definitions[0].Address, true);
        Assert.Equal(initial.OccurrenceId, Assert.Single(repository.LoadUncleared()).OccurrenceId);
        source.Send(Definitions[0].Address, null, quality: AlarmQuality.Bad);
        Assert.Equal("状态未知", monitor.Snapshot().ActiveAlarms[0].StatusText);
        source.Send(Definitions[0].Address, false);
        var cleared = Assert.Single(repository.Query(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
        Assert.True(cleared.RecoveryObservedAfterGap);
        Assert.NotNull(cleared.ClearedAt);
    }

    [Fact]
    public void All_normal_requires_every_valid_signal_and_connection()
    {
        var source = new TestSource();
        var repository = new InMemoryAlarmLogRepository();
        using var monitor = new AlarmMonitorService(Definitions, source, repository);
        using var status = new AlarmStatusViewModel(monitor, repository, new InlineDispatcher(), new AlarmNavigationService());
        source.Connect();
        source.Send(Definitions[0].Address, false);
        Assert.Contains("未确认", status.SummaryText);
        source.Send(Definitions[1].Address, false);
        Assert.Contains("无活动报警", status.SummaryText);
        source.Send(Definitions[1].Address, null, quality: AlarmQuality.Bad);
        Assert.Contains("未确认", status.SummaryText);
        source.Connect(false);
        Assert.Contains("未确认", status.SummaryText);
    }

    [Fact]
    public void Historical_query_keeps_range_and_pins_new_outside_alarm_then_updates_same_row()
    {
        var repository = new InMemoryAlarmLogRepository();
        var older = new AlarmLogRecord(DateTimeOffset.Now.AddDays(-10), "报警", "A", "旧报警", DateTimeOffset.Now.AddDays(-9), true);
        repository.Add(older);
        using var history = new AlarmHistoryViewModel(repository, new InlineDispatcher());
        history.StartDate = DateTime.Today.AddDays(-12);
        history.EndDate = DateTime.Today.AddDays(-8);
        history.QueryCoreCommand.Execute(null);
        Assert.False(history.IsLiveMode);
        Assert.Single(history.Records);
        var current = new AlarmLogRecord(DateTimeOffset.Now, "报警", "B", "新报警", null, true);
        repository.Add(current);
        Assert.Equal(2, history.Records.Count);
        Assert.True(history.Records[0].IsOutsideRange);
        Assert.Contains("实时新增·范围外", history.Records[0].RowNote);
        Assert.Contains("查询结果 1 条；实时新增 1 条", history.StatusMessage);
        repository.Upsert(current with { ClearedAt = DateTimeOffset.Now });
        Assert.Equal(2, history.Records.Count);
        Assert.Equal("已清除", history.Records[0].Record.StatusText);
        Assert.Equal(DateTime.Today.AddDays(-8), history.EndDate);
        history.QueryCoreCommand.Execute(null);
        Assert.Single(history.Records);
        history.ReturnToLiveCommand.Execute(null);
        Assert.True(history.IsLiveMode);
        Assert.Equal(current.OccurrenceId, Assert.Single(history.Records).Record.OccurrenceId);
    }

    [Fact]
    public async Task Background_callbacks_are_marshaled_and_queued_updates_cannot_regress_a_query()
    {
        var dispatcher = new QueueDispatcher();
        var repository = new InMemoryAlarmLogRepository();
        using var history = new AlarmHistoryViewModel(repository, dispatcher);
        var row = new AlarmLogRecord(DateTimeOffset.Now, "报警", "A", "后台报警", null, true);
        await Task.Run(() => repository.Add(row));
        Assert.Empty(history.Records);
        await Task.Run(() => repository.Upsert(row with { ClearedAt = DateTimeOffset.Now }));
        history.ReturnToLiveCommand.Execute(null);
        dispatcher.Drain();
        Assert.Equal("已清除", Assert.Single(history.Records).Record.StatusText);
        history.Dispose();
        repository.Add(row with { OccurrenceId = Guid.NewGuid() });
        dispatcher.Drain();
        Assert.Single(history.Records);
    }

    [Fact]
    public async Task Persistence_survives_restart_and_reconciles_uncleared_without_duplicates()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "history.db");
        Guid id;
        await using (var repository = new SqliteAlarmLogRepository(path))
        {
            var source = new TestSource(false);
            using var monitor = new AlarmMonitorService(Definitions, source, repository);
            source.Connect(); source.Send(Definitions[0].Address, true);
            id = Assert.Single(repository.LoadUncleared()).OccurrenceId;
            await repository.FlushAsync();
        }
        await using (var repository = new SqliteAlarmLogRepository(path))
        {
            var source = new TestSource(false);
            using var monitor = new AlarmMonitorService(Definitions, source, repository);
            Assert.Equal(AlarmQuality.Unknown, Assert.Single(monitor.Snapshot().ActiveAlarms).Quality);
            source.Connect(); source.Send(Definitions[0].Address, true);
            Assert.Equal(id, Assert.Single(repository.LoadUncleared()).OccurrenceId);
            source.Send(Definitions[0].Address, false);
            await repository.FlushAsync();
        }
        await using var reopened = new SqliteAlarmLogRepository(path);
        Assert.Empty(reopened.LoadUncleared());
        Assert.Equal(id, Assert.Single(reopened.Query(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)).OccurrenceId);
    }

    [Fact]
    public async Task Storage_failure_keeps_alarm_visible_and_retries_after_unlock()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "history.db");
        await using var repository = new SqliteAlarmLogRepository(path);
        var source = new TestSource(false);
        using var monitor = new AlarmMonitorService(Definitions, source, repository);
        using var history = new AlarmHistoryViewModel(repository, new InlineDispatcher());
        using var status = new AlarmStatusViewModel(monitor, repository, new InlineDispatcher(), new AlarmNavigationService());
        source.Connect();
        using (var fileLock = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            source.Send(Definitions[0].Address, true);
            Assert.Single(history.Records);
            Assert.Equal(1, status.ActiveCount);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await Assert.ThrowsAnyAsync<Exception>(() => repository.FlushAsync(timeout.Token));
            Assert.Contains("保存失败", repository.StorageError);
        }
        await repository.FlushAsync();
        Assert.Empty(repository.StorageError);
    }

    [Fact]
    public void Real_source_does_not_reactivate_simulation_history()
    {
        var repository = new InMemoryAlarmLogRepository();
        repository.Add(new AlarmLogRecord(DateTimeOffset.Now, "报警", "A", "模拟", null, true) { Address = Definitions[0].Address });
        using var monitor = new AlarmMonitorService(Definitions, new TestSource(false), repository);
        Assert.Empty(monitor.Snapshot().ActiveAlarms);
        Assert.False(monitor.Snapshot().IsSimulated);
    }

    [Theory]
    [InlineData("Boolean", true, true)]
    [InlineData("Boolean", false, false)]
    [InlineData("Int32", 1, true)]
    [InlineData("Int32", 0, false)]
    public void Opc_values_require_confirmed_type_and_polarity(string type, object value, bool expected)
    {
        Assert.NotEmpty(type);
        Assert.Equal(expected, AlarmArrayDecoder.DecodeElement(value));
    }

    [Fact]
    public void Opc_unknown_types_values_and_missing_config_fail_closed()
    {
        Assert.Null(AlarmArrayDecoder.DecodeElement(1.0));
        Assert.Null(AlarmArrayDecoder.DecodeElement(2));
        Assert.Null(AlarmArrayDecoder.DecodeElement(null));
        Assert.Throws<InvalidOperationException>(() => new OpcUaAlarmOptions { EndpointUrl = "" }.Validate());
    }

    [Fact]
    public void Array_address_is_not_row_index()
    {
        Assert.Equal(500, AlarmArrayDecoder.ParseIndex("EQ_Alarm[500]"));
        Assert.Throws<InvalidOperationException>(() => AlarmArrayDecoder.ParseIndex("MAIN.EQ_Alarm[500]"));
    }

    [Fact]
    public async Task Unconfigured_source_never_reports_connected_or_emits_alarms()
    {
        await using var source = new UnavailableAlarmSignalSource("未配置");
        var connections = new List<AlarmConnection>();
        var signals = new List<AlarmSignal>();
        source.ConnectionChanged += (_, state) => connections.Add(state);
        source.SignalReceived += (_, signal) => signals.Add(signal);
        await source.StartAsync(Definitions, CancellationToken.None);
        Assert.False(Assert.Single(connections).IsConnected);
        Assert.Empty(signals);
    }
}
