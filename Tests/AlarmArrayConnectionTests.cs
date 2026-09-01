using Opc.Ua;
using Small_square_cavity_coating_machine.Models.Alarms;
using Small_square_cavity_coating_machine.Services.Alarms;
using Small_square_cavity_coating_machine.Services.History;
using Small_square_cavity_coating_machine.ViewModels;
using Small_square_cavity_coating_machine.ViewModels.History;
using System.IO;
using Xunit;

namespace Small_square_cavity_coating_machine.Tests;

public sealed class AlarmArrayConnectionTests
{
    internal sealed class Settings : IAlarmConnectionSettingsStore
    {
        public OpcUaAlarmOptions Options = new();
        public bool FailSave;
        public OpcUaAlarmOptions Load() => Options;
        public Task SaveAsync(OpcUaAlarmOptions options, CancellationToken token)
        {
            if (FailSave) throw new IOException("disk unavailable");
            Options = options;
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeSession : IAlarmArraySession
    {
        public bool[] Values = new bool[552];
        public Action<DataValue>? Callback;
        public readonly List<string> Calls = [];
        public bool Disposed;
        public readonly TaskCompletionSource Failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task FindArrayAsync(string name, CancellationToken token)
        {
            Assert.Equal("EQ_Alarm", name); Calls.Add("find"); return Task.CompletedTask;
        }
        public Task<DataValue> ReadAsync(CancellationToken token)
        { Calls.Add("read"); return Task.FromResult(new DataValue(new Variant(Values.ToArray()))); }
        public Task SubscribeAsync(Action<DataValue> onValue, CancellationToken token)
        { Calls.Add("subscribe"); Callback = onValue; return Task.CompletedTask; }
        public Task WaitForDisconnectAsync(CancellationToken token) => Failure.Task.WaitAsync(token);
        public void Send(Array? value, StatusCode? status = null) =>
            Callback!(new DataValue(new Variant(value)) { StatusCode = status ?? StatusCodes.Good });
        public ValueTask DisposeAsync() { Disposed = true; Calls.Add("dispose"); return ValueTask.CompletedTask; }
    }

    internal sealed class Factory(params FakeSession[] sessions) : IAlarmArraySessionFactory
    {
        private int _index;
        public int ConnectCount => _index;
        public bool Fail;
        public Task<IAlarmArraySession> ConnectAsync(OpcUaAlarmOptions options, CancellationToken token)
        {
            if (Fail) throw new IOException("connection refused");
            var index = _index++;
            if (index > 0) Assert.True(sessions[index - 1].Disposed);
            return Task.FromResult<IAlarmArraySession>(sessions[index]);
        }
    }

    internal static async Task Until(Func<bool> predicate)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(5, deadline.Token);
    }

    internal sealed class Browser : IAlarmNodeBrowser
    {
        public Dictionary<NodeId, List<AlarmBrowseNode>> Nodes = [];
        public HashSet<NodeId> Readable = [];
        public List<NodeId> Visited = [];
        public Task<IReadOnlyList<AlarmBrowseNode>> BrowseAsync(NodeId parent, CancellationToken token)
        {
            Visited.Add(parent);
            return Task.FromResult<IReadOnlyList<AlarmBrowseNode>>(Nodes.GetValueOrDefault(parent) ?? []);
        }
        public Task<bool> IsReadableArrayAsync(NodeId node, CancellationToken token) => Task.FromResult(Readable.Contains(node));
    }

    [Fact]
    public void Real_database_sparse_array_decodes_all_104_addresses()
    {
        using var temp = new AlarmFeatureTests.TemporaryDirectory();
        var path = SqliteAlarmDefinitionRepository.ExtractEmbeddedDatabase(temp.Path);
        var definitions = new SqliteAlarmDefinitionRepository(path).Load();
        var values = new bool[552]; values[500] = true; values[551] = true;
        var sample = new AlarmArrayDecoder(definitions).Decode(new DataValue(new Variant(values)), DateTimeOffset.Now);
        Assert.Equal(104, sample.Signals.Count);
        Assert.Equal(new[] { "EQ_Alarm[500]", "EQ_Alarm[551]" },
            sample.Signals.Where(s => s.IsActive == true).Select(s => s.Address));
        Assert.Empty(sample.Error);
        Assert.All(sample.Signals, s => Assert.Equal(AlarmQuality.Good, s.Quality));
    }

    [Theory]
    [InlineData("EQ_Alarm[-1]")]
    [InlineData("EQ_Alarm[]")]
    [InlineData("EQ_Alarm[2147483648]")]
    [InlineData("Alarm[0]")]
    [InlineData("EQ_Alarm[2]extra")]
    public void Invalid_array_addresses_are_not_guessed(string address) =>
        Assert.Throws<InvalidOperationException>(() => AlarmArrayDecoder.ParseIndex(address));

