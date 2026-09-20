using Opc.Ua;
using Small_square_cavity_coating_machine.Models.Equipment;
using Small_square_cavity_coating_machine.Services.Equipment;
using System.Collections.Concurrent;
using Small_square_cavity_coating_machine.Services.Recipes;
using Small_square_cavity_coating_machine.Models.Recipes;
using Opc.Ua.Configuration;
using Opc.Ua.Server;
using Small_square_cavity_coating_machine.Services.Alarms;
using Small_square_cavity_coating_machine.Services.History;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace Small_square_cavity_coating_machine.Tests;

/// <summary>Real OPC UA on loopback only; never contacts the configured machine endpoint.</summary>
public sealed class AlarmOpcUaLoopbackTests
{
    private sealed class TestServer : StandardServer
    {
        public ArrayNodes Nodes = null!;
        protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            Nodes = new ArrayNodes(server, configuration);
            return new MasterNodeManager(server, configuration, null, new INodeManager[] { Nodes });
        }
    }

    private sealed class ArrayNodes(IServerInternal server, ApplicationConfiguration config)
        : CustomNodeManager2(server, config, "urn:coating:loopback:test")
    {
        private BaseDataVariableState<bool[]> _array = null!;
        private BaseDataVariableState<bool[]> _io = null!;
        private BaseDataVariableState<float[]> _parameters = null!;
        private BaseDataVariableState<float[]> _process = null!;
        private FolderState _folder = null!;
        private BaseDataVariableState<float[]> _recipe = null!;
        private BaseDataVariableState<bool> _recipeOk = null!, _coatOk = null!, _recipeLoad = null!;
        public float[] RecipeValues { get { lock (Lock) return _recipe.Value.ToArray(); } }
        public bool RejectRecipeRange;
        public readonly ConcurrentQueue<string> RecipeOperations = new();
        public bool RejectIndexRange;
        public ConcurrentQueue<(NodeId Node, string Range, object Value)> Writes { get; } = new();
        public override void Write(OperationContext context, IList<WriteValue> nodesToWrite, IList<ServiceResult> errors)
        {
            for (var i = 0; i < nodesToWrite.Count; i++)
            {
                var write = nodesToWrite[i];
                if (write.NodeId.NamespaceIndex != NamespaceIndex) continue;
                Writes.Enqueue((write.NodeId, write.IndexRange, write.Value.Value));
                if (RejectIndexRange && write.NodeId == _parameters.NodeId)
                { errors[i] = new ServiceResult(StatusCodes.BadIndexRangeInvalid); write.Processed = true; }
            }
            for (var i = 0; i < nodesToWrite.Count; i++)
                if (RejectRecipeRange && nodesToWrite[i].NodeId == _recipe.NodeId)
                { errors[i] = new ServiceResult(StatusCodes.BadIndexRangeInvalid); nodesToWrite[i].Processed = true; }
            base.Write(context, nodesToWrite, errors);
            foreach (var write in nodesToWrite)
            {
                if (write.NodeId == _recipe.NodeId) RecipeOperations.Enqueue("write:array:" + write.IndexRange);
                if (write.NodeId == _recipeLoad.NodeId) RecipeOperations.Enqueue("write:load:" + write.Value.Value);
                if (write.NodeId == _coatOk.NodeId) RecipeOperations.Enqueue("write:coat:" + write.Value.Value);
                if (write.NodeId == _recipeOk.NodeId)
                {
                    RecipeOperations.Enqueue("write:recipe:" + write.Value.Value);
                    _ = CompleteLayerAsync();
                }
            }
        }
        private async Task CompleteLayerAsync()
        {
            await Task.Delay(400);
            lock (Lock) { _recipeOk.Value = true; _recipeOk.Timestamp = DateTime.UtcNow; _recipeOk.ClearChangeMasks(SystemContext, false); }
        }
        public override void Read(OperationContext context, double maxAge, IList<ReadValueId> nodes, IList<DataValue> values, IList<ServiceResult> errors)
        {
            base.Read(context, maxAge, nodes, values, errors);
            foreach (var node in nodes) if (node.NodeId == _recipe.NodeId && node.AttributeId == Attributes.Value)
                RecipeOperations.Enqueue("read:array");
        }
        public float[] ParameterValues { get { lock (Lock) return _parameters.Value.ToArray(); } }
        public void ChangeIo(int index, bool value)
        {
            lock (Lock)
            {
                var next = _io.Value.ToArray(); next[index] = value; _io.Value = next;
                _io.Timestamp = DateTime.UtcNow; _io.ClearChangeMasks(SystemContext, false);
            }
        }
        public void SetProcess(float[] values, StatusCode quality)
        {
            lock (Lock)
            {
                _process.Value = values; _process.StatusCode = quality; _process.Timestamp = DateTime.UtcNow;
                _process.ClearChangeMasks(SystemContext, false);
            }
        }
        public void AddDuplicateProcess()
        {
            lock (Lock)
            {
                var duplicate = new BaseDataVariableState<float[]>(_folder) {
                    NodeId = new NodeId("duplicate.process", NamespaceIndex), BrowseName = new QualifiedName("EQ_Process", NamespaceIndex),
                    DisplayName = "Duplicate process", ReferenceTypeId = ReferenceTypeIds.HasComponent, TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                    DataType = DataTypeIds.Float, ValueRank = ValueRanks.OneDimension, ArrayDimensions = new uint[] {8},
                    AccessLevel = AccessLevels.CurrentRead, UserAccessLevel = AccessLevels.CurrentRead, Value = new float[8] };
                _folder.AddChild(duplicate); AddPredefinedNode(SystemContext, duplicate);
            }
        }
        public void AddDuplicateIo()
        {
            lock (Lock)
            {
                var copy = new BaseDataVariableState<bool[]>(_folder) {
                    NodeId = new NodeId("duplicate.io", NamespaceIndex),
                    BrowseName = new QualifiedName("EQ_IO", NamespaceIndex), DisplayName = "Duplicate IO",
                    ReferenceTypeId = ReferenceTypeIds.HasComponent, TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                    DataType = DataTypeIds.Boolean, ValueRank = ValueRanks.OneDimension, ArrayDimensions = new uint[] {532},
                    AccessLevel = AccessLevels.CurrentRead, UserAccessLevel = AccessLevels.CurrentRead, Value = new bool[532] };
                _folder.AddChild(copy); AddPredefinedNode(SystemContext, copy);
            }
        }
        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Lock)
            {
                var folder = new FolderState(null)
                {
                    NodeId = new NodeId("program", NamespaceIndex),
                    BrowseName = new QualifiedName("Program", NamespaceIndex),
                    DisplayName = "Program", TypeDefinitionId = ObjectTypeIds.FolderType
                };
                folder.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
                if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out var references))
                    externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
                references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, folder.NodeId));
                _array = new BaseDataVariableState<bool[]>(folder)
                {
                    NodeId = new NodeId("vendor.nonmatching.identifier", NamespaceIndex),
                    BrowseName = new QualifiedName("EQ_Alarm", NamespaceIndex),
                    DisplayName = "PLC alarm array",
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                    DataType = DataTypeIds.Boolean, ValueRank = ValueRanks.OneDimension,
                    ArrayDimensions = new uint[] { 552 },
                    AccessLevel = AccessLevels.CurrentRead, UserAccessLevel = AccessLevels.CurrentRead,
                    Value = new bool[552], StatusCode = StatusCodes.Good, Timestamp = DateTime.UtcNow
                };
                _array.Value[500] = true;
                folder.AddChild(_array);
                _folder = folder;
                _io = new BaseDataVariableState<bool[]>(folder) {
                    NodeId = new NodeId("vendor.IO.nonmatching", NamespaceIndex),
                    BrowseName = new QualifiedName("EQ_IO", NamespaceIndex), DisplayName = "IO",
                    ReferenceTypeId = ReferenceTypeIds.HasComponent, TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                    DataType = DataTypeIds.Boolean, ValueRank = ValueRanks.OneDimension, ArrayDimensions = new uint[] {532},
                    AccessLevel = AccessLevels.CurrentRead, UserAccessLevel = AccessLevels.CurrentRead,
                    Value = new bool[532], StatusCode = StatusCodes.Good, Timestamp = DateTime.UtcNow };
                _parameters = new BaseDataVariableState<float[]>(folder) {
                    NodeId = new NodeId("vendor.parameters.nonmatching", NamespaceIndex),
                    BrowseName = new QualifiedName("EQ_Parameter1", NamespaceIndex), DisplayName = "Parameters",
                    ReferenceTypeId = ReferenceTypeIds.HasComponent, TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                    DataType = DataTypeIds.Float, ValueRank = ValueRanks.OneDimension, ArrayDimensions = new uint[] {21},
                    AccessLevel = AccessLevels.CurrentReadOrWrite, UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                    Value = Enumerable.Repeat(5f,21).ToArray(), StatusCode = StatusCodes.Good, Timestamp = DateTime.UtcNow };
                _process = new BaseDataVariableState<float[]>(folder) {
                    NodeId = new NodeId("vendor.process.actual", NamespaceIndex),
                    BrowseName = new QualifiedName("EQ_Process", NamespaceIndex), DisplayName = "Process",
                    ReferenceTypeId = ReferenceTypeIds.HasComponent, TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                    DataType = DataTypeIds.Float, ValueRank = ValueRanks.OneDimension, ArrayDimensions = new uint[] {8},
                    AccessLevel = AccessLevels.CurrentRead, UserAccessLevel = AccessLevels.CurrentRead,
                    Value = new float[] {999, 1, 2, 3, 4, 5, 6, 7}, StatusCode = StatusCodes.Good, Timestamp = DateTime.UtcNow };
                folder.AddChild(_process);
                folder.AddChild(_io); folder.AddChild(_parameters);
                _recipe = new BaseDataVariableState<float[]>(folder) {
                    NodeId = new NodeId("vendor.recipe.block", NamespaceIndex), BrowseName = new QualifiedName("EQ_Recipe1", NamespaceIndex),
                    DisplayName = "Recipe", ReferenceTypeId = ReferenceTypeIds.HasComponent, TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                    DataType = DataTypeIds.Float, ValueRank = ValueRanks.OneDimension, ArrayDimensions = new uint[] {200},
                    AccessLevel = AccessLevels.CurrentReadOrWrite, UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                    Value = Enumerable.Repeat(99f, 200).ToArray(), StatusCode = StatusCodes.Good, Timestamp = DateTime.UtcNow };
                BaseDataVariableState<bool> Flag(string browse, bool initial, bool writeOnly = false) => new(folder) {
                    NodeId = new NodeId("vendor." + browse, NamespaceIndex), BrowseName = new QualifiedName(browse, NamespaceIndex),
                    DisplayName = browse, ReferenceTypeId = ReferenceTypeIds.HasComponent, TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                    DataType = DataTypeIds.Boolean, ValueRank = ValueRanks.Scalar,
                    AccessLevel = writeOnly ? AccessLevels.CurrentWrite : AccessLevels.CurrentReadOrWrite,
                    UserAccessLevel = writeOnly ? AccessLevels.CurrentWrite : AccessLevels.CurrentReadOrWrite,
                    Value = initial, StatusCode = StatusCodes.Good, Timestamp = DateTime.UtcNow };
                _recipeOk = Flag("EQ_RecipeOK", false); _coatOk = Flag("EQ_CoatOK", true); _recipeLoad = Flag("EQ_Recipe1Load", false, writeOnly: true);
                folder.AddChild(_recipe); folder.AddChild(_recipeOk); folder.AddChild(_coatOk); folder.AddChild(_recipeLoad);
                AddPredefinedNode(SystemContext, folder);
            }
        }

        public void Set(int index, bool active)
        {
            lock (Lock)
            {
                var next = _array.Value.ToArray(); next[index] = active;
                _array.Value = next;
                _array.Timestamp = DateTime.UtcNow;
                _array.ClearChangeMasks(SystemContext, false);
            }
        }
    }

    [Fact]
    public async Task Anonymous_None_real_session_browses_reads_and_subscribes_array_without_client_certificate()
    {
        using var temp = new AlarmFeatureTests.TemporaryDirectory();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var portProbe = new TcpListener(IPAddress.Loopback, 0);
        portProbe.Start();
        var port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();
        var endpoint = $"opc.tcp://127.0.0.1:{port}";
        var configuration = new ApplicationConfiguration
        {
            ApplicationName = "CoatingLoopbackTest", ApplicationUri = "urn:localhost:coating:loopback:test",
            ApplicationType = ApplicationType.Server,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier { StoreType = "Directory",
                    StorePath = Path.Combine(temp.Path, "server-own"), SubjectName = "CN=CoatingLoopbackTest" },
                TrustedPeerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = Path.Combine(temp.Path, "trusted") },
                TrustedIssuerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = Path.Combine(temp.Path, "issuers") },
                RejectedCertificateStore = new CertificateStoreIdentifier { StoreType = "Directory", StorePath = Path.Combine(temp.Path, "rejected") }
            },
            TransportQuotas = new TransportQuotas(),
            ServerConfiguration = new ServerConfiguration
            {
                BaseAddresses = new StringCollection { endpoint },
                SecurityPolicies = new ServerSecurityPolicyCollection { new ServerSecurityPolicy {
                    SecurityMode = MessageSecurityMode.None, SecurityPolicyUri = SecurityPolicies.None } },
                UserTokenPolicies = new UserTokenPolicyCollection { new UserTokenPolicy(UserTokenType.Anonymous) }
            }
        };
        await configuration.ValidateAsync(ApplicationType.Server, deadline.Token);
        var application = new ApplicationInstance(configuration, DefaultTelemetry.Create(_ => { }));
        Assert.True(await application.CheckApplicationInstanceCertificatesAsync(true, null, deadline.Token));
        using var server = new TestServer();
        await server.StartAsync(configuration, deadline.Token);
        try
        {
            var settings = new AlarmArrayConnectionTests.Settings { Options = new OpcUaAlarmOptions { EndpointUrl = endpoint } };
            await using var source = new OpcUaAlarmSignalSource(settings,
                new OpcUaAlarmArraySessionFactory(Path.Combine(temp.Path, "client-pki")));
            var repository = new InMemoryAlarmLogRepository();
            var definitions = new SqliteAlarmDefinitionRepository(SqliteAlarmDefinitionRepository.ExtractEmbeddedDatabase(temp.Path)).Load();
            using var monitor = new AlarmMonitorService(definitions, source, repository);
            await source.StartAsync(definitions, deadline.Token);
            try { await AlarmArrayConnectionTests.Until(() => monitor.Snapshot().AllSignalsKnown); }
            catch (Exception ex) { throw new InvalidOperationException(source.Status.Message, ex); }
            var first = Assert.Single(repository.LoadUncleared());
            Assert.Equal("EQ_Alarm[500]", first.Address);
            server.Nodes.Set(500, false);
            await AlarmArrayConnectionTests.Until(() => repository.LoadUncleared().Count == 0);
            server.Nodes.Set(551, true);
            await AlarmArrayConnectionTests.Until(() => repository.LoadUncleared().Count == 1);
            Assert.Equal("EQ_Alarm[551]", Assert.Single(repository.LoadUncleared()).Address);
            Assert.Equal(2, repository.Query(DateTimeOffset.MinValue, DateTimeOffset.MaxValue).Count);
            await source.DisposeAsync();
            var definitionRepository = new SqliteEquipmentDefinitionRepository(SqliteAlarmDefinitionRepository.ExtractEmbeddedDatabase(temp.Path));
            var factory = new OpcUaEquipmentSessionFactory(Path.Combine(temp.Path, "shared-client"));
            var runtime = new EquipmentRuntimeRepository(Path.Combine(temp.Path, "runtime.db"));
            var processDefinitions = new SqliteProcessDefinitionRepository(SqliteAlarmDefinitionRepository.ExtractEmbeddedDatabase(temp.Path)).Load();
            await using var client = new OpcUaEquipmentClient(settings, factory, definitions,
                definitionRepository.LoadIo(), definitionRepository.LoadParameters(), new EquipmentFeatureTests.Authorization { RecipeAllowed = true },
                runtime, enableRecipes: true, processDefinitions: processDefinitions);
            await client.StartAsync(deadline.Token);
            try { await AlarmArrayConnectionTests.Until(() => client.Snapshot().Groups.Values.All(g => g.IsReady)); }
            catch (Exception ex) { throw new InvalidOperationException(client.Status.Message, ex); }
            await using var telemetry = new OpcUaTelemetrySource(client, processDefinitions);
            Assert.Equal(1, telemetry.Capture(DateTimeOffset.Now).HighVacuumPa);
            Assert.Equal(7, telemetry.Capture(DateTimeOffset.Now).TemperatureC);
            server.Nodes.SetProcess(new float[] {999, 11, 12, 13, 14, 15, 16, 17}, StatusCodes.Good);
            await AlarmArrayConnectionTests.Until(() => telemetry.Capture(DateTimeOffset.Now).TemperatureC == 17);
            server.Nodes.SetProcess(new float[8], StatusCodes.BadCommunicationError);
            await AlarmArrayConnectionTests.Until(() => double.IsNaN(telemetry.Capture(DateTimeOffset.Now).HighVacuumPa));
            server.Nodes.SetProcess(new float[] {999, 21, 22, 23, 24, 25, 26, 27}, StatusCodes.Good);
            await AlarmArrayConnectionTests.Until(() => telemetry.Capture(DateTimeOffset.Now).HighVacuumPa == 21);
            Assert.All(client.Snapshot().Groups[EquipmentGroups.Process].Points.Values, p => Assert.False(p.CanWrite));
            Assert.Empty(server.Nodes.Writes);
            server.Nodes.ChangeIo(500, true);
            await AlarmArrayConnectionTests.Until(() => client.Snapshot().Groups[EquipmentGroups.Io].Points["EQ_IO[500]"].Value is true);
            var before = server.Nodes.ParameterValues;
            var writeResult = await client.WriteParameterAsync(new("EQ_Parameter1[2]", "0.25", 5f, client.Snapshot().Epoch), deadline.Token);
            Assert.Equal(ParameterWriteOutcome.Confirmed, writeResult.Outcome);
            var request = Assert.Single(server.Nodes.Writes);
            Assert.True(string.IsNullOrEmpty(request.Range));
            var paramWrite = Assert.IsType<float[]>(request.Value);
            Assert.Equal(21, paramWrite.Length);
            Assert.Equal(0.25f, paramWrite[2]);
            var after = server.Nodes.ParameterValues;
            Assert.Equal(0.25f, after[2]);
            for (var i = 0; i < 21; i++) if (i != 2) Assert.Equal(before[i], after[i]);
            server.Nodes.RejectIndexRange = true;
            var rejected = await client.WriteParameterAsync(new("EQ_Parameter1[0]", "7", 5f, client.Snapshot().Epoch), deadline.Token);
            Assert.Equal(ParameterWriteOutcome.Rejected, rejected.Outcome);
            Assert.Equal(2, server.Nodes.Writes.Count);
            Assert.All(server.Nodes.Writes, r =>
            {
                Assert.Equal("vendor.parameters.nonmatching", r.Node.Identifier);
                Assert.True(string.IsNullOrEmpty(r.Range));
            });
            Assert.Equal(5f, server.Nodes.ParameterValues[0]);
            server.Nodes.Writes.Clear(); server.Nodes.RecipeOperations.Clear();
            var recipeGateway = new OpcUaRecipePlcGateway(client, runtime,
                new SqliteRecipeDefinitionRepository(SqliteAlarmDefinitionRepository.ExtractEmbeddedDatabase(temp.Path)).Load());
            var dispatch = new RecipeDispatchService(recipeGateway, runtime);
            var run = await dispatch.RunAsync(new(RecipeDispatchMode.Selected,
                [new RecipeLayer { Sequence = 2, CathodeAPower = 230 }, new RecipeLayer { Sequence = 5, CathodeBPower = 320 }],
                "序号2、5") { RecipeName = "回环实协议" }, new Progress<RecipeRunProgress>(), deadline.Token);
            Assert.True(run.IsCompleted, run.FailureReason);
            Assert.Contains("PLC实际数组长度=200", run.Notice);
            Assert.Contains("最终发送载荷长度=200", run.Notice);
            var recipeWrites = server.Nodes.Writes.ToArray();
            Assert.Equal(7, recipeWrites.Length);
            Assert.Equal("vendor.EQ_Recipe1Load", recipeWrites[0].Node.Identifier); Assert.True((bool)recipeWrites[0].Value);
            Assert.Equal("vendor.EQ_CoatOK", recipeWrites[1].Node.Identifier); Assert.False((bool)recipeWrites[1].Value);
            Assert.Equal("vendor.EQ_CoatOK", recipeWrites[^1].Node.Identifier); Assert.True((bool)recipeWrites[^1].Value);
            foreach (var item in recipeWrites.Where(w => w.Node.Identifier.Equals("vendor.recipe.block")))
            {
                Assert.True(string.IsNullOrEmpty(item.Range));
                var payload = Assert.IsType<float[]>(item.Value);
                Assert.Equal(200, payload.Length);
                var variant = new Variant(payload);
                Assert.Equal(BuiltInType.Float, variant.TypeInfo.BuiltInType);
                Assert.Equal(ValueRanks.OneDimension, variant.TypeInfo.ValueRank);
                Assert.All(payload.Skip(18), v => Assert.Equal(99f, v));
            }
            Assert.All(server.Nodes.RecipeValues.Skip(18), v => Assert.Equal(99f, v));
            var ops = server.Nodes.RecipeOperations.ToArray();
            var blocks = ops.Select((v,i) => (v,i)).Where(x => x.v == "write:array:").ToArray();
            foreach (var block in blocks)
            {
                var reset = Array.FindIndex(ops, block.i + 1, s => s == "write:recipe:False");
                Assert.True(reset > block.i); Assert.Contains("read:array", ops[(block.i+1)..reset]);
            }
            server.Nodes.RejectRecipeRange = true; server.Nodes.Writes.Clear();
            var rejectedRecipe = await dispatch.RunAsync(new(RecipeDispatchMode.All, [new RecipeLayer { Sequence = 1 }], "序号1"),
                new Progress<RecipeRunProgress>(), deadline.Token);
            Assert.False(rejectedRecipe.IsCompleted); Assert.Equal(3, server.Nodes.Writes.Count);
            Assert.DoesNotContain(server.Nodes.Writes, w => w.Node.Identifier.Equals("vendor.EQ_RecipeOK"));
            Assert.True(string.IsNullOrEmpty(server.Nodes.Writes.Last().Range));
            Assert.All(server.Nodes.RecipeValues.Skip(18), v => Assert.Equal(99f, v));
            await client.DisposeAsync();
            Assert.True(double.IsNaN(telemetry.Capture(DateTimeOffset.Now).TemperatureC));
            server.Nodes.AddDuplicateIo();
            server.Nodes.AddDuplicateProcess();
            await using var discover = await factory.ConnectAsync(settings.Options, deadline.Token);
            var bindings = await discover.DiscoverAsync(deadline.Token);
            Assert.False(bindings[EquipmentGroups.Process].Available);
            Assert.Contains("多个", bindings[EquipmentGroups.Process].Error);
            Assert.False(bindings[EquipmentGroups.Io].Available);
            Assert.Contains("多个", bindings[EquipmentGroups.Io].Error);
            Assert.True(bindings[EquipmentGroups.Alarm].Available);
            Assert.True(bindings[EquipmentGroups.Parameter].Available);
        }
        finally { await server.StopAsync(CancellationToken.None); }
    }
}
