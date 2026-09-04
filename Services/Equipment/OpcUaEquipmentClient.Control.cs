using Opc.Ua;
using Small_square_cavity_coating_machine.Models.Alarms;
using Small_square_cavity_coating_machine.Models.Equipment;
using Small_square_cavity_coating_machine.Models.History;
using Small_square_cavity_coating_machine.Services.Alarms;
using System.Globalization;

namespace Small_square_cavity_coating_machine.Services.Equipment;

public sealed partial class OpcUaEquipmentClient
{
    public async Task<ControlWriteResult> WriteControlAsync(ControlWriteRequest request, CancellationToken token = default)
    {
        if (!await _lifecycle.WaitAsync(0, token).ConfigureAwait(false))
            return new(ControlWriteOutcome.Rejected, "已有写入或连接切换正在处理，本次未提交");

        OperationLogRecord? audit = null;
        var sent = false;
        object? target = null;
        object? previous = null;
        CancellationToken connectionToken = default;
        ControlWriteResult result;
        try
        {
            IEquipmentSession session;
            EquipmentPoint point;
            string group, endpoint, user;
            int? index;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_recipePending || _recipeLease is not null) throw new InvalidOperationException("配方任务运行期间禁止手动控制");
                if (!_authorization.CanOperate(request.Permission)) throw new InvalidOperationException("当前账号没有此控制权限");
                if (request.RequiresBuiltInAdministrator && !_authorization.IsBuiltInAdministrator)
                    throw new InvalidOperationException("只有当前登录的内置管理员可以解除或恢复互锁");
                if (!_addressGroups.TryGetValue(request.Address, out group!)) throw new InvalidOperationException("不是数据库定义的控制地址");
                point = _groups.GetValueOrDefault(group)?.Points.GetValueOrDefault(request.Address)
                    ?? throw new InvalidOperationException("控制点尚未建立快照");
                if (_phase != AlarmConnectionPhase.Connected || _session is null || _sessionLifetime is null
                    || point.Quality != AlarmQuality.Good || !point.CanWrite || point.ValueType is null)
                    throw new InvalidOperationException("PLC控制点未就绪、质量无效或不可写");
                if (request.Epoch != _epoch) throw new InvalidOperationException("连接已变化，请重新确认后操作");
                target = ConvertControlValue(request.Value, point.ValueType);
                index = EquipmentGroups.IsScalar(group) ? null : EquipmentAddress.Index(request.Address, group);
                session = _session;
                connectionToken = _sessionLifetime.Token;
                endpoint = _options.EndpointUrl;
                user = _authorization.CurrentUserName;
                _writeAddress = request.Address;
                _writeStage = "正在校验并保存控制审计";
                _writing = true;
                _writeDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Notify();
            }

            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, connectionToken);
            previous = await ReadControlElementAsync(session, group, index, point.ValueType!, lifetime.Token).ConfigureAwait(false);
            var pending = new OperationLogRecord(DateTimeOffset.Now, user, request.Target, request.Action,
                ParameterValueCodec.Format(target), false, false, "", IsSimulated)
            {
                Endpoint = endpoint,
                Address = request.Address,
                PreviousValue = ParameterValueCodec.Format(previous),
                Outcome = "待处理"
            };
            await Task.Run(() => _runtime.BeginWrite(pending), lifetime.Token).ConfigureAwait(false);
            audit = pending;

            if (!_authorization.CanOperate(request.Permission) || _authorization.CurrentUserName != user
                || request.RequiresBuiltInAdministrator && !_authorization.IsBuiltInAdministrator)
                throw new InvalidOperationException("账号或权限已变化，本次未发送");
            lock (_gate)
                if (_epoch != request.Epoch || _session != session || _phase != AlarmConnectionPhase.Connected
                    || !_groups[group].Points[request.Address].CanWrite)
                    throw new InvalidOperationException("控制点状态已变化，本次未发送");

            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                sent = true;
                if (request.ArrayMutations is { Count: > 0 })
                {
                    if (index is null) throw new InvalidOperationException("标量控制点不能批量数组写入");
                    var mutations = new List<ArrayWriteMutation>(request.ArrayMutations.Count + 1) { new(index.Value, target!) };
                    mutations.AddRange(request.ArrayMutations);
                    await session.WriteArrayElementsAsync(group, mutations, deadline.Token).ConfigureAwait(false);
                }
                else
                {
                    await session.WriteElementAsync(group, index, target, deadline.Token).ConfigureAwait(false);
                }
            }

            lock (_gate)
            {
                var stage = request.Confirm == ControlConfirm.ReadBack ? "已发送，等待回读" : "已发送，等待反馈";
                _message = $"{request.Target}{request.Action}已发送";
                _writeStage = stage;
                Notify();
            }

            switch (request.Confirm)
            {
                case ControlConfirm.ReadBack:
                {
                    using var readbackDeadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    readbackDeadline.CancelAfter(TimeSpan.FromMilliseconds(request.ConfirmTimeoutMs));
                    while (true)
                    {
                        var actual = await ReadControlElementAsync(session, group, index, point.ValueType!, readbackDeadline.Token).ConfigureAwait(false);
                        if (ParameterValueCodec.Same(actual, target)) break;
                        await Task.Delay(100, readbackDeadline.Token).ConfigureAwait(false);
                    }
                    result = new(ControlWriteOutcome.Confirmed, "PLC变量写入已回读确认", target);
                    break;
                }
                case ControlConfirm.Sent:
                    result = new(ControlWriteOutcome.Confirmed, $"{request.Target}{request.Action}命令已发送", target);
                    break;
                case ControlConfirm.State:
                    result = await WaitStateConfirmationAsync(request, session, lifetime.Token).ConfigureAwait(false);
                    break;
                default:
                    result = new(ControlWriteOutcome.Rejected, "未知的写入确认模式，未提交");
                    break;
            }
        }
        catch (ParameterWriteRejectedException ex)
        {
            result = new(ControlWriteOutcome.Rejected, ex.Message);
        }
        catch (Exception ex)
        {
            var reason = connectionToken.IsCancellationRequested ? "通讯中断或程序关闭"
                : ex is OperationCanceledException && token.IsCancellationRequested ? "操作已取消，命令可能已经发送"
                : ex is OperationCanceledException && sent ? "5秒内未获得相同值回读"
                : ex is OperationCanceledException ? "写入请求超时"
                : ex.Message;
            result = new(sent ? ControlWriteOutcome.Unknown : ControlWriteOutcome.Rejected,
                sent ? $"结果未确认：{reason}；未自动重试" : $"未写入：{reason}");
        }

        try
        {
            if (audit is not null)
            {
                var final = audit with
                {
                    IsSuccessful = result.Outcome == ControlWriteOutcome.Confirmed,
                    Outcome = result.Outcome == ControlWriteOutcome.Confirmed ? "已确认"
                        : result.Outcome == ControlWriteOutcome.Unknown ? "结果未确认" : "已拒绝",
                    FailureReason = result.Outcome == ControlWriteOutcome.Confirmed ? "" : result.Message
                };
                try { await Task.Run(() => _runtime.FinishWrite(final, null), CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { result = result with { Message = result.Message + "；本机记录保存失败：" + ex.Message }; }
            }
            return result;
        }
        finally { EndWrite(); }
    }

    private async Task<ControlWriteResult> WaitStateConfirmationAsync(ControlWriteRequest request,
        IEquipmentSession session, CancellationToken token)
    {
        var stateAddress = request.StateAddress
            ?? throw new InvalidOperationException("状态确认缺少Part_State地址");
        var stateGroup = EquipmentAddress.Group(stateAddress);
        var stateIndex = EquipmentAddress.Index(stateAddress, stateGroup);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(request.ConfirmTimeoutMs));
        var confirmed = false;
        var faulted = false;
        try
        {
            while (true)
            {
                var data = await session.ReadGroupAsync(stateGroup, stateIndex, deadline.Token).ConfigureAwait(false);
                object? word = data.Value is Array { Rank: 1, Length: 1 } array ? array.GetValue(0) : data.Value;
                if (!StatusCode.IsGood(data.StatusCode) || data.StatusCode.Overflow || word is null
                    || word is not bool && !ParameterValueCodec.IsValid(word))
                    throw new InvalidOperationException("状态点回读质量或数据类型无效");
                var value = Convert.ToUInt64(word);
                if ((value & (1UL << 7)) != 0) { faulted = true; break; }
                var expectedBit = request.ExpectOpen ? 1UL << 1 : 1UL;
                if ((value & expectedBit) != 0) { confirmed = true; break; }
                await Task.Delay(100, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !token.IsCancellationRequested)
        {
            // 达到确认超时，按“已发送但未确认”处理。
        }
        if (confirmed)
            return new(ControlWriteOutcome.Confirmed, $"{request.Target}{request.Action}状态已确认", request.Value);
        if (faulted)
            return new(ControlWriteOutcome.Rejected, $"{request.Target}{request.Action}状态故障");
        return new(ControlWriteOutcome.Unknown, $"{request.Target}{request.Action}已发送，5秒内状态未确认");
    }

    private static object ConvertControlValue(object value, Type targetType)
    {
        if (value.GetType() == targetType) return value;
        if (!ParameterValueCodec.IsNumeric(targetType) || value is not IConvertible)
            throw new InvalidOperationException("控制值与PLC数据类型不匹配");
        try
        {
            var converted = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
            if (!ParameterValueCodec.IsValid(converted)) throw new InvalidOperationException("控制值不是有限数值");
            return converted!;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidOperationException("控制值超出PLC数据类型范围", ex);
        }
    }

    private static async Task<object> ReadControlElementAsync(IEquipmentSession session, string group, int? index,
        Type type, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var data = await session.ReadGroupAsync(group, index, deadline.Token).ConfigureAwait(false);
        object? value = index.HasValue && data.Value is Array { Rank: 1, Length: 1 } array ? array.GetValue(0) : data.Value;
        if (!StatusCode.IsGood(data.StatusCode) || data.StatusCode.Overflow || value?.GetType() != type
            || value is not bool && !ParameterValueCodec.IsValid(value))
            throw new InvalidOperationException("控制点回读质量或数据类型无效");
        return value;
    }
}