    [Fact]
    public void Short_arrays_bad_quality_and_invalid_values_are_unknown_not_normal()
    {
        var decoder = new AlarmArrayDecoder(AlarmFeatureTests.Definitions);
        var sample = decoder.Decode(new DataValue(new Variant(new[] { 1 })), DateTimeOffset.Now);
        Assert.True(sample.Signals[0].IsActive);
        Assert.Null(sample.Signals[1].IsActive);
        Assert.Contains("500", sample.Error);
        var values = new int[552]; values[500] = 2;
        sample = decoder.Decode(new DataValue(new Variant(values)), DateTimeOffset.Now);
        Assert.False(sample.Signals[0].IsActive);
        Assert.Null(sample.Signals[1].IsActive);
        foreach (var value in new object?[] { null, true, new double[552], new object[552], new bool[2, 300] })
        {
            sample = decoder.Decode(new DataValue(new Variant(value)), DateTimeOffset.Now);
            Assert.All(sample.Signals, s => Assert.Null(s.IsActive));
        }
        sample = decoder.Decode(new DataValue(new Variant(new bool[552])) { StatusCode = StatusCodes.BadCommunicationError },
            DateTimeOffset.Now);
        Assert.All(sample.Signals, s => Assert.Equal(AlarmQuality.Bad, s.Quality));
    }

    [Fact]
    public void Every_supported_integer_array_accepts_only_zero_and_one()
    {
        Array[] arrays = [new byte[] { 0, 1, 2 }, new sbyte[] { 0, 1, -1 }, new short[] { 0, 1, 2 },
            new ushort[] { 0, 1, 2 }, new int[] { 0, 1, 2 }, new uint[] { 0, 1, 2 },
            new long[] { 0, 1, 2 }, new ulong[] { 0, 1, 2 }];
        var definitions = Enumerable.Range(0, 3).Select(i => new AlarmDefinition(i, $"Alarm{i}", 7, $"EQ_Alarm[{i}]", "A", "", "")).ToArray();
        var decoder = new AlarmArrayDecoder(definitions);
        foreach (var values in arrays)
        {
            var sample = decoder.Decode(new DataValue(new Variant(values)), DateTimeOffset.Now);
            Assert.False(sample.Signals[0].IsActive);
            Assert.True(sample.Signals[1].IsActive);
            Assert.Null(sample.Signals[2].IsActive);
        }
    }

    [Fact]
    public async Task Discovery_is_recursive_exact_cycle_safe_and_uses_server_namespace()
    {
        foreach (ushort ns in new ushort[] { 2, 5 })
        {
            var folder = new NodeId("Program", ns);
            var array = new NodeId("vendor.actual.identifier", ns);
            var browser = new Browser
            {
                Nodes = {
                    [ObjectIds.ObjectsFolder] = [new(folder, "Program", NodeClass.Object)],
                    [folder] = [new(array, "EQ_Alarm", NodeClass.Variable), new(ObjectIds.ObjectsFolder, "cycle", NodeClass.Object)]
                },
                Readable = [array]
            };
            Assert.Equal(array, await AlarmArrayDiscovery.FindAsync(browser, "EQ_Alarm", CancellationToken.None));
            Assert.Equal(3, browser.Visited.Count);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Missing_or_ambiguous_arrays_fail_closed(int count)
    {
        var browser = new Browser();
        browser.Nodes[ObjectIds.ObjectsFolder] = [];
        for (var i = 0; i < count; i++)
        {
            var id = new NodeId($"array{i}", 2);
            browser.Nodes[ObjectIds.ObjectsFolder].Add(new(id, "EQ_Alarm", NodeClass.Variable));
            browser.Readable.Add(id);
        }
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => AlarmArrayDiscovery.FindAsync(browser, "EQ_Alarm", CancellationToken.None));
        Assert.Contains(count == 0 ? "未找到" : "多个", ex.Message);
    }

    [Fact]
    public async Task Wrong_case_or_unreadable_array_is_not_a_match()
    {
        var browser = new Browser
        {
            Nodes = { [ObjectIds.ObjectsFolder] = [
                new(new NodeId("a", 2), "eq_alarm", NodeClass.Variable),
                new(new NodeId("b", 2), "EQ_Alarm", NodeClass.Variable)] },
            Readable = [new NodeId("a", 2)]
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => AlarmArrayDiscovery.FindAsync(browser, "EQ_Alarm", CancellationToken.None));
    }

    [Theory]
    [InlineData("opc.tcp://192.168.0.10:4840")]
    [InlineData("opc.tcp://plc.local:4840/UA")]
    [InlineData("opc.tcp://[::1]:4840")]
    public void Endpoint_accepts_explicit_host_port(string value) =>
        Assert.Equal(value, OpcUaAlarmOptions.ValidateEndpoint(value));

    [Theory]
    [InlineData("192.168.0.10:4840")]
    [InlineData("opc.tcp://192.168.0.10")]
    [InlineData("opc.tcp://host:0")]
    [InlineData("opc.tcp://host:65536")]
    [InlineData("opc.tcp://user:secret@host:4840")]
    [InlineData("opc.tcp://host:4840?x=1")]
    [InlineData("opc.tcp://host:4840#x")]
    [InlineData("opc.tcp://ho st:4840")]
    public void Invalid_endpoints_rejected_before_save(string value) =>
        Assert.Throws<InvalidOperationException>(() => OpcUaAlarmOptions.ValidateEndpoint(value));

    [Fact]
    public async Task Settings_default_atomic_replace_and_restart_load()
    {
        using var temp = new AlarmFeatureTests.TemporaryDirectory();
        var path = Path.Combine(temp.Path, "alarms.opcua.json");
        var store = new AlarmConnectionSettingsStore(path);
        Assert.Equal(OpcUaAlarmOptions.DefaultEndpoint, store.Load().EndpointUrl);
        await store.SaveAsync(new(), CancellationToken.None);
        var options = new OpcUaAlarmOptions { EndpointUrl = "opc.tcp://other-plc:4840" };
        await store.SaveAsync(options, CancellationToken.None);
        Assert.Equal(options, new AlarmConnectionSettingsStore(path).Load());
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(options with { EndpointUrl = "bad" }, CancellationToken.None));
        Assert.Equal(options, store.Load());
    }

