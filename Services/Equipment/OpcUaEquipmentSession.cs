using Opc.Ua;
using Opc.Ua.Client;
using Small_square_cavity_coating_machine.Models.Equipment;
using Small_square_cavity_coating_machine.Services.Alarms;
using Small_square_cavity_coating_machine.Services.Equipment;

namespace Small_square_cavity_coating_machine.Services.Equipment
{
    public sealed class OpcUaEquipmentSessionFactory(string pkiDirectory) : IEquipmentSessionFactory
    {
        public async Task<IEquipmentSession> ConnectAsync(OpcUaAlarmOptions options, CancellationToken token) =>
            (IEquipmentSession)await new OpcUaAlarmArraySessionFactory(pkiDirectory).ConnectAsync(options, token).ConfigureAwait(false);
    }
}

namespace Small_square_cavity_coating_machine.Services.Alarms
{
    public sealed partial class OpcUaAlarmArraySession : IEquipmentSession
    {
        private readonly Dictionary<string, NodeId> _groupNodes = [];
        private readonly Dictionary<string, EquipmentBinding> _groupBindings = [];
        private readonly List<(MonitoredItem Item, MonitoredItemNotificationEventHandler Handler)> _equipmentHandlers = [];

        public async Task<IReadOnlyDictionary<string, EquipmentBinding>> DiscoverAsync(CancellationToken token)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            var candidates = EquipmentGroups.Monitored.ToDictionary(g => g, _ => new HashSet<NodeId>());
            var queue = new Queue<NodeId>();
            var visited = new HashSet<NodeId> { ObjectIds.ObjectsFolder };
            queue.Enqueue(ObjectIds.ObjectsFolder);
            while (queue.TryDequeue(out var parent))
            {
                foreach (var node in await BrowseAsync(parent, timeout.Token).ConfigureAwait(false))
                {
                    if (!visited.Add(node.Id)) continue;
                    if (visited.Count > 50000) throw new InvalidOperationException("设备节点浏览超过50000项，不能确认唯一绑定");
                    if (node.NodeClass == NodeClass.Variable && candidates.TryGetValue(node.BrowseName, out var matches))
                        matches.Add(node.Id);
                    if (node.NodeClass is NodeClass.Variable or NodeClass.Object) queue.Enqueue(node.Id);
                }
            }
            foreach (var group in EquipmentGroups.Monitored)
            {
                try
                {
                    var matches = new List<(NodeId Id, EquipmentBinding Binding)>();
                    var invalid = new List<string>();
                    foreach (var id in candidates[group])
                    {
                        var metadata = await ReadBindingAsync(group, id, token).ConfigureAwait(false);
                        if (metadata.Available) matches.Add((id, metadata));
                        else invalid.Add(metadata.Error);
                    }
                    if (matches.Count != 1) throw new InvalidOperationException(matches.Count == 0
                        ? "未找到唯一可读数组" + (invalid.Count > 0 ? "：" + string.Join("；", invalid.Distinct()) : "")
                        : "找到多个同名可读数组，禁止猜测绑定");
                    _groupNodes[group] = matches[0].Id;
                    _groupBindings[group] = matches[0].Binding;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex) { _groupBindings[group] = new(group, false, false, null, ex.Message); }
            }
            return new Dictionary<string, EquipmentBinding>(_groupBindings);
        }

