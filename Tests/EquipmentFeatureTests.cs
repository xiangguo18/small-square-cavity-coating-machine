using Opc.Ua;
using Small_square_cavity_coating_machine.Models.Alarms;
using Small_square_cavity_coating_machine.Models.Equipment;
using Small_square_cavity_coating_machine.Models.History;
using Small_square_cavity_coating_machine.Models.Security;
using Small_square_cavity_coating_machine.Services.Alarms;
using Small_square_cavity_coating_machine.Services.Equipment;
using Small_square_cavity_coating_machine.Services.Security;
using Small_square_cavity_coating_machine.Services.History;
using Small_square_cavity_coating_machine.ViewModels;
using Small_square_cavity_coating_machine.ViewModels.Equipment;
using Small_square_cavity_coating_machine.ViewModels.History;
using System.IO;
using System.Security.Cryptography;
using Xunit;

namespace Small_square_cavity_coating_machine.Tests;

public sealed class EquipmentFeatureTests
{
    internal sealed class Authorization : IAuthorizationService
    {
        public bool Allowed = true;
        public bool RecipeAllowed;
        public string CurrentUserName { get; set; } = "测试工程师";
        public event EventHandler? AccessChanged;
        public event EventHandler<AuthorizationDeniedEventArgs>? AccessDenied { add { } remove { } }
        public bool CanOperate(PermissionKey permission) => Allowed && (permission == PermissionKey.ParameterSettings || RecipeAllowed && permission == PermissionKey.ProcessRecipe);
        public bool TryAuthorize(PermissionKey permission) => CanOperate(permission);
        public void SetAllowed(bool value) { Allowed = value; AccessChanged?.Invoke(this, EventArgs.Empty); }
    }
    internal sealed class Runtime(string path) : IEquipmentRuntimeRepository
    {
        public readonly EquipmentRuntimeRepository Inner = new(path);
        public bool FailBegin, FailFinish;
        public Action? AfterBegin;
        public string StorageError => Inner.StorageError;
        public event EventHandler<OperationLogRecord>? RecordAdded { add => Inner.RecordAdded += value; remove => Inner.RecordAdded -= value; }
        public void BeginWrite(OperationLogRecord record)
        { if (FailBegin) throw new IOException("审计磁盘不可用"); Inner.BeginWrite(record); AfterBegin?.Invoke(); }
        public void FinishWrite(OperationLogRecord record, ParameterCache? cache)
        { if (FailFinish) throw new IOException("最终保存失败"); Inner.FinishWrite(record, cache); }
        public void Add(OperationLogRecord record) => Inner.Add(record);
        public IReadOnlyList<OperationLogRecord> Query(DateTimeOffset start, DateTimeOffset end) => Inner.Query(start, end);
        public IReadOnlyList<ParameterCache> LoadCache(string endpoint) => Inner.LoadCache(endpoint);
        public IReadOnlyList<OperationLogRecord> All => Query(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
    }
    internal sealed class Session : IEquipmentSession
    {
        public readonly Dictionary<string, Array> Values = new() {
            [EquipmentGroups.Alarm] = new bool[552], [EquipmentGroups.Io] = new bool[532],
            [EquipmentGroups.Parameter] = Enumerable.Repeat(5f,21).ToArray(),
            [EquipmentGroups.PartCommand] = new bool[1000],
            [EquipmentGroups.PartState] = InitialPartStates(),
            [EquipmentGroups.PartData] = new float[27],
            [EquipmentGroups.PartDataSet1] = new float[21],
            [EquipmentGroups.PartDataSet2] = new float[3],
            [EquipmentGroups.Interlock] = Enumerable.Repeat(true,16).ToArray() };
        public readonly Dictionary<string, bool> Scalars = EquipmentGroups.SystemControlPoints
            .ToDictionary(g => g, g => g.EndsWith("_En", StringComparison.Ordinal), StringComparer.Ordinal);
        public readonly Dictionary<string, EquipmentBinding> Overrides = [];
        public readonly Dictionary<string, string> SubscriptionErrors = [];
        public readonly List<(int Index, object Value)> Writes = [];
        public readonly List<(string Group, int? Index, object Value)> ControlWrites = [];
        public readonly List<(string Group, IReadOnlyList<(int Index, object Value)> Mutations)> ArrayWrites = [];
        public readonly List<string> InitialReads = [];
        public string[] Subscribed = [];
        public int SubscribeCount;
        public bool Disposed;
        public bool ApplyFeedback = true;
        /// <summary>系统按钮绿灯槽（Part_State[30..35]）默认熄灭，按钮状态由 PLC 反馈写入。</summary>
        private static ushort[] InitialPartStates()
        {
            var states = Enumerable.Repeat((ushort)1, 500).ToArray();
            for (var i = 30; i <= 35; i++) states[i] = 0;
            return states;
        }
        public Func<int, object, CancellationToken, Task>? WriteHandler;
        public Func<int, CancellationToken, Task<DataValue>>? ElementRead;
        public Action<string, DataValue>? Callback;
        public readonly TaskCompletionSource Failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyDictionary<string, EquipmentBinding>> DiscoverAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyDictionary<string, EquipmentBinding>>(Values.Keys.Concat(Scalars.Keys).ToDictionary(g => g,
                g => Overrides.GetValueOrDefault(g) ?? new(g, true, EquipmentGroups.IsWritable(g),
                    Scalars.ContainsKey(g) ? typeof(bool) : Values[g].GetType().GetElementType(), ""), StringComparer.Ordinal));
        public Task<DataValue> ReadGroupAsync(string group, int? index, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Scalars.TryGetValue(group, out var scalar)) return Task.FromResult(new DataValue(new Variant(scalar)));
            if (index is not null && ElementRead is not null) return ElementRead(index.Value, token);
            if (index is null) InitialReads.Add(group);
            var value = Values[group];
            if (index is null) return Task.FromResult(new DataValue(new Variant(value.Clone())));
            var element = Array.CreateInstance(value.GetType().GetElementType()!, 1);
            element.SetValue(value.GetValue(index.Value), 0);
            return Task.FromResult(new DataValue(new Variant(element)));
        }
        public Task<IReadOnlyDictionary<string, string>> SubscribeGroupsAsync(IReadOnlyList<string> groups, Action<string, DataValue> callback, CancellationToken token)
        { SubscribeCount++; Subscribed = groups.ToArray(); Callback = callback; return Task.FromResult<IReadOnlyDictionary<string, string>>(SubscriptionErrors); }
        public async Task WriteParameterElementAsync(int index, object value, CancellationToken token)
        {
            Writes.Add((index, value));
            if (WriteHandler is not null) { await WriteHandler(index,value,token); return; }
            Values[EquipmentGroups.Parameter].SetValue(value, index); Send(EquipmentGroups.Parameter);
        }
        public Task WriteElementAsync(string group, int? index, object value, CancellationToken token)
        {
            if (group == EquipmentGroups.Parameter && index.HasValue)
                return WriteParameterElementAsync(index.Value, value, token);
            token.ThrowIfCancellationRequested();
            ControlWrites.Add((group,index,value));
            if (Scalars.ContainsKey(group)) Scalars[group] = (bool)value;
            else if (index.HasValue) Values[group].SetValue(value,index.Value);
            else throw new InvalidOperationException("数组控制点缺少下标");
            ApplySystemFeedback(group, value);
            ApplyPartCommandFeedback(group, index, value);
            Send(group);
            return Task.CompletedTask;
        }
        public Task WriteArrayElementsAsync(string group, IReadOnlyList<ArrayWriteMutation> mutations, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ArrayWrites.Add((group, mutations.Select(m => (m.Index, m.Value)).ToArray()));
            foreach (var mutation in mutations)
            {
                if (Scalars.ContainsKey(group)) throw new InvalidOperationException("数组组不能作为标量写入");
                Values[group].SetValue(mutation.Value, mutation.Index);
                if (group == EquipmentGroups.PartCommand && Equals(mutation.Value, true))
                    ApplyPartCommandFeedback(group, mutation.Index, mutation.Value);
            }
            if (group == EquipmentGroups.PartCommand) ApplyWorkflowFeedbackFromValues();
            Send(group);
            return Task.CompletedTask;
        }
        private void ApplyPartCommandFeedback(string group, int? index, object value)
        {
            if (!ApplyFeedback) return;
            if (group != EquipmentGroups.PartCommand || value is not true || index is not { } commandIndex)
                return;
            // 模拟 PLC：收到开/关命令后更新对应 Part_State，但不主动推送订阅，UI 变化仍等真实状态反馈。
            (int Part, bool Open) feedback = commandIndex switch
            {
                0 => (0, true), 1 => (0, false), 2 => (1, true), 3 => (1, false),
                6 => (2, true), 7 => (2, false),
                8 => (3, true), 9 => (3, false),
                10 => (4, true), 11 => (4, false), 12 => (4, false),
                13 => (5, true), 14 => (5, false),
                15 => (24, true), 16 => (24, false), 17 => (25, true), 18 => (25, false),
                21 => (6, true), 22 => (6, false), 23 => (7, true), 24 => (7, false),
                27 => (11, true), 28 => (11, false), 29 => (12, true), 30 => (12, false),
                31 => (15, true), 32 => (15, false), 33 => (14, true), 34 => (14, false),
                35 => (13, true), 36 => (13, false), 37 => (16, true), 38 => (16, false),
                39 => (20, true), 40 => (20, false),
                41 => (21, true), 42 => (21, false), 43 => (22, true), 44 => (22, false),
                45 => (23, true), 46 => (23, false), _ => (-1, false)
            };
            if (feedback.Part >= 0)
                Values[EquipmentGroups.PartState].SetValue(feedback.Part switch
                {
                    0 => feedback.Open ? (ushort)2 : (ushort)0,
                    1 => feedback.Open ? (ushort)1 : (ushort)0,
                    _ => feedback.Open ? (ushort)2 : (ushort)1
                }, feedback.Part);
        }
        private void ApplySystemFeedback(string group, object value)
        {
            if (!ApplyFeedback) return;
            if (!group.StartsWith("EQ_", StringComparison.Ordinal) || group == EquipmentGroups.PassInterlock) return;
            var (output, stateIndex) = group switch
            {
                "EQ_Manual" => ("fbButtonManual_Output", 30),
                "EQ_Auto" => ("fbButtonAuto_Output", 32),
                "EQ_Semi" => ("fbButtonSemi_Output", 31),
                "EQ_Start" => ("fbButtonStart_Output", 33),
                "EQ_Stop" => ("fbButtonStop_Output", 34),
                "EQ_Reset" => ("fbButtonReset_Output", 35),
                "EQ_BuzzerDisable" => ("fbButtonBuzzerDisable_Output", -1),
                _ => ("", -1)
            };
            if (output.Length > 0 && Scalars.ContainsKey(output))
            {
                Scalars[output] = (bool)value;
                Send(output);
            }
            if (stateIndex >= 0)
            {
                Values[EquipmentGroups.PartState].SetValue((bool)value ? (ushort)1 : (ushort)0, stateIndex);
                Send(EquipmentGroups.PartState);
            }
        }
        private void ApplyWorkflowFeedbackFromValues()
        {
            if (!ApplyFeedback) return;
            var commands = Values[EquipmentGroups.PartCommand];
            bool Active(int index) => commands.GetValue(index) is bool b && b;
            Scalars["fbButtonPumpStart_Output"] = Active(980);
            Scalars["fbButtonVentStart_Output"] = Active(981);
            Scalars["fbButtonHP_Start_Output"] = Active(982);
            Send("fbButtonPumpStart_Output");
            Send("fbButtonVentStart_Output");
            Send("fbButtonHP_Start_Output");
        }
        public void Send(string group, StatusCode? quality = null) =>
            Callback?.Invoke(group, new DataValue(new Variant(Scalars.TryGetValue(group,out var scalar) ? scalar : Values[group].Clone())) { StatusCode = quality ?? StatusCodes.Good });
        public Task WaitForDisconnectAsync(CancellationToken token) => Failure.Task.WaitAsync(token);
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    internal sealed class Factory(params Session[] sessions) : IEquipmentSessionFactory
    {
        public int ConnectCount;
        public Task<IEquipmentSession> ConnectAsync(OpcUaAlarmOptions options, CancellationToken token)
        {
            var index = Interlocked.Increment(ref ConnectCount) - 1;
            if (index >= sessions.Length) throw new IOException("No extra test sessions");
            if (index > 0) Assert.True(sessions[index-1].Disposed);
            return Task.FromResult<IEquipmentSession>(sessions[index]);
        }
    }
    internal sealed class Harness : IAsyncDisposable
    {
        public readonly AlarmFeatureTests.TemporaryDirectory Temp = new();
        public readonly IReadOnlyList<IoDefinition> Io;
        public readonly IReadOnlyList<ParameterDefinition> Parameters;
        public readonly IReadOnlyList<AlarmDefinition> Alarms;
        public readonly string DefinitionsPath;
        public readonly Authorization Auth = new();
        public readonly Runtime Runtime;
        public readonly Session Session;
        public readonly Factory Factory;
        public readonly OpcUaEquipmentClient Client;
        public readonly AlarmArrayConnectionTests.Settings Settings = new() { Options = new OpcUaAlarmOptions { EndpointUrl = "opc.tcp://127.0.0.1:4841", ReconnectDelayMs = 1000 } };
        public Harness(params Session[] sessions)
        {
            DefinitionsPath = SqliteAlarmDefinitionRepository.ExtractEmbeddedDatabase(Temp.Path);
            var repo = new SqliteEquipmentDefinitionRepository(DefinitionsPath);
            Io=repo.LoadIo(); Parameters=repo.LoadParameters();
            Alarms=new SqliteAlarmDefinitionRepository(DefinitionsPath).Load();
            Runtime = new(Path.Combine(Temp.Path,"equipment-runtime.db"));
            Session = sessions.FirstOrDefault() ?? new Session();
            Factory = new(sessions.Length == 0 ? [Session] : sessions);
            Client=new(Settings,Factory,Alarms,Io,Parameters,Auth,Runtime);
        }
        public async Task Start()
        { await Client.StartAsync(CancellationToken.None); await AlarmArrayConnectionTests.Until(() => Client.Snapshot().IsConnected); }
        public ParameterWriteRequest Request(int index=0, string draft="8")
        {
            var snapshot=Client.Snapshot(); var point=snapshot.Groups[EquipmentGroups.Parameter].Points[$"EQ_Parameter1[{index}]"];
            return new(point.Address,draft,point.Value!,snapshot.Epoch);
        }
        public async ValueTask DisposeAsync() { await Client.DisposeAsync(); Temp.Dispose(); }
    }