    [Fact]
    public async Task Initial_read_single_subscription_reconnect_and_lifecycle_update_both_viewmodels()
    {
        var first = new FakeSession(); first.Values[0] = true;
        var second = new FakeSession(); second.Values[0] = true;
        var settings = new Settings();
        var factory = new Factory(first, second);
        await using var source = new OpcUaAlarmSignalSource(settings, factory);
        var repository = new InMemoryAlarmLogRepository();
        using var monitor = new AlarmMonitorService(AlarmFeatureTests.Definitions, source, repository);
        using var history = new AlarmHistoryViewModel(repository, new AlarmFeatureTests.InlineDispatcher());
        using var status = new AlarmStatusViewModel(monitor, repository, new AlarmFeatureTests.InlineDispatcher(), new AlarmNavigationService());
        await source.StartAsync(AlarmFeatureTests.Definitions, CancellationToken.None);
        await Until(() => source.Status.Phase == AlarmConnectionPhase.Connected);
        var occurrence = Assert.Single(repository.LoadUncleared());
        Assert.True(occurrence.ObservedOnConnect);
        Assert.Equal(new[] { "find", "read", "subscribe" }, first.Calls);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        first.Values[500] = true; first.Send(first.Values);
        Assert.Equal(2, status.ActiveCount);
        Assert.Equal(2, history.Records.Count);
        Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(500));
        first.Send(first.Values);
        Assert.Equal(2, repository.LoadUncleared().Count);
        first.Send(new bool[552], StatusCodes.BadCommunicationError);
        Assert.All(repository.LoadUncleared(), r => Assert.Equal(AlarmQuality.Unknown, r.Quality));
        first.Send(first.Values);
        first.Values[500] = false; first.Send(first.Values);
        Assert.Single(repository.LoadUncleared());

        var sawUnknown = false;
        source.ConnectionChanged += (_, connection) =>
        {
            if (!connection.IsConnected) sawUnknown = repository.LoadUncleared().All(r => r.Quality == AlarmQuality.Unknown);
        };
        await source.ApplyEndpointAsync("opc.tcp://other-plc:4840");
        await Until(() => source.Status.Phase == AlarmConnectionPhase.Connected);
        Assert.True(first.Disposed);
        Assert.True(sawUnknown);
        Assert.Equal(occurrence.OccurrenceId, Assert.Single(repository.LoadUncleared()).OccurrenceId);
        Assert.Equal("opc.tcp://other-plc:4840", settings.Options.EndpointUrl);
        Assert.Equal(2, factory.ConnectCount);
        first.Send(new bool[552]); // Late callback from the retired session cannot clear the alarm.
        Assert.Single(repository.LoadUncleared());
        second.Values[0] = false; second.Send(second.Values);
        Assert.Empty(repository.LoadUncleared());
        second.Values[0] = true; second.Send(second.Values);
        Assert.NotEqual(occurrence.OccurrenceId, Assert.Single(repository.LoadUncleared()).OccurrenceId);
    }

