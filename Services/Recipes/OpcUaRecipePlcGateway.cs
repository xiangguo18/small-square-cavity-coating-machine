using Small_square_cavity_coating_machine.Models.Equipment;
using Small_square_cavity_coating_machine.Models.History;
using Small_square_cavity_coating_machine.Models.Recipes;
using Small_square_cavity_coating_machine.Services.Equipment;
using System.Text.Json;

namespace Small_square_cavity_coating_machine.Services.Recipes;

/// <summary>Uses the shared equipment session. Only the verified protocol writes recipe/handshake nodes.</summary>
public sealed class OpcUaRecipePlcGateway(IOpcUaEquipmentClient client, IEquipmentRuntimeRepository runtime,
    IReadOnlyList<RecipeDefinition> definitions, string definitionError = "") : IRecipePlcGateway
{
    private IRecipeEquipmentLease? _lease;
    private OperationLogRecord? _runAudit;
    private readonly Dictionary<int, Array> _blocks = [];
    private string _stageFailure = "";
    public bool IsAvailable => definitionError.Length == 0 && client.RecipeAvailable;
    public bool IsSimulated => client.IsSimulated;
    public string Notice { get; private set; } = "";
    public event EventHandler? AvailabilityChanged { add => client.SnapshotChanged += value; remove => client.SnapshotChanged -= value; }

    public Task<RecipeGatewayPreflightResult> PreflightAsync(CancellationToken cancellationToken) =>
        Task.FromResult(IsAvailable ? RecipeGatewayPreflightResult.Ready :
            RecipeGatewayPreflightResult.Blocked(definitionError.Length > 0 ? definitionError : "配方节点未就绪、不可写或质量无效"));

    public async Task BeginRunAsync(RecipeRunRequest request, CancellationToken token)
    {
        Notice = ""; _stageFailure = ""; _blocks.Clear();
        if (request.Layers.Count == 0 || request.Layers.Select(l => l.Sequence).Distinct().Count() != request.Layers.Count)
            throw new InvalidOperationException("待执行配方为空或序号重复");
        _lease = await client.AcquireRecipeRunAsync(token).ConfigureAwait(false);
        // 诊断：上报 EQ_Recipe1 识别到的元素类型与下发项数，便于比对 PLC 的 REAL 数组。
        Notice = client.RecipeNodeDiagnostics;
        foreach (var layer in request.Layers) _blocks.Add(layer.Sequence, RecipeDefinitions.ConvertLayer(layer, _lease.ArrayType, definitions));
        var pending = new OperationLogRecord(DateTimeOffset.Now, _lease.User, request.RecipeName, "开始配方任务",
            false, false, "") {
            RecipeRunId = request.RunId, RecipeSnapshot = JsonSerializer.Serialize(request),
            Outcome = "配方运行中" };
        await Task.Run(() => runtime.BeginWrite(pending), token).ConfigureAwait(false);
        _runAudit = pending;
        await _lease.TransactionAsync(async t =>
        {
            await TriggerRecipeLoadAsync(t).ConfigureAwait(false);
            await FlagAsync(EquipmentGroups.CoatOk, false, t).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
    }
    public async Task SendLayerAsync(RecipeLayer layer, CancellationToken token)
    {
        var lease = _lease ?? throw new InvalidOperationException("未开始配方任务");
        if (_stageFailure.Length > 0) throw new InvalidOperationException(_stageFailure);
        var block = _blocks[layer.Sequence];
        await lease.TransactionAsync(async t =>
        {
            await StageAsync($"层{layer.Sequence}：写入并回读18项", "EQ_Recipe1[0:17]", JsonSerializer.Serialize(block.Cast<object>()),
                async () => {
                    await lease.WriteAsync(EquipmentGroups.Recipe, block, t).ConfigureAwait(false);
                    await lease.ConfirmAsync(EquipmentGroups.Recipe, block, t).ConfigureAwait(false);
                }, t).ConfigureAwait(false);
            await StageAsync($"层{layer.Sequence}：清零启动并确认", EquipmentGroups.RecipeOk, "0",
                () => lease.StartLayerAsync(t), t).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
    }
    public async Task WaitForLayerCompletedAsync(CancellationToken token)
    {
        var lease = _lease!;
        await lease.WaitLayerAsync(token).ConfigureAwait(false);
        try { await StageAsync("当前层PLC完成信号已确认", EquipmentGroups.RecipeOk, "1", () => Task.CompletedTask, token).ConfigureAwait(false); }
        catch (Exception ex)
        {
            // The PLC completion is already established: retain the count, but forbid any subsequent write.
            _stageFailure = "本层PLC完成已确认，但完成记录保存失败；禁止继续下发：" + ex.Message;
            Notice = _stageFailure;
        }
    }
    public Task CompleteRunAsync(CancellationToken token)
    {
        if (_stageFailure.Length > 0) throw new InvalidOperationException(_stageFailure);
        return _lease!.TransactionAsync(t => FlagAsync(EquipmentGroups.CoatOk, true, t, true), token);
    }

    private Task FlagAsync(string group, bool value, CancellationToken token, bool final = false) =>
        StageAsync(final ? "提交配方完成标志并回读" : "复位配方完成标志并回读", group, value ? "1" : "0",
            async () => {
                var target = _lease!.FlagValue(group, value);
                await _lease.WriteAsync(group, target, token).ConfigureAwait(false);
                await _lease.ConfirmAsync(group, target, token).ConfigureAwait(false);
            }, token, final);

    private Task TriggerRecipeLoadAsync(CancellationToken token) =>
        StageAsync("下发配方加载触发", EquipmentGroups.RecipeLoad, "1",
            () => _lease!.WriteAsync(EquipmentGroups.RecipeLoad,
                _lease.FlagValue(EquipmentGroups.RecipeLoad, true), token), token, successOutcome: "PLC已接受写入");

    private async Task StageAsync(string stage, string address, string value, Func<Task> action, CancellationToken token,
        bool final = false, string successOutcome = "PLC已确认")
    {
        var pending = new OperationLogRecord(DateTimeOffset.Now, _lease!.User, _runAudit!.Target, stage,
            false, false, "") { RecipeRunId = _runAudit.RecipeRunId, Outcome = "待处理" };
        await Task.Run(() => runtime.BeginWrite(pending), token).ConfigureAwait(false);
        try { token.ThrowIfCancellationRequested(); await action().ConfigureAwait(false); }
        catch (Exception ex)
        {
            var reason = _lease.Failure.Length > 0 ? _lease.Failure : ex.Message;
            if (ex is OperationCanceledException && !token.IsCancellationRequested) reason = "通讯或回读确认超过5秒";
            try { runtime.FinishWrite(pending with { Outcome = "结果未确认", FailureReason = reason }, null); }
            catch (Exception save) { reason += "；" + save.Message; }
            throw new InvalidOperationException(stage + "失败：" + reason + "；不自动重试，需人工核对", ex);
        }
        try { await Task.Run(() => runtime.FinishWrite(pending with { Outcome = successOutcome, IsSuccessful = true }, null)).ConfigureAwait(false); }
        catch (Exception ex)
        {
            var text = stage + "：PLC已确认，但本机最终记录保存失败：" + ex.Message;
            if (final) Notice = text; else throw new InvalidOperationException(text);
        }
    }
    public async Task EndRunAsync(RecipeRunResult result)
    {
        try
        {
            if (_runAudit is not null)
                await Task.Run(() => runtime.FinishWrite(_runAudit with { Outcome = result.IsCompleted ? "配方完成" : "停止自动下发·需人工核对",
                    IsSuccessful = result.IsCompleted, FailureReason = result.FailureReason }, null)).ConfigureAwait(false);
        }
        catch (Exception ex) { Notice += "；任务最终记录保存失败：" + ex.Message; }
        finally
        {
            if (_lease is not null) await _lease.DisposeAsync().ConfigureAwait(false);
            _lease = null; _runAudit = null; _blocks.Clear();
        }
    }
}
