using Opc.Ua;
using Small_square_cavity_coating_machine.Models.Equipment;
using Small_square_cavity_coating_machine.Models.Recipes;
using Small_square_cavity_coating_machine.Services.Equipment;
using Small_square_cavity_coating_machine.Services.Recipes;
using Small_square_cavity_coating_machine.Services.Alarms;
using Small_square_cavity_coating_machine.Services.History;
using System.Collections.Concurrent;
using Xunit;

namespace Small_square_cavity_coating_machine.Tests;

public sealed class RecipeProtocolTests
{
    internal sealed class Session : IEquipmentSession
    {
        public readonly EquipmentFeatureTests.Session Base = new();
        public Array Recipe = Enumerable.Repeat(77f, 24).ToArray();
        public object RecipeOk = true, CoatOk = true;
        public bool AutoComplete = true, FastComplete, SuppressZero, Mismatch, FailFinal, Missing, ReadOnly;
        public readonly ConcurrentQueue<string> Calls = new();
        public readonly ConcurrentQueue<(string Group, object Value)> Writes = new();
        public Func<string, object, CancellationToken, Task>? Writing;
        public Action<string, DataValue>? Callback;
        private DateTime _stamp = DateTime.UtcNow;
        private DateTime Stamp() => _stamp = DateTime.UtcNow > _stamp ? DateTime.UtcNow : _stamp.AddTicks(1);
        public async Task<IReadOnlyDictionary<string, EquipmentBinding>> DiscoverAsync(CancellationToken token)
        {
            var result = new Dictionary<string, EquipmentBinding>(await Base.DiscoverAsync(token));
            foreach (var group in EquipmentGroups.RecipePoints)
                result[group] = new(group, !Missing, !ReadOnly, group == EquipmentGroups.Recipe ? Recipe.GetType().GetElementType()
                    : (group == EquipmentGroups.RecipeOk ? RecipeOk : CoatOk).GetType(), Missing ? "缺失配方节点" : "");
            return result;
        }
        private DataValue Value(string group) => new(new Variant(group == EquipmentGroups.Recipe ? Recipe.Clone() : group == EquipmentGroups.RecipeOk ? RecipeOk : CoatOk))
            { SourceTimestamp = _stamp };
        public async Task<DataValue> ReadGroupAsync(string group, int? index, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!EquipmentGroups.RecipePoints.Contains(group)) return await Base.ReadGroupAsync(group, index, token);
            Calls.Enqueue("read:" + group);
            return Value(group);
        }
        public async Task<IReadOnlyDictionary<string, string>> SubscribeGroupsAsync(IReadOnlyList<string> groups, Action<string, DataValue> onValue, CancellationToken token)
        {
            Callback = onValue;
            return await Base.SubscribeGroupsAsync(groups.Where(g => EquipmentGroups.All.Contains(g)).ToArray(), onValue, token);
        }
        public Task WriteParameterElementAsync(int index, object value, CancellationToken token) => Base.WriteParameterElementAsync(index, value, token);
        public async Task WriteRecipeValueAsync(string group, object value, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Calls.Enqueue("write:" + group); Writes.Enqueue((group, value));
            if (Writing is not null) await Writing(group, value, token);
            if (group == EquipmentGroups.CoatOk)
            {
                if (FailFinal && System.Convert.ToBoolean(value)) throw new InvalidOperationException("完成标志拒绝");
                CoatOk = value; Publish(group);
            }
            else if (group == EquipmentGroups.Recipe)
            {
                if (!Mismatch) Array.Copy((Array)value, Recipe, 18);
                Publish(group);
            }
            else
            {
                if (!SuppressZero) { RecipeOk = value; Publish(group); }
                if (AutoComplete)
                {
                    if (FastComplete) Complete();
                    else _ = Task.Run(async () => { await Task.Delay(150); Complete(); });
                }
            }
        }
        public void Complete()
        {
            RecipeOk = RecipeOk is bool ? true : System.Convert.ChangeType(1, RecipeOk.GetType());
            Publish(EquipmentGroups.RecipeOk); Publish(EquipmentGroups.RecipeOk);
        }
        public void Publish(string group, StatusCode? status = null)
        {
            Stamp(); var data = Value(group); data.StatusCode = status ?? StatusCodes.Good; Callback?.Invoke(group, data);
        }
        public Task WaitForDisconnectAsync(CancellationToken token) => Base.WaitForDisconnectAsync(token);
        public ValueTask DisposeAsync() => Base.DisposeAsync();
    }
    internal sealed class Factory(Session session) : IEquipmentSessionFactory
    {
        public int Count;
        public Task<IEquipmentSession> ConnectAsync(OpcUaAlarmOptions options, CancellationToken token)
        { Count++; return Task.FromResult<IEquipmentSession>(session); }
    }
    internal sealed class Harness : IAsyncDisposable
    {
        public readonly EquipmentFeatureTests.Harness Definitions = new();
        public readonly Session Session;
        public readonly OpcUaEquipmentClient Client;
        public readonly OpcUaRecipePlcGateway Gateway;
        public readonly RecipeDispatchService Dispatch;
        public Harness(Session? session = null)
        {
            Session = session ?? new();
            Definitions.Auth.RecipeAllowed = true;
            Client = new(Definitions.Settings, new Factory(Session), Definitions.Alarms, Definitions.Io, Definitions.Parameters,
                Definitions.Auth, Definitions.Runtime, true, enableRecipes: true);
            Gateway = new(Client, Definitions.Runtime, new SqliteRecipeDefinitionRepository(Definitions.DefinitionsPath).Load());
            Dispatch = new(Gateway, Definitions.Runtime, new RecipeDispatchOptions { LayerCompleteTimeout = TimeSpan.FromSeconds(3) });
        }
        public async Task Start() { await Client.StartAsync(CancellationToken.None); await AlarmArrayConnectionTests.Until(() => Client.Snapshot().IsConnected); }
        public Task<RecipeRunResult> Run(params RecipeLayer[] layers) => Dispatch.RunAsync(new(RecipeDispatchMode.Selected, layers, "测试选择") { RecipeName = "测试配方" },
            new Progress<RecipeRunProgress>(), CancellationToken.None);
        public async ValueTask DisposeAsync() { await Client.DisposeAsync(); await Definitions.DisposeAsync(); }
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Sequential_protocol_uses_one_session_and_preserves_tail_even_with_fast_duplicate_completion(bool fast)
    {
        await using var h = new Harness(new Session { FastComplete = fast, RecipeOk = false });
        await h.Start();
        var result = await h.Run(new RecipeLayer { Sequence = 2, CathodeAPower = 123, StageSpeedRpm = 20 },
            new RecipeLayer { Sequence = 5, CathodeAPower = 321 });
        Assert.True(result.IsCompleted, result.FailureReason);
        Assert.Equal(2, result.CompletedLayers);
        Assert.Equal(new[] { EquipmentGroups.CoatOk, EquipmentGroups.Recipe, EquipmentGroups.RecipeOk,
            EquipmentGroups.Recipe, EquipmentGroups.RecipeOk, EquipmentGroups.CoatOk }, h.Session.Writes.Select(w => w.Group));
        Assert.False((bool)h.Session.Writes.First().Value); Assert.True((bool)h.Session.Writes.Last().Value);
        Assert.All(h.Session.Writes.Where(w => w.Group == EquipmentGroups.RecipeOk), w => Assert.False((bool)w.Value));
        Assert.All(h.Session.Recipe.Cast<float>().Skip(18), x => Assert.Equal(77f, x));
        Assert.Equal(1, h.Session.Base.SubscribeCount);
        Assert.Empty(h.Session.Base.Writes); // IO and parameter nodes not used by recipe protocol.
        var records = h.Definitions.Runtime.All;
        Assert.All(records, a => { Assert.True(a.IsSimulated); Assert.Equal("测试工程师", a.UserName); });
        Assert.Single(records, a => a.RecipeSnapshot.Length > 0);
        Assert.Single(records.Select(a => a.RecipeRunId).Distinct());
        var restarted = new EquipmentRuntimeRepository(System.IO.Path.Combine(h.Definitions.Temp.Path, "equipment-runtime.db"));
        Assert.Contains(restarted.Query(DateTimeOffset.MinValue, DateTimeOffset.MaxValue), a => a.Outcome == "配方完成");
    }
    [Theory] [InlineData("mismatch")] [InlineData("rangeReject")] [InlineData("zeroMissing")] [InlineData("finalFail")]
    public async Task Failed_confirmation_never_advances_or_claims_success(string mode)
    {
        var session = new Session { Mismatch = mode == "mismatch", SuppressZero = mode == "zeroMissing", FailFinal = mode == "finalFail" };
        if (mode == "rangeReject") session.Writing = (g, v, t) => g == EquipmentGroups.Recipe ? throw new InvalidOperationException("BadIndexRangeInvalid") : Task.CompletedTask;
        await using var h = new Harness(session); await h.Start();
        var result = await h.Run(new RecipeLayer { Sequence = 1 });
        Assert.False(result.IsCompleted);
        if (mode is "mismatch" or "rangeReject") Assert.DoesNotContain(session.Writes, w => w.Group == EquipmentGroups.RecipeOk);
        if (mode == "finalFail") { Assert.Equal(1, result.CompletedLayers); Assert.Contains("全部层已完成，完成标志未确认", result.FailureReason); }
        else Assert.DoesNotContain(session.Writes, w => w.Group == EquipmentGroups.CoatOk && w.Value is true);
        Assert.Single(session.Writes, w => w.Group == EquipmentGroups.Recipe);
    }
    [Theory] [InlineData("permission")] [InlineData("audit")] [InlineData("bounds")] [InlineData("readonly")] [InlineData("missing")]
    public async Task Preflight_rejection_produces_no_writes(string reason)
    {
        await using var h = new Harness(new Session { ReadOnly = reason == "readonly", Missing = reason == "missing" }); await h.Start();
        if (reason == "permission") h.Definitions.Auth.SetAllowed(false);
        if (reason == "audit") h.Definitions.Runtime.FailBegin = true;
        var result = await h.Run(new RecipeLayer { Sequence = 1, StageSpeedRpm = reason == "bounds" ? -1 : 0 });
        Assert.False(result.IsCompleted); Assert.Empty(h.Session.Writes); Assert.False(h.Client.IsWriteInProgress);
    }
    [Theory] [InlineData("disconnect")] [InlineData("quality")] [InlineData("permission")]
    public async Task Active_run_failure_stops_future_writes_and_never_resumes(string reason)
    {
        await using var h = new Harness(new Session { AutoComplete = false }); await h.Start();
        var run = h.Run(new RecipeLayer { Sequence = 1 }, new RecipeLayer { Sequence = 2 });
        await AlarmArrayConnectionTests.Until(() => h.Session.Writes.Count == 3 && !h.Client.Snapshot().IsWriting);
        if (reason == "disconnect") h.Session.Base.Failure.TrySetException(new System.IO.IOException("断线"));
        if (reason == "quality") h.Session.Publish(EquipmentGroups.Recipe, StatusCodes.BadCommunicationError);
        if (reason == "permission") h.Definitions.Auth.SetAllowed(false);
        var result = await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(result.IsCompleted); Assert.Equal(3, h.Session.Writes.Count);
        h.Session.Complete(); Assert.Equal(3, h.Session.Writes.Count);
    }
    [Fact] public async Task Endpoint_locked_for_run_but_parameters_allowed_during_coating()
    {
        await using var h = new Harness(new Session { AutoComplete = false }); await h.Start();
        var run = h.Run(new RecipeLayer { Sequence = 1 });
        await AlarmArrayConnectionTests.Until(() => h.Session.Writes.Count == 3 && !h.Client.Snapshot().IsWriting);
        Assert.True(h.Client.IsWriteInProgress);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Client.ApplyEndpointAsync("opc.tcp://127.0.0.1:4999"));
        var result = await h.Client.WriteParameterAsync(new("EQ_Parameter1[0]", "8", 5f, h.Client.Snapshot().Epoch));
        Assert.Equal(ParameterWriteOutcome.Confirmed, result.Outcome);
        h.Session.Complete(); Assert.True((await run).IsCompleted);
    }
    [Fact] public async Task Pending_parameter_finishes_then_recipe_transaction_blocks_new_parameter_submissions()
    {
        await using var h = new Harness(); await h.Start();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Session.Base.WriteHandler = async (i,v,t) => { await release.Task.WaitAsync(t); h.Session.Base.Values[EquipmentGroups.Parameter].SetValue(v,i); };
        var parameter = h.Client.WriteParameterAsync(new("EQ_Parameter1[0]", "8", 5f, h.Client.Snapshot().Epoch));
        await AlarmArrayConnectionTests.Until(() => h.Session.Base.Writes.Count == 1);
        var run = h.Run(new RecipeLayer { Sequence = 1 });
        Assert.Empty(h.Session.Writes);
        Assert.Equal(ParameterWriteOutcome.Rejected, (await h.Client.WriteParameterAsync(new("EQ_Parameter1[1]", "9", 5f, h.Client.Snapshot().Epoch))).Outcome);
        release.SetResult();
        Assert.Equal(ParameterWriteOutcome.Confirmed, (await parameter).Outcome);
        Assert.True((await run).IsCompleted);
    }
    [Fact] public void Definitions_and_mapping_validate_new_ranges_types_and_unused_mode_zeroes()
    {
        var d = RecipeDefinitions.Default;
        Assert.Equal(18, d.Count); Assert.Equal(120, d[3].Maximum); Assert.Equal(3600, d[6].Maximum); Assert.Equal(2, d[13].DefaultValue);
        var layer = new RecipeLayer { Sequence = 1, IntervalSeconds = 120, CoatingSeconds = 3600, StageSpeedRpm = 500,
            PressureControlMode = RecipePressureControlMode.Pressure, IgnitionPressurePa = 0.2, IgnitionApcPercent = 40 };
        var block = (float[])RecipeDefinitions.ConvertLayer(layer, typeof(float), d);
        Assert.Equal(120, block[3]); Assert.Equal(3600, block[6]); Assert.Equal(500, block[5]); Assert.Equal(0.2f, block[14]); Assert.Equal(0, block[16]);
        Assert.Throws<InvalidOperationException>(() => RecipeDefinitions.ConvertLayer(layer, typeof(int), d));
        Assert.Throws<InvalidOperationException>(() => RecipeDefinitions.ConvertLayer(new RecipeLayer { Sequence = 1, StageSpeedRpm = -1 }, typeof(float), d));
    }
    [Fact] public async Task Integer_flags_and_double_recipe_preserve_exact_types()
    {
        await using var h = new Harness(new Session { Recipe = Enumerable.Repeat(77d,24).ToArray(), RecipeOk = 0, CoatOk = (short)1 });
        await h.Start();
        var result = await h.Run(new RecipeLayer { Sequence = 1, IgnitionPressurePa = 0.2 });
        Assert.True(result.IsCompleted, result.FailureReason);
        Assert.IsType<double[]>(h.Session.Writes.Single(w => w.Group == EquipmentGroups.Recipe).Value);
        Assert.IsType<int>(h.Session.Writes.Single(w => w.Group == EquipmentGroups.RecipeOk).Value);
        Assert.All(h.Session.Writes.Where(w => w.Group == EquipmentGroups.CoatOk), w => Assert.IsType<short>(w.Value));
    }
    [Fact] public async Task Final_local_save_failure_preserves_PLC_confirmed_success()
    {
        await using var h = new Harness(); await h.Start();
        h.Session.Writing = (group,value,token) => {
            if (group == EquipmentGroups.CoatOk && value is true) h.Definitions.Runtime.FailFinish = true;
            return Task.CompletedTask;
        };
        var result = await h.Run(new RecipeLayer { Sequence = 1 });
        Assert.True(result.IsCompleted, result.FailureReason); Assert.Contains("PLC已确认", result.Notice);
        Assert.Contains("保存失败", result.Notice); Assert.True((bool)h.Session.CoatOk);
    }
    [Fact] public async Task Audit_failure_after_parameter_block_prevents_start()
    {
        await using var h = new Harness(); await h.Start();
        h.Session.Writing = (group,value,token) => {
            if (group == EquipmentGroups.Recipe) h.Definitions.Runtime.FailBegin = true;
            return Task.CompletedTask;
        };
        var result = await h.Run(new RecipeLayer { Sequence = 1 });
        Assert.False(result.IsCompleted);
        Assert.DoesNotContain(h.Session.Writes, w => w.Group == EquipmentGroups.RecipeOk);
    }
    [Fact] public async Task Layer_completion_audit_failure_retains_completed_count_but_blocks_final_flag()
    {
        await using var h = new Harness(new Session { AutoComplete = false }); await h.Start();
        var run = h.Run(new RecipeLayer { Sequence = 1 });
        await AlarmArrayConnectionTests.Until(() => h.Session.Writes.Count == 3 && !h.Client.Snapshot().IsWriting);
        h.Definitions.Runtime.FailBegin = true; h.Session.Complete();
        var result = await run;
        Assert.False(result.IsCompleted); Assert.Equal(1,result.CompletedLayers);
        Assert.Contains("全部层已完成，完成标志未确认",result.FailureReason);
        Assert.Equal(3,h.Session.Writes.Count);
    }

    [Fact] public async Task Alarms_and_unknown_alarm_quality_do_not_add_an_unapproved_interlock()
    {
        await using var h = new Harness(); await h.Start();
        h.Session.Base.Values[EquipmentGroups.Alarm].SetValue(true,0); h.Session.Base.Send(EquipmentGroups.Alarm);
        h.Session.Base.Send(EquipmentGroups.Alarm, StatusCodes.BadCommunicationError);
        Assert.True((await h.Run(new RecipeLayer { Sequence = 1 })).IsCompleted);
    }
    [Fact] public async Task Explicit_stop_while_waiting_sends_no_machine_stop_or_completion_write()
    {
        await using var h = new Harness(new Session { AutoComplete = false }); await h.Start();
        using var stop = new CancellationTokenSource();
        var run = h.Dispatch.RunAsync(new(RecipeDispatchMode.All, [new RecipeLayer { Sequence = 1 },new RecipeLayer { Sequence = 2 }], "1-2"),
            new Progress<RecipeRunProgress>(), stop.Token);
        await AlarmArrayConnectionTests.Until(() => h.Session.Writes.Count == 3);
        stop.Cancel();
        var result = await run;
        Assert.False(result.IsCompleted); Assert.Contains("不表示设备已停机",result.FailureReason);
        Assert.Equal(3,h.Session.Writes.Count); Assert.False(h.Client.IsWriteInProgress);
    }
    [Fact] public void Interrupted_run_snapshot_is_restored_only_as_history()
    {
        using var temp = new AlarmFeatureTests.TemporaryDirectory();
        var file = System.IO.Path.Combine(temp.Path,"runtime.db");
        var runtime = new EquipmentRuntimeRepository(file);
        var id = Guid.NewGuid();
        runtime.BeginWrite(new Small_square_cavity_coating_machine.Models.History.OperationLogRecord(DateTimeOffset.Now,
            "user","recipe","run","1,3",false,false,"",true) { Outcome = "配方运行中", RecipeRunId = id, RecipeSnapshot = "{\"layers\":[1,3]}" });
        var restored = Assert.Single(new EquipmentRuntimeRepository(file).Query(DateTimeOffset.MinValue,DateTimeOffset.MaxValue));
        Assert.Equal(id,restored.RecipeRunId); Assert.Equal("结果未确认",restored.Outcome); Assert.Contains("需人工核对",restored.FailureReason);
        Assert.Contains("layers",restored.RecipeSnapshot);
    }

}
