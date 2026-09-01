using Microsoft.Data.Sqlite;
using Opc.Ua;
using Small_square_cavity_coating_machine.Models.Equipment;
using Small_square_cavity_coating_machine.Models.History;
using Small_square_cavity_coating_machine.Models.Recipes;
using Small_square_cavity_coating_machine.Services.Equipment;
using Small_square_cavity_coating_machine.Services.History;
using Small_square_cavity_coating_machine.Services.Recipes;
using Small_square_cavity_coating_machine.ViewModels.History;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using Xunit;

namespace Small_square_cavity_coating_machine.Tests;

public sealed class ProcessTrendFeatureTests
{
    internal static IReadOnlyList<ProcessDefinition> Definitions(string path) => new SqliteProcessDefinitionRepository(path).Load();
    internal static TelemetrySample Sample(DateTimeOffset? time = null) =>
        new(time ?? DateTimeOffset.Now, "session", "", "", 1, 2, 3, 4, 5, 6, 7, "Good");

    [Fact]
    public async Task Definitions_are_read_only_validate_identity_and_use_Address_not_Id()
    {
        await using var h = new EquipmentFeatureTests.Harness();
        var hash = SHA256.HashData(File.ReadAllBytes(h.DefinitionsPath));
        var definitions = Definitions(h.DefinitionsPath);
        Assert.Equal(Enumerable.Range(1, 7).Select(i => $"EQ_Process[{i}]"), definitions.Select(p => p.Address));
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(h.DefinitionsPath)));
        SqliteProcessDefinitionRepository.Validate(definitions.Reverse().ToArray());
        Assert.Throws<InvalidOperationException>(() => SqliteProcessDefinitionRepository.Validate(definitions.Skip(1).ToArray()));
        Assert.Throws<InvalidOperationException>(() => SqliteProcessDefinitionRepository.Validate(definitions.Select(p => p.Id == 1 ? p with { Address = definitions[0].Address } : p).ToArray()));
        Assert.Throws<InvalidOperationException>(() => SqliteProcessDefinitionRepository.Validate(definitions.Select(p => p.Id == 0 ? p with { DataType = "BOOL" } : p).ToArray()));
        Assert.Throws<InvalidOperationException>(() => SqliteProcessDefinitionRepository.Validate(definitions.Select(p => p.Id == 0 ? p with { Id = 1 } : p).ToArray()));

        var changed = definitions.Select(p => p.Id == 0 ? p with { Address = "EQ_Process[8]" } : p).Reverse().ToArray();
        h.Session.Values[EquipmentGroups.Process] = new float[] { 999, 1, 2, 3, 4, 5, 6, 7, 88 };
        await using var client = new OpcUaEquipmentClient(h.Settings, h.Factory, h.Alarms, h.Io, h.Parameters, h.Auth, h.Runtime,
            processDefinitions: changed);
        await client.StartAsync(CancellationToken.None);
        await AlarmArrayConnectionTests.Until(() => client.Snapshot().Groups[EquipmentGroups.Process].IsReady);
        await using var source = new OpcUaTelemetrySource(client, changed);
        var sample = source.Capture(DateTimeOffset.Now);
        Assert.Equal(88, sample.HighVacuumPa);
        Assert.Equal(2, sample.FilmHighVacuumPa);
        Assert.Equal(7, sample.TemperatureC);
        Assert.Empty(h.Session.Writes);
    }

    [Fact]
    public async Task Shared_process_snapshot_samples_constants_each_second_and_does_not_reuse_bad_values()
    {
        await using var h = new EquipmentFeatureTests.Harness();
        var definitions = Definitions(h.DefinitionsPath);
        h.Session.Values[EquipmentGroups.Process] = new float[] { 999, 1, 2, 3, 4, 5, 6, 7 };
        await using var client = new OpcUaEquipmentClient(h.Settings, h.Factory, h.Alarms, h.Io, h.Parameters, h.Auth, h.Runtime,
            processDefinitions: definitions);
        await using var source = new OpcUaTelemetrySource(client, definitions);
        Assert.True(double.IsNaN(source.Capture(DateTimeOffset.Now).HighVacuumPa));
        await client.StartAsync(CancellationToken.None);
        await AlarmArrayConnectionTests.Until(() => client.Snapshot().Groups[EquipmentGroups.Process].IsReady);
        var samples = new ConcurrentQueue<TelemetrySample>();
        source.SampleReceived += (_, s) => samples.Enqueue(s);
        await source.StartAsync(CancellationToken.None);
        await AlarmArrayConnectionTests.Until(() => samples.Count >= 2);
        var captured = samples.ToArray();
        Assert.Equal(1, captured[0].HighVacuumPa);
        Assert.Equal(captured[0].HighVacuumPa, captured[1].HighVacuumPa);
        Assert.InRange((captured[1].Timestamp - captured[0].Timestamp).TotalSeconds, 0.7, 1.5);
        Assert.Equal(1, h.Factory.ConnectCount);
        Assert.All(client.Snapshot().Groups[EquipmentGroups.Process].Points.Values, p => Assert.False(p.CanWrite));
        h.Session.Send(EquipmentGroups.Process, StatusCodes.BadCommunicationError);
        Assert.True(double.IsNaN(source.Capture(DateTimeOffset.Now).HighVacuumPa));
        h.Session.Values[EquipmentGroups.Process] = new float[] { 0, 90000, 2, 3, float.NaN, 5, 6, 7 };
        h.Session.Send(EquipmentGroups.Process);
        var partial = source.Capture(DateTimeOffset.Now);
        Assert.Equal(90000, partial.HighVacuumPa); // No clipping to the definition's 15000 maximum.
        Assert.True(double.IsNaN(partial.Power1CurrentA));
        Assert.Equal(7, partial.TemperatureC);
        h.Session.Values[EquipmentGroups.Process] = new float[] { 0, 1 };
        h.Session.Send(EquipmentGroups.Process);
        Assert.True(double.IsNaN(source.Capture(DateTimeOffset.Now).TemperatureC));
        h.Session.Failure.TrySetResult();
        await AlarmArrayConnectionTests.Until(() => !client.Snapshot().IsConnected);
        Assert.True(double.IsNaN(source.Capture(DateTimeOffset.Now).HighVacuumPa));
        Assert.Empty(h.Session.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Wrong_array_type_or_subscription_rejection_does_not_disable_other_groups(bool rejected)
    {
        await using var h = new EquipmentFeatureTests.Harness();
        var definitions = Definitions(h.DefinitionsPath);
        h.Session.Values[EquipmentGroups.Process] = rejected ? new float[8] : new int[8];
        if (rejected) h.Session.SubscriptionErrors[EquipmentGroups.Process] = "test rejected";
        await using var client = new OpcUaEquipmentClient(h.Settings, h.Factory, h.Alarms, h.Io, h.Parameters, h.Auth, h.Runtime,
            processDefinitions: definitions);
        await client.StartAsync(CancellationToken.None);
        await AlarmArrayConnectionTests.Until(() => client.Snapshot().IsConnected);
        Assert.True(client.Snapshot().Groups[EquipmentGroups.Parameter].IsReady);
        Assert.False(client.Snapshot().Groups[EquipmentGroups.Process].IsReady);
        await using var source = new OpcUaTelemetrySource(client, definitions);
        Assert.True(double.IsNaN(source.Capture(DateTimeOffset.Now).TemperatureC));
    }

    [Fact]
    public async Task Reconnect_uses_new_epoch_and_accepts_double_process_values()
    {
        var first = new EquipmentFeatureTests.Session();
        var second = new EquipmentFeatureTests.Session();
        first.Values[EquipmentGroups.Process] = new float[] { 999, 1, 2, 3, 4, 5, 6, 7 };
        second.Values[EquipmentGroups.Process] = new double[] { 999, 11, 12, 13, 14, 15, 16, 17 };
        await using var h = new EquipmentFeatureTests.Harness(first, second);
        var definitions = Definitions(h.DefinitionsPath);
        await using var client = new OpcUaEquipmentClient(h.Settings, h.Factory, h.Alarms, h.Io, h.Parameters, h.Auth, h.Runtime,
            processDefinitions: definitions);
        await using var source = new OpcUaTelemetrySource(client, definitions);
        await client.StartAsync(CancellationToken.None);
        await AlarmArrayConnectionTests.Until(() => client.Snapshot().Groups[EquipmentGroups.Process].IsReady);
        var epoch = client.Snapshot().Epoch;
        first.Failure.TrySetResult();
        await AlarmArrayConnectionTests.Until(() => !client.Snapshot().IsConnected);
        Assert.True(double.IsNaN(source.Capture(DateTimeOffset.Now).HighVacuumPa));
        await AlarmArrayConnectionTests.Until(() => source.Capture(DateTimeOffset.Now).HighVacuumPa == 11);
        Assert.True(client.Snapshot().Epoch > epoch);
        first.Send(EquipmentGroups.Process); // An old session's late notification must not win.
        Assert.Equal(17, source.Capture(DateTimeOffset.Now).TemperatureC);
    }

    [Fact]
    public async Task Simulation_uses_same_process_mapping_without_opening_a_real_session()
    {
        await using var h = new EquipmentFeatureTests.Harness();
        var definitions = Definitions(h.DefinitionsPath);
        var factory = new SimulatedEquipmentSessionFactory(h.Parameters, definitions);
        await using var client = new OpcUaEquipmentClient(new SimulationConnectionSettings(), factory,
            h.Alarms, h.Io, h.Parameters, h.Auth, h.Runtime, isSimulated: true, processDefinitions: definitions);
        await using var source = new OpcUaTelemetrySource(client, definitions);
        await client.StartAsync(CancellationToken.None);
        await AlarmArrayConnectionTests.Until(() => source.Capture(DateTimeOffset.Now).TemperatureC > 100);
        Assert.Equal("Simulation:Good", source.Capture(DateTimeOffset.Now).DataQuality);
        factory.Disconnect();
        await AlarmArrayConnectionTests.Until(() => !client.Snapshot().IsConnected);
        Assert.True(double.IsNaN(source.Capture(DateTimeOffset.Now).TemperatureC));
    }

    [Theory]
    [InlineData(RecipeDispatchMode.All, "complete", "正常完成")]
    [InlineData(RecipeDispatchMode.Selected, "complete", "正常完成")]
    [InlineData(RecipeDispatchMode.All, "begin", "启动失败")]
    [InlineData(RecipeDispatchMode.All, "abort", "异常中止")]
    [InlineData(RecipeDispatchMode.Selected, "cancel", "已取消")]
    public async Task Dispatch_archives_each_lifecycle_without_changing_recipe_protocol(RecipeDispatchMode mode, string failure, string status)
    {
        using var temp = new AlarmFeatureTests.TemporaryDirectory();
        var repository = new SqliteProcessTrendRepository(Path.Combine(temp.Path, "trend.db"));
        await using var recorder = new ProcessTrendRecorder(repository, new CsvTrendFileService(), Path.Combine(temp.Path, "csv"), []);
        using var cancellation = new CancellationTokenSource();
        var gateway = new Gateway(recorder, failure, cancellation);
        var dispatch = new RecipeDispatchService(gateway, new InMemoryOperationLogRepository(), trendRecorder: recorder);
        var request = new RecipeRunRequest(mode, [new RecipeLayer { Sequence = 3 }], "3") { RecipeName = "同名配方" };
        var result = await dispatch.RunAsync(request, new Progress<RecipeRunProgress>(), cancellation.Token);
        var run = Assert.Single(await recorder.QueryAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
        Assert.Equal(request.RunId, run.RunId);
        Assert.Equal(status, run.Status);
        Assert.Equal(failure == "complete", result.IsCompleted);
        Assert.NotNull(run.EndedAt);
        Assert.True(File.Exists(run.ExportPath));
        var samples = await recorder.LoadSamplesAsync(run.RunId);
        Assert.All(samples, s => { Assert.Equal(request.RunId.ToString(), s.RecipeRunId); Assert.Equal(request.RecipeName, s.RecipeName); });
        var csv = await new CsvTrendFileService().LoadAsync(run.ExportPath);
        Assert.Equal(samples, csv);
        Assert.Equal(samples, repository.LoadSamples(run.RunId));
    }

    [Fact]
    public async Task Preflight_rejection_creates_no_archive()
    {
        using var temp = new AlarmFeatureTests.TemporaryDirectory();
        await using var recorder = new ProcessTrendRecorder(new SqliteProcessTrendRepository(Path.Combine(temp.Path, "trend.db")),
            new CsvTrendFileService(), temp.Path, []);
        using var cancellation = new CancellationTokenSource();
        var dispatch = new RecipeDispatchService(new Gateway(recorder, "preflight", cancellation), new InMemoryOperationLogRepository(), trendRecorder: recorder);
        var result = await dispatch.RunAsync(new(RecipeDispatchMode.All, [new RecipeLayer()], "1"), new Progress<RecipeRunProgress>(), CancellationToken.None);
        Assert.False(result.IsCompleted);
        Assert.Empty(await recorder.QueryAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
    }

    [Fact]
    public async Task Disk_failure_keeps_memory_allows_manual_export_and_does_not_abort_dispatch()
    {
        using var temp = new AlarmFeatureTests.TemporaryDirectory();
        var repository = new FailingRepository(new SqliteProcessTrendRepository(Path.Combine(temp.Path, "trend.db")));
        var blockedDirectory = Path.Combine(temp.Path, "not-a-directory");
        File.WriteAllText(blockedDirectory, "block automatic export");
        await using var recorder = new ProcessTrendRecorder(repository, new CsvTrendFileService(), blockedDirectory, []);
        using var cancellation = new CancellationTokenSource();
        var dispatch = new RecipeDispatchService(new Gateway(recorder, "complete", cancellation), new InMemoryOperationLogRepository(), trendRecorder: recorder);
        var result = await dispatch.RunAsync(new(RecipeDispatchMode.All, [new RecipeLayer()], "1"), new Progress<RecipeRunProgress>(), CancellationToken.None);
        Assert.True(result.IsCompleted);
        var run = Assert.Single(await recorder.QueryAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
        Assert.Contains("数据库保存失败", run.RecordingError);
        Assert.Contains("CSV导出失败", run.RecordingError);
        Assert.Single(await recorder.LoadSamplesAsync(run.RunId));
        repository.Fail = false;
        var manual = Path.Combine(temp.Path, "manual.csv");
        await recorder.ExportAsync(run.RunId, manual);
        Assert.Single(await new CsvTrendFileService().LoadAsync(manual));
        Assert.Single(repository.Inner.LoadSamples(run.RunId));
        Assert.Empty(Assert.Single(await recorder.QueryAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)).RecordingError);
    }

    [Fact]
    public async Task Same_names_cross_midnight_and_interrupted_runs_survive_restart_with_missing_samples()
    {
        using var temp = new AlarmFeatureTests.TemporaryDirectory();
        var path = Path.Combine(temp.Path, "trend.db");
        var repository = new SqliteProcessTrendRepository(path);
        var start = new DateTimeOffset(2026, 8, 28, 23, 59, 59, TimeSpan.FromHours(8));
        var run = new ProcessTrendRun(Guid.NewGuid(), "同名", start, null, "运行中", "", false, "[]", 1);
        var sample = Sample(start) with { HighVacuumPa = double.NaN, DataQuality = "Bad", RecipeRunId = run.RunId.ToString() };
        repository.Save(run, [sample], 0);
        repository.Save(run, [sample], 0); // Retry is idempotent.
        var second = run with { RunId = Guid.NewGuid(), StartedAt = start.AddSeconds(2), EndedAt = start.AddMinutes(5), Status = "正常完成" };
        repository.Save(second, [], 0);
        var reopened = new SqliteProcessTrendRepository(path);
        var previousDay = Assert.Single(reopened.Query(start.Date, start.Date.AddDays(1)));
        Assert.Equal("程序中断", previousDay.Status);
        Assert.Contains("未确认", previousDay.FailureReason);
        Assert.Equal(second.RunId, Assert.Single(reopened.Query(start.Date.AddDays(1), start.Date.AddDays(2))).RunId);
        Assert.Equal(sample, Assert.Single(reopened.LoadSamples(run.RunId)));
        var csv = Path.Combine(temp.Path, "old-format.csv");
        await new CsvTrendFileService().SaveAsync(csv, [sample]);
        Assert.Equal(sample, Assert.Single(await new CsvTrendFileService().LoadAsync(csv)));
    }

    [Fact]
    public void Downsampling_preserves_per_series_gap_boundaries_and_full_source()
    {
        var samples = Enumerable.Range(0, 20000).Select(i => Sample(DateTimeOffset.Now.AddSeconds(i))).ToArray();
        samples[133] = samples[133] with { Power2CurrentA = double.NaN };
        var display = ProcessTrendViewModel.Downsample(samples, 100);
        Assert.Contains(samples[132], display);
        Assert.Contains(samples[133], display);
        Assert.Contains(samples[134], display);
        Assert.Equal(samples[^1], display[^1]);
        Assert.Equal(20000, samples.Length);
    }

    [Fact]
    public async Task Previously_failed_csv_can_be_exported_after_restart_and_status_is_updated()
    {
        using var temp = new AlarmFeatureTests.TemporaryDirectory();
        var repository = new SqliteProcessTrendRepository(Path.Combine(temp.Path, "trends.db"));
        var run = new ProcessTrendRun(Guid.NewGuid(), "导出重试", DateTimeOffset.Now, DateTimeOffset.Now,
            "正常完成", "", false, "[]", 1, RecordingError: "CSV导出失败：disk full");
        repository.Save(run, [Sample()], 0);
        await using var recorder = new ProcessTrendRecorder(repository, new CsvTrendFileService(), temp.Path, []);
        var output = Path.Combine(temp.Path, "retry.csv");
        await recorder.ExportAsync(run.RunId, output);
        var saved = Assert.Single(await recorder.QueryAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
        Assert.Equal(output, saved.ExportPath);
        Assert.Empty(saved.RecordingError);
        Assert.Single(await new CsvTrendFileService().LoadAsync(output));
    }

    private sealed class FailingRepository(IProcessTrendRepository inner) : IProcessTrendRepository
    {
        public bool Fail = true;
        public IProcessTrendRepository Inner => inner;
        public void Save(ProcessTrendRun run, IReadOnlyList<TelemetrySample> samples, int firstSequence)
        { if (Fail) throw new IOException("disk full"); inner.Save(run, samples, firstSequence); }
        public IReadOnlyList<ProcessTrendRun> Query(DateTimeOffset start, DateTimeOffset end) => inner.Query(start, end);
        public IReadOnlyList<TelemetrySample> LoadSamples(Guid id) => inner.LoadSamples(id);
    }

    private sealed class Gateway(IProcessTrendRecorder recorder, string failure, CancellationTokenSource cancellation) : IRecipePlcGateway
    {
        public bool IsAvailable => true;
        public bool IsSimulated => false;
        public Task<RecipeGatewayPreflightResult> PreflightAsync(CancellationToken token) => Task.FromResult(failure == "preflight"
            ? RecipeGatewayPreflightResult.Blocked("preflight failed") : RecipeGatewayPreflightResult.Ready);
        public Task BeginRunAsync(RecipeRunRequest request, CancellationToken token)
        { if (failure == "begin") throw new IOException("begin failed"); return Task.CompletedTask; }
        public Task SendLayerAsync(RecipeLayer layer, CancellationToken token)
        {
            recorder.RecordSample(Sample());
            if (failure == "abort") throw new IOException("layer failed");
            if (failure == "cancel") { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
            return Task.CompletedTask;
        }
        public Task WaitForLayerCompletedAsync(CancellationToken token) => Task.CompletedTask;
        public Task CompleteRunAsync(CancellationToken token) => Task.CompletedTask;
    }
}