        private async Task<EquipmentBinding> ReadBindingAsync(string group, NodeId id, CancellationToken token)
        {
            var values = await _session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                new ReadValueIdCollection {
                    new ReadValueId { NodeId = id, AttributeId = Attributes.ValueRank },
                    new ReadValueId { NodeId = id, AttributeId = Attributes.UserAccessLevel },
                    new ReadValueId { NodeId = id, AttributeId = Attributes.DataType }
                }, token).ConfigureAwait(false);
            if (values.Results.Count != 3 || values.Results.Any(v => !StatusCode.IsGood(v.StatusCode))
                || values.Results[0].Value is not int rank || (EquipmentGroups.IsScalar(group) ? rank != ValueRanks.Scalar : rank is not (0 or 1))
                || values.Results[1].Value is not byte access || (access & AccessLevels.CurrentRead) == 0)
                return new(group, false, false, null, "节点不是可读的一维数组");
            var dataType = values.Results[2].Value as NodeId;
            Type? type = dataType == DataTypeIds.Boolean ? typeof(bool)
                : dataType == DataTypeIds.Float ? typeof(float) : dataType == DataTypeIds.Double ? typeof(double)
                : dataType == DataTypeIds.SByte ? typeof(sbyte) : dataType == DataTypeIds.Byte ? typeof(byte)
                : dataType == DataTypeIds.Int16 ? typeof(short) : dataType == DataTypeIds.UInt16 ? typeof(ushort)
                : dataType == DataTypeIds.Int32 ? typeof(int) : dataType == DataTypeIds.UInt32 ? typeof(uint)
                : dataType == DataTypeIds.Int64 ? typeof(long) : dataType == DataTypeIds.UInt64 ? typeof(ulong) : null;
            if (group == EquipmentGroups.Process && type != typeof(float) && type != typeof(double))
                return new(group, false, false, type, "EQ_Process必须为Float/Double一维数组");
            return new(group, true, EquipmentGroups.IsWritable(group) && (access & AccessLevels.CurrentWrite) != 0,
                type, type is null ? "不支持的PLC元素数据类型" : "");
        }

        public async Task<DataValue> ReadGroupAsync(string group, int? index, CancellationToken token)
        {
            if (!_groupNodes.TryGetValue(group, out var node)) throw new InvalidOperationException($"未绑定{group}");
            var response = await _session.ReadAsync(null, 0, TimestampsToReturn.Both,
                new ReadValueIdCollection { new ReadValueId {
                    NodeId = node, AttributeId = Attributes.Value
                } }, token).ConfigureAwait(false);
            var data = response.Results.Single();
            if (index is not { } elementIndex) return data;
            if (data.Value is not Array { Rank: 1 } array || elementIndex < 0 || elementIndex >= array.Length)
                throw new InvalidOperationException($"{group}不是可索引的一维数组，或下标{elementIndex}越界");
            return new DataValue(new Variant(array.GetValue(elementIndex)))
            {
                StatusCode = data.StatusCode,
                SourceTimestamp = data.SourceTimestamp,
                ServerTimestamp = data.ServerTimestamp
            };
        }

        public async Task<IReadOnlyDictionary<string, string>> SubscribeGroupsAsync(IReadOnlyList<string> groups,
            Action<string, DataValue> onValue, CancellationToken token)
        {
            _subscription = new Subscription(_session.DefaultSubscription)
            {
                DisplayName = "Equipment.Alarm.IO.Parameter.Process.Recipe", PublishingEnabled = true,
                PublishingInterval = _options.PublishingIntervalMs, KeepAliveCount = 10, LifetimeCount = 30,
                SequentialPublishing = true
            };
            foreach (var group in groups)
            {
                var item = new MonitoredItem(_subscription.DefaultItem) {
                    DisplayName = group, StartNodeId = _groupNodes[group], AttributeId = Attributes.Value,
                    SamplingInterval = _options.SamplingIntervalMs, QueueSize = 100,
                    DiscardOldest = true, MonitoringMode = MonitoringMode.Reporting };
                MonitoredItemNotificationEventHandler handler = (sender, _) =>
                {
                    foreach (var value in sender.DequeueValues())
                    {
                        onValue(group, value);
                        if (group == EquipmentGroups.Alarm && value.StatusCode.Overflow)
                            _failure.TrySetResult("报警订阅队列溢出，需要重新核对报警状态");
                    }
                };
                item.Notification += handler;
                _equipmentHandlers.Add((item, handler));
                _subscription.AddItem(item);
            }
            _session.AddSubscription(_subscription);
            await _subscription.CreateAsync(token).ConfigureAwait(false);
            return _equipmentHandlers.Where(p => !p.Item.Status.Created || ServiceResult.IsBad(p.Item.Status.Error))
                .ToDictionary(p => p.Item.DisplayName, p => $"订阅失败：{p.Item.Status.Error}");
        }

        public async Task WriteParameterElementAsync(int index, object value, CancellationToken token)
            => await WriteElementAsync(EquipmentGroups.Parameter, index, value, token).ConfigureAwait(false);

        public async Task WriteElementAsync(string group, int? index, object value, CancellationToken token)
        {
            if (!_groupBindings.TryGetValue(group, out var binding) || !binding.Available || !binding.CanWrite
                || binding.ElementType != value.GetType() || index < 0 || EquipmentGroups.IsScalar(group) == index.HasValue)
                throw new ParameterWriteRejectedException($"{group}节点不可写或写入类型不匹配");
            if (index.HasValue)
            {
                var current = await ReadGroupAsync(group, null, token).ConfigureAwait(false);
                if (!StatusCode.IsGood(current.StatusCode) || current.StatusCode.Overflow
                    || current.Value is not Array { Rank: 1 } currentArray
                    || index.Value < 0 || index.Value >= currentArray.Length)
                    throw new ParameterWriteRejectedException($"{group}整组读取失败或下标越界，元素写入未执行");
                var array = Array.CreateInstance(value.GetType(), currentArray.Length);
                Array.Copy(currentArray, array, currentArray.Length);
                array.SetValue(value, index.Value);
                await WriteArrayPayloadAsync(group, array, token).ConfigureAwait(false);
                return;
            }
            await WriteScalarPayloadAsync(group, value, token).ConfigureAwait(false);
        }

        public async Task WriteArrayElementsAsync(string group, IReadOnlyList<ArrayWriteMutation> mutations, CancellationToken token)
        {
            if (mutations is null || mutations.Count == 0)
                throw new ParameterWriteRejectedException($"{group}无数组元素写入");
            if (!_groupBindings.TryGetValue(group, out var binding) || !binding.Available || !binding.CanWrite
                || binding.ElementType is null || EquipmentGroups.IsScalar(group))
                throw new ParameterWriteRejectedException($"{group}节点不可写或不是数组");
            var current = await ReadGroupAsync(group, null, token).ConfigureAwait(false);
            if (!StatusCode.IsGood(current.StatusCode) || current.StatusCode.Overflow
                || current.Value is not Array { Rank: 1 } currentArray)
                throw new ParameterWriteRejectedException($"{group}整组读取失败，数组写入未执行");
            var array = Array.CreateInstance(binding.ElementType, currentArray.Length);
            Array.Copy(currentArray, array, currentArray.Length);
            foreach (var mutation in mutations)
            {
                if (mutation.Index < 0 || mutation.Index >= array.Length)
                    throw new ParameterWriteRejectedException($"{group}下标越界：{mutation.Index}");
                if (mutation.Value.GetType() != binding.ElementType)
                    throw new ParameterWriteRejectedException($"{group}元素写入类型不匹配：{mutation.Index}");
                array.SetValue(mutation.Value, mutation.Index);
            }
            await WriteArrayPayloadAsync(group, array, token).ConfigureAwait(false);
        }

        private async Task WriteArrayPayloadAsync(string group, Array payload, CancellationToken token)
        {
            var response = await _session.WriteAsync(null, new WriteValueCollection {
                new WriteValue { NodeId = _groupNodes[group], AttributeId = Attributes.Value,
                    IndexRange = null,
                    Value = new DataValue(new Variant(payload)) }
            }, token).ConfigureAwait(false);
            var status = response.Results.Single();
            if (StatusCode.IsBad(status)) throw new ParameterWriteRejectedException($"PLC拒绝{group}写入：{status}");
            if (!StatusCode.IsGood(status)) throw new InvalidOperationException($"PLC未明确确认写入：{status}");
        }

        private async Task WriteScalarPayloadAsync(string group, object value, CancellationToken token)
        {
            var response = await _session.WriteAsync(null, new WriteValueCollection {
                new WriteValue { NodeId = _groupNodes[group], AttributeId = Attributes.Value,
                    IndexRange = null,
                    Value = new DataValue(new Variant(value)) }
            }, token).ConfigureAwait(false);
            var status = response.Results.Single();
            if (StatusCode.IsBad(status)) throw new ParameterWriteRejectedException($"PLC拒绝{group}写入：{status}");
            if (!StatusCode.IsGood(status)) throw new InvalidOperationException($"PLC未明确确认写入：{status}");
        }

        public async Task WriteRecipeValueAsync(string group, object value, CancellationToken token)
        {
            if (!EquipmentGroups.RecipePoints.Contains(group) || !_groupBindings.TryGetValue(group, out var binding)
                || !binding.Available || !binding.CanWrite) throw new InvalidOperationException("配方节点不可写");
            if (group == EquipmentGroups.Recipe
                ? value is not Array { Rank: 1, Length: 18 } || value.GetType().GetElementType() != binding.ElementType
                : value.GetType() != binding.ElementType)
                throw new InvalidOperationException($"配方写入类型不匹配：节点元素类型={RecipeTypeName(binding.ElementType)}，"
                    + $"输入类型={value.GetType().FullName}");

            object payload = value;
            string diagnostics;
            if (group == EquipmentGroups.Recipe)
            {
                var input = (Array)value;
                var current = await ReadGroupAsync(group, null, token).ConfigureAwait(false);
                if (!StatusCode.IsGood(current.StatusCode) || current.StatusCode.Overflow)
                    throw new InvalidOperationException($"EQ_Recipe1整组读取质量无效：{current.StatusCode}；写入未执行");
                if (current.Value is not Array { Rank: 1 } currentArray || currentArray.GetLowerBound(0) != 0)
                    throw new InvalidOperationException($"EQ_Recipe1当前值不是下界为0的一维数组："
                        + $"{current.Value?.GetType().FullName ?? "null"}；写入未执行");
                if (currentArray.GetType().GetElementType() != binding.ElementType)
                    throw new InvalidOperationException($"EQ_Recipe1节点元素类型={RecipeTypeName(binding.ElementType)}，"
                        + $"当前值类型={currentArray.GetType().FullName}；写入未执行");
                if (currentArray.Length < input.Length)
                    throw new InvalidOperationException($"EQ_Recipe1实际数组长度={currentArray.Length}，"
                        + $"不足配方覆盖项数={input.Length}；写入未执行");

                var fullArray = Array.CreateInstance(binding.ElementType!, currentArray.Length);
                Array.Copy(currentArray, fullArray, currentArray.Length);
                Array.Copy(input, 0, fullArray, 0, input.Length);
                payload = fullArray;
                diagnostics = $"节点元素类型={RecipeTypeName(binding.ElementType)}，PLC实际数组长度={currentArray.Length}，"
                    + $"配方输入类型={input.GetType().FullName}，配方覆盖项数={input.Length}，"
                    + $"最终发送载荷类型={fullArray.GetType().FullName}，最终发送载荷长度={fullArray.Length}";
            }
            else
            {
                diagnostics = $"节点元素类型={RecipeTypeName(binding.ElementType)}，发送载荷类型={value.GetType().FullName}";
            }

            var variant = new Variant(payload);
            if (group == EquipmentGroups.Recipe && (variant.TypeInfo.ValueRank != ValueRanks.OneDimension
                || binding.ElementType == typeof(float) && variant.TypeInfo.BuiltInType != BuiltInType.Float
                || binding.ElementType == typeof(double) && variant.TypeInfo.BuiltInType != BuiltInType.Double))
                throw new InvalidOperationException($"EQ_Recipe1 Variant编码类型异常：BuiltInType={variant.TypeInfo.BuiltInType}，"
                    + $"ValueRank={variant.TypeInfo.ValueRank}；{diagnostics}；写入未执行");
            var response = await _session.WriteAsync(null, new WriteValueCollection {
                new WriteValue { NodeId = _groupNodes[group], AttributeId = Attributes.Value,
                    IndexRange = null,
                    Value = new DataValue(variant) } }, token).ConfigureAwait(false);
            var status = response.Results.Single();
            if (!StatusCode.IsGood(status)) throw new InvalidOperationException($"PLC配方写入未成功：{status}；{diagnostics}；不降级、不重试");
        }

        private static string RecipeTypeName(Type? type) => type == typeof(float) ? "FLOAT(REAL)/System.Single"
            : type == typeof(double) ? "DOUBLE(LREAL)/System.Double"
            : type?.FullName ?? "未知";

        private void DetachEquipmentHandlers()
        {
            foreach (var entry in _equipmentHandlers) entry.Item.Notification -= entry.Handler;
            _equipmentHandlers.Clear();
        }
    }
}