    [Fact]
    public async Task Lost_session_reconnects_automatically_without_duplicate_occurrence()
    {
        var first = new FakeSession(); first.Values[0] = true;
        var second = new FakeSession(); second.Values[0] = true;
        var settings = new Settings { Options = new OpcUaAlarmOptions { ReconnectDelayMs = 1000 } };
        await using var source = new OpcUaAlarmSignalSource(settings, new Factory(first, second));
        var repository = new InMemoryAlarmLogRepository();
        using var monitor = new AlarmMonitorService(AlarmFeatureTests.Definitions, source, repository);
        await source.StartAsync(AlarmFeatureTests.Definitions, CancellationToken.None);
        await Until(() => source.Status.Phase == AlarmConnectionPhase.Connected);
        var id = Assert.Single(repository.LoadUncleared()).OccurrenceId;
        first.Failure.TrySetException(new IOException("connection lost"));
        await Until(() => source.Status.Phase == AlarmConnectionPhase.Failed);
        Assert.Equal(AlarmQuality.Unknown, Assert.Single(repository.LoadUncleared()).Quality);
        await Until(() => source.Status.Phase == AlarmConnectionPhase.Connected);
        Assert.True(first.Disposed);
        Assert.Equal(id, Assert.Single(repository.LoadUncleared()).OccurrenceId);
        Assert.Equal(AlarmQuality.Good, Assert.Single(repository.LoadUncleared()).Quality);
    }

    [Fact]
    public async Task Save_failure_stops_old_subscription_and_preserves_saved_target_and_alarms()
    {
        var session = new FakeSession(); session.Values[0] = true;
        var settings = new Settings();
        await using var source = new OpcUaAlarmSignalSource(settings, new Factory(session));
        var repository = new InMemoryAlarmLogRepository();
        using var monitor = new AlarmMonitorService(AlarmFeatureTests.Definitions, source, repository);
        await source.StartAsync(AlarmFeatureTests.Definitions, CancellationToken.None);
        await Until(() => source.Status.Phase == AlarmConnectionPhase.Connected);
        settings.FailSave = true;
        await Assert.ThrowsAsync<IOException>(() => source.ApplyEndpointAsync("opc.tcp://new:4840"));
        Assert.True(session.Disposed);
        Assert.Equal(OpcUaAlarmOptions.DefaultEndpoint, source.Status.EndpointUrl);
        Assert.Equal(AlarmConnectionPhase.Failed, source.Status.Phase);
        Assert.Contains("保存失败", source.Status.Message);
        Assert.Equal(AlarmQuality.Unknown, Assert.Single(repository.LoadUncleared()).Quality);
    }

    [Fact]
    public async Task Connection_failure_is_visible_and_new_address_remains_saved()
    {
        var settings = new Settings();
        await using var source = new OpcUaAlarmSignalSource(settings, new Factory { Fail = true });
        await source.StartAsync(AlarmFeatureTests.Definitions, CancellationToken.None);
        await Until(() => source.Status.Phase == AlarmConnectionPhase.Failed);
        await source.ApplyEndpointAsync("opc.tcp://new:4840");
        await Until(() => source.Status.Phase == AlarmConnectionPhase.Failed);
        Assert.Contains("connection refused", source.Status.Message);
        Assert.Equal("opc.tcp://new:4840", settings.Options.EndpointUrl);
    }

    [Fact]
    public async Task Settings_viewmodel_allows_guest_validates_and_marshals_status()
    {
        var dispatcher = new AlarmFeatureTests.QueueDispatcher();
        var settings = new Settings();
        await using var source = new OpcUaAlarmSignalSource(settings, new Factory { Fail = true });
        using var vm = new PlcConnectionStatusViewModel(source, dispatcher);
        Assert.True(vm.CanApply); // No account or authorization dependency.
        vm.BeginEditCommand.Execute(null);
        vm.DraftEndpoint = "bad";
        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.NotEmpty(vm.ValidationError);
        Assert.Equal(OpcUaAlarmOptions.DefaultEndpoint, source.Status.EndpointUrl);
        vm.DraftEndpoint = "opc.tcp://new:4840";
        await vm.ApplyCommand.ExecuteAsync(null);
        dispatcher.Drain();
        Assert.Equal("opc.tcp://new:4840", vm.EndpointUrl);
        var closed = false; vm.CloseRequested += (_, _) => closed = true;
        vm.DraftEndpoint = "discard";
        vm.CancelCommand.Execute(null);
        Assert.True(closed);
        Assert.Equal(vm.EndpointUrl, vm.DraftEndpoint);
        await source.StartAsync(AlarmFeatureTests.Definitions, CancellationToken.None);
        await Until(() => source.Status.Phase == AlarmConnectionPhase.Failed);
        dispatcher.Drain();
        Assert.Equal("PLC连接失败", vm.StatusText);
        Assert.Contains("connection refused", vm.DetailText);
    }
}