    [Fact] public async Task Definition_counts_sparse_indices_metadata_and_hash_are_immutable()
    {
        await using var h=new Harness();
        var before=SHA256.HashData(File.ReadAllBytes(h.DefinitionsPath));
        Assert.Equal(64,h.Io.Count); Assert.Equal(21,h.Parameters.Count);
        Assert.Equal(Enumerable.Range(0,32).Concat(Enumerable.Range(500,32)),h.Io.Select(d=>EquipmentAddress.Index(d.Address,EquipmentGroups.Io)));
        Assert.Equal(Enumerable.Range(0,21),h.Parameters.Select(d=>EquipmentAddress.Index(d.Address,EquipmentGroups.Parameter)));
        Assert.Equal(32,h.Io.Count(d=>d.Type=="IN")); Assert.Equal(32,h.Io.Count(d=>d.Type=="OUT"));
        Assert.Contains("门阀",h.Parameters[0].Chinese); Assert.Equal("Sec",h.Parameters[0].Unit);
        Assert.Equal(0,h.Parameters[0].MinValue); Assert.Equal(60,h.Parameters[0].MaxValue);
        await h.Start(); Assert.Equal(ParameterWriteOutcome.Confirmed,(await h.Client.WriteParameterAsync(h.Request())).Outcome);
        Assert.Equal(before,SHA256.HashData(File.ReadAllBytes(h.DefinitionsPath)));
    }
    [Theory]
    [InlineData("EQ_IO[-1]")] [InlineData("EQ_IO[1]extra")] [InlineData("EQ_Parameter1[0]")] [InlineData("EQ_IO[]")]
    public void Bad_addresses_are_not_guessed(string address) =>
        Assert.Throws<InvalidOperationException>(()=>EquipmentAddress.Index(address,EquipmentGroups.Io));

    [Fact] public async Task One_session_one_subscription_three_groups_and_IO_is_read_only()
    {
        await using var h=new Harness();
        using var io=new IoStatusViewModel(h.Io,h.Client,new AlarmFeatureTests.InlineDispatcher());
        Assert.All(io.Rows,r=>Assert.Equal("—",r.ValueText));
        await h.Start();
        Assert.Equal(1,h.Factory.ConnectCount); Assert.Equal(1,h.Session.SubscribeCount);
        Assert.Equal(EquipmentGroups.All,h.Session.Subscribed); Assert.Equal(EquipmentGroups.All,h.Session.InitialReads);
        ((bool[])h.Session.Values[EquipmentGroups.Io])[500]=true; h.Session.Send(EquipmentGroups.Io);
        Assert.Equal("1",io.Rows.Single(r=>r.Definition.Address=="EQ_IO[500]").ValueText);
        io.FilterCommand.Execute("OUT"); Assert.Equal(32,io.Rows.Count);
        Assert.All(io.Rows,r=>Assert.Equal("OUT",r.Definition.Type));
        var result=await h.Client.WriteParameterAsync(new("EQ_IO[500]","0",true,h.Client.Snapshot().Epoch));
        Assert.Equal(ParameterWriteOutcome.Rejected,result.Outcome); Assert.Empty(h.Session.Writes);
        h.Session.Send(EquipmentGroups.Io,StatusCodes.BadCommunicationError);
        Assert.All(io.Rows,r=>Assert.Equal("—",r.ValueText));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Per_group_binding_or_subscription_failure_does_not_stop_alarm(bool subscription)
    {
        var session=new Session();
        if(subscription) session.SubscriptionErrors[EquipmentGroups.Io]="订阅失败";
        else session.Overrides[EquipmentGroups.Io]=new(EquipmentGroups.Io,false,false,null,"多个同名数组");
        session.Overrides[EquipmentGroups.Parameter]=new(EquipmentGroups.Parameter,false,false,null,"未找到");
        await using var h=new Harness(session);
        await using var adapter=new EquipmentAlarmSignalSource(h.Client);
        var history=new InMemoryAlarmLogRepository();
        using var monitor=new AlarmMonitorService(h.Alarms,adapter,history);
        await h.Start();
        ((bool[])session.Values[EquipmentGroups.Alarm])[500]=true; session.Send(EquipmentGroups.Alarm);
        Assert.Equal("EQ_Alarm[500]",Assert.Single(history.LoadUncleared()).Address);
        Assert.False(h.Client.Snapshot().Groups[EquipmentGroups.Io].IsReady);
        session.Send(EquipmentGroups.Io); // Even a stale callback from an unsuccessful item must stay invalid.
        Assert.False(h.Client.Snapshot().Groups[EquipmentGroups.Io].IsReady);
        Assert.True(h.Client.Snapshot().Groups[EquipmentGroups.Alarm].IsReady);
    }
    [Fact] public async Task Invalid_IO_elements_short_array_bad_quality_and_parameter_type_are_unknown()
    {
        var session=new Session(); session.Values[EquipmentGroups.Io]=new int[532];
        await using var h=new Harness(session); await h.Start();
        ((int[])session.Values[EquipmentGroups.Io])[500]=2; session.Send(EquipmentGroups.Io);
        Assert.Equal(AlarmQuality.Bad,h.Client.Snapshot().Groups[EquipmentGroups.Io].Points["EQ_IO[500]"].Quality);
        session.Values[EquipmentGroups.Io]=new int[32]; session.Send(EquipmentGroups.Io);
        Assert.Equal(32,h.Client.Snapshot().Groups[EquipmentGroups.Io].Points.Values.Count(p=>p.Quality==AlarmQuality.Good));
        session.Send(EquipmentGroups.Parameter,StatusCodes.Uncertain);
        Assert.All(h.Client.Snapshot().Groups[EquipmentGroups.Parameter].Points.Values,p=>Assert.False(p.CanWrite));
        session.Values[EquipmentGroups.Parameter]=new double[21]; session.Send(EquipmentGroups.Parameter);
        Assert.False(h.Client.Snapshot().Groups[EquipmentGroups.Parameter].IsReady);
        Assert.True(h.Client.Snapshot().Groups[EquipmentGroups.Alarm].IsReady);
    }
    [Fact] public async Task Single_element_write_is_audited_first_and_confirmed_cache_survives_restart()
    {
        await using var h=new Harness(); await h.Start();
        var neighbors=(float[])h.Session.Values[EquipmentGroups.Parameter].Clone();
        h.Session.WriteHandler=(index,value,token)=>{
            Assert.Equal("待处理",Assert.Single(h.Runtime.All).Outcome);
            Assert.IsType<float>(value); Assert.Equal(20,index);
            h.Session.Values[EquipmentGroups.Parameter].SetValue(value,index);
            return Task.CompletedTask;
        };
        var result=await h.Client.WriteParameterAsync(h.Request(20,"8500"));
        Assert.Equal(ParameterWriteOutcome.Confirmed,result.Outcome); Assert.Single(h.Session.Writes);
        for(var i=0;i<20;i++) Assert.Equal(neighbors[i],h.Session.Values[EquipmentGroups.Parameter].GetValue(i));
        var restarted=new EquipmentRuntimeRepository(Path.Combine(h.Temp.Path,"equipment-runtime.db"));
        var audit=Assert.Single(restarted.Query(DateTimeOffset.MinValue,DateTimeOffset.MaxValue));
        Assert.Equal("已确认",audit.Outcome);
        Assert.Equal("测试工程师",audit.UserName);
        var cache=Assert.Single(restarted.LoadCache(h.Settings.Options.EndpointUrl));
        Assert.Equal("8500",cache.Value);
        Assert.Equal(h.Settings.Options.EndpointUrl,cache.Endpoint);
        Assert.Empty(restarted.LoadCache("opc.tcp://another:4840"));
    }
    [Theory] [InlineData("NaN")] [InlineData("Infinity")] [InlineData("-1")] [InlineData("61")] [InlineData("abc")]
    public async Task Invalid_numeric_input_never_reaches_transport(string draft)
    {
        await using var h=new Harness(); await h.Start();
        Assert.Equal(ParameterWriteOutcome.Rejected,(await h.Client.WriteParameterAsync(h.Request(0,draft))).Outcome);
        Assert.Empty(h.Session.Writes); Assert.Empty(h.Runtime.All);
    }
    [Fact] public async Task Permission_conflict_readonly_badquality_and_audit_failure_block_write()
    {
        await using var h=new Harness(); await h.Start();
        h.Auth.SetAllowed(false);
        Assert.Equal(ParameterWriteOutcome.Rejected,(await h.Client.WriteParameterAsync(h.Request())).Outcome);
        h.Auth.SetAllowed(true); var original=h.Request(); ((float[])h.Session.Values[EquipmentGroups.Parameter])[0]=6;
        Assert.Contains("原值",(await h.Client.WriteParameterAsync(original)).Message);
        h.Session.Send(EquipmentGroups.Parameter,StatusCodes.BadCommunicationError);
        Assert.Equal(ParameterWriteOutcome.Rejected,(await h.Client.WriteParameterAsync(original)).Outcome);
        h.Session.Send(EquipmentGroups.Parameter); h.Runtime.FailBegin=true;
        Assert.Contains("审计",(await h.Client.WriteParameterAsync(h.Request())).Message);
        Assert.Empty(h.Session.Writes);
        h.Runtime.FailBegin=false; h.Runtime.AfterBegin=()=>h.Auth.SetAllowed(false);
        Assert.Equal(ParameterWriteOutcome.Rejected,(await h.Client.WriteParameterAsync(h.Request())).Outcome);
        Assert.Empty(h.Session.Writes);
        Assert.Equal("已拒绝",Assert.Single(h.Runtime.All).Outcome);
    }
    [Fact] public async Task Server_readonly_node_disables_submit()
    {
        var session=new Session(); session.Overrides[EquipmentGroups.Parameter]=new(EquipmentGroups.Parameter,true,false,typeof(float),"");
        await using var h=new Harness(session); await h.Start();
        using var vm=new ParameterSettingsViewModel(h.Parameters,h.Client,h.Runtime,h.Auth,new AlarmFeatureTests.InlineDispatcher());
        Assert.All(vm.Rows,r=>Assert.False(r.CanEdit));
        Assert.Equal(ParameterWriteOutcome.Rejected,(await h.Client.WriteParameterAsync(h.Request())).Outcome);
        Assert.Empty(session.Writes);
    }
    [Fact] public async Task Busy_write_blocks_other_rows_and_endpoint_changes_then_final_save_failure_is_separate()
    {
        await using var h=new Harness(); await h.Start();
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Session.WriteHandler=async (index,value,token)=>{ await release.Task.WaitAsync(token); h.Session.Values[EquipmentGroups.Parameter].SetValue(value,index); };
        h.Runtime.FailFinish=true;
        using var vm=new PlcConnectionStatusViewModel(h.Client,new AlarmFeatureTests.InlineDispatcher());
        var writing=h.Client.WriteParameterAsync(h.Request());
        await AlarmArrayConnectionTests.Until(()=>h.Session.Writes.Count==1);
        Assert.False(vm.CanApply);
        Assert.Equal(ParameterWriteOutcome.Rejected,(await h.Client.WriteParameterAsync(h.Request(1,"9"))).Outcome);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>h.Client.ApplyEndpointAsync("opc.tcp://127.0.0.1:4842"));
        release.SetResult();
        var result=await writing;
        Assert.Equal(ParameterWriteOutcome.Confirmed,result.Outcome);
        Assert.Contains("本机记录保存失败",result.Message); Assert.Single(h.Session.Writes);
        Assert.True(vm.CanApply);
    }
    [Theory] [InlineData("reject")] [InlineData("disconnect")] [InlineData("mismatch")]
    public async Task Rejection_disconnect_and_readback_timeout_never_replay(string mode)
    {
        await using var h=new Harness(); await h.Start();
        h.Session.WriteHandler=(index,value,token)=>{
            if(mode=="reject") throw new ParameterWriteRejectedException("BadIndexRangeInvalid");
            if(mode=="disconnect") h.Session.Failure.TrySetException(new IOException("断线"));
            return Task.CompletedTask; // mismatch intentionally retains old value
        };
        var result=await h.Client.WriteParameterAsync(h.Request());
        Assert.Equal(mode=="reject"?ParameterWriteOutcome.Rejected:ParameterWriteOutcome.Unknown,result.Outcome);
        Assert.Single(h.Session.Writes); Assert.Empty(h.Runtime.LoadCache(h.Settings.Options.EndpointUrl));
        Assert.Equal(mode=="reject"?"已拒绝":"结果未确认",Assert.Single(h.Runtime.All).Outcome);
    }
    [Fact] public async Task Reconnect_marks_all_unknown_ignores_old_notifications_and_rejects_old_drafts()
    {
        var first=new Session(); var next=new Session();
        await using var h=new Harness(first,next);
        await using var adapter=new EquipmentAlarmSignalSource(h.Client);
        var log=new InMemoryAlarmLogRepository(); using var monitor=new AlarmMonitorService(h.Alarms,adapter,log);
        ((bool[])first.Values[EquipmentGroups.Alarm])[500]=true; ((bool[])next.Values[EquipmentGroups.Alarm])[500]=true;
        await h.Start(); var old=h.Request(); var id=Assert.Single(log.LoadUncleared()).OccurrenceId;
        var sawUnknown=false;
        h.Client.SnapshotChanged+=(_,_)=>{if(!h.Client.Snapshot().IsConnected) sawUnknown=h.Client.Snapshot().Groups.Values.All(g=>g.Points.Values.All(p=>p.Quality!=AlarmQuality.Good));};
        await h.Client.ApplyEndpointAsync("opc.tcp://127.0.0.1:4842");
        await AlarmArrayConnectionTests.Until(()=>h.Client.Snapshot().IsConnected);
        Assert.True(sawUnknown); Assert.True(first.Disposed); Assert.Equal(2,h.Factory.ConnectCount);
        first.Values[EquipmentGroups.Alarm]=new bool[552]; first.Send(EquipmentGroups.Alarm);
        first.Values[EquipmentGroups.Parameter]=new float[21]; first.Send(EquipmentGroups.Parameter);
        Assert.Equal(id,Assert.Single(log.LoadUncleared()).OccurrenceId);
        Assert.Equal(5f,h.Client.Snapshot().Groups[EquipmentGroups.Parameter].Points[old.Address].Value);
        Assert.Contains("连接已变化",(await h.Client.WriteParameterAsync(old)).Message);
        Assert.Empty(next.Writes);
    }
    [Fact] public async Task Draft_is_separate_from_live_updates_blur_does_not_write_and_Escape_resets()
    {
        await using var h=new Harness(); await h.Start();
        using var vm=new ParameterSettingsViewModel(h.Parameters,h.Client,h.Runtime,h.Auth,new AlarmFeatureTests.InlineDispatcher());
        var row=vm.Rows[0]; row.BeginEditCommand.Execute(null); row.DraftText="8";
        ((float[])h.Session.Values[EquipmentGroups.Parameter])[0]=7; h.Session.Send(EquipmentGroups.Parameter);
        Assert.Equal("8",row.DraftText); Assert.Equal("7",row.CurrentText);
        row.EndEditCommand.Execute(null); Assert.Empty(h.Session.Writes);
        await row.SubmitCommand.ExecuteAsync(null); Assert.Contains("原值",row.StatusText); Assert.Empty(h.Session.Writes);
        row.CancelEditCommand.Execute(null); Assert.Equal("7",row.DraftText);
        row.DraftText="9"; await row.SubmitCommand.ExecuteAsync(null);
        Assert.Single(h.Session.Writes); Assert.Contains("写入已确认",row.StatusText);
        h.Auth.SetAllowed(false); Assert.All(vm.Rows,r=>Assert.False(r.CanEdit));
    }
    [Fact] public async Task Draft_started_before_endpoint_switch_cannot_be_rebased_on_new_epoch()
    {
        await using var h=new Harness(new Session(),new Session()); await h.Start();
        using var vm=new ParameterSettingsViewModel(h.Parameters,h.Client,h.Runtime,h.Auth,new AlarmFeatureTests.InlineDispatcher());
        var row=vm.Rows[0]; row.BeginEditCommand.Execute(null);
        await h.Client.ApplyEndpointAsync("opc.tcp://127.0.0.1:4842");
        await AlarmArrayConnectionTests.Until(()=>h.Client.Snapshot().IsConnected);
        row.DraftText="8"; await row.SubmitCommand.ExecuteAsync(null);
        Assert.Contains("连接已变化",row.StatusText); Assert.Empty(h.Session.Writes);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Bad_element_read_quality_blocks_before_send_or_marks_readback_unknown(bool afterSend)
    {
        await using var h=new Harness(); await h.Start();
        h.Session.ElementRead=(index,token)=>Task.FromResult(new DataValue(new Variant(new float[]{5f})) {
            StatusCode=!afterSend || h.Session.Writes.Count>0 ? StatusCodes.BadCommunicationError : StatusCodes.Good });
        var result=await h.Client.WriteParameterAsync(h.Request());
        Assert.Equal(afterSend?ParameterWriteOutcome.Unknown:ParameterWriteOutcome.Rejected,result.Outcome);
        Assert.Equal(afterSend?1:0,h.Session.Writes.Count);
    }
    [Fact] public async Task User_changed_during_baseline_read_cannot_submit_another_users_draft()
    {
        await using var h=new Harness(); await h.Start();
        h.Session.ElementRead=(index,token)=>{
            h.Auth.CurrentUserName="另一用户";
            return Task.FromResult(new DataValue(new Variant(new float[]{5f})));
        };
        Assert.Equal(ParameterWriteOutcome.Rejected,(await h.Client.WriteParameterAsync(h.Request())).Outcome);
        Assert.Empty(h.Session.Writes);
    }
    [Fact] public async Task Simulated_session_never_switches_to_a_real_transport_and_records_are_labeled()
    {
        await using var h=new Harness();
        var simulator=new SimulatedEquipmentSessionFactory(h.Parameters);
        await using var client=new OpcUaEquipmentClient(new SimulationConnectionSettings(),simulator,h.Alarms,h.Io,h.Parameters,h.Auth,h.Runtime,true);
        using var status=new PlcConnectionStatusViewModel(client,new AlarmFeatureTests.InlineDispatcher());
        await client.StartAsync(CancellationToken.None);
        await AlarmArrayConnectionTests.Until(()=>client.Snapshot().Groups.Values.All(g=>g.IsReady));
        Assert.Contains("模拟",status.StatusText); Assert.False(status.CanApply);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>client.ApplyEndpointAsync("opc.tcp://192.168.0.10:4840"));
        var snapshot=client.Snapshot();
        var result=await client.WriteParameterAsync(new("EQ_Parameter1[0]","8",snapshot.Groups[EquipmentGroups.Parameter].Points["EQ_Parameter1[0]"].Value!,snapshot.Epoch));
        Assert.Equal(ParameterWriteOutcome.Confirmed,result.Outcome);
        Assert.True(Assert.Single(h.Runtime.LoadCache(snapshot.Endpoint)).IsSimulated);
    }
    [Fact] public void Numeric_types_use_exact_server_type_and_check_after_float_rounding()
    {
        var definition=new ParameterDefinition(0,"x","s","EQ_Parameter1[0]",0,"","Time","",0,0,10000);
        foreach(var type in new[]{typeof(byte),typeof(sbyte),typeof(short),typeof(ushort),typeof(int),typeof(uint),typeof(long),typeof(ulong),typeof(float),typeof(double)})
            Assert.Equal(type,ParameterValueCodec.Parse("12",type,definition).GetType());
        Assert.Throws<InvalidOperationException>(()=>ParameterValueCodec.Parse("1.1",typeof(int),definition));
        Assert.Throws<InvalidOperationException>(()=>ParameterValueCodec.Parse("256",typeof(byte),definition));
        Assert.Throws<InvalidOperationException>(()=>ParameterValueCodec.Parse("0.1",typeof(float),definition with{MaxValue=0.1}));
        Assert.Equal(0.2f,ParameterValueCodec.Parse("0.2",typeof(float),definition));
        Assert.False(ParameterValueCodec.Same(0.2f,0.2d));
    }
    [Fact] public void Pending_audit_is_unknown_after_restart_and_failure_before_send_is_durable_fail_closed()
    {
        using var temp=new AlarmFeatureTests.TemporaryDirectory();
        var path=Path.Combine(temp.Path,"runtime.db"); var runtime=new EquipmentRuntimeRepository(path);
        var record=new OperationLogRecord(DateTimeOffset.Now,"u","p","write",false,false,""){Outcome="待处理"};
        runtime.BeginWrite(record);
        var restarted=new EquipmentRuntimeRepository(path);
        Assert.Equal("结果未确认",Assert.Single(restarted.Query(DateTimeOffset.MinValue,DateTimeOffset.MaxValue)).Outcome);
        var unavailable=new EquipmentRuntimeRepository(temp.Path); // directory cannot be a SQLite file
        Assert.NotEmpty(unavailable.StorageError);
        Assert.Throws<IOException>(()=>unavailable.BeginWrite(record));
    }
}
