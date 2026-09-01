using Small_square_cavity_coating_machine.Models.Recipes;
using Small_square_cavity_coating_machine.Services.History;

namespace Small_square_cavity_coating_machine.Services.Recipes;

public sealed class RecipeDispatchService(IRecipePlcGateway gateway, IOperationLogRepository operationLogRepository,
    RecipeDispatchOptions? options = null, IProcessTrendRecorder? trendRecorder = null) : IRecipeDispatchService
{
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private readonly RecipeDispatchOptions _options = options ?? new();
    public async Task<RecipeRunResult> RunAsync(RecipeRunRequest request, IProgress<RecipeRunProgress> progress, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(operationLogRepository);
        if (!await _runLock.WaitAsync(0, token).ConfigureAwait(false))
            return RecipeRunResult.Aborted(request.Layers.Count, 0, "已有配方任务正在运行");
        request = request with { Layers = request.Layers.Select(l => l.Snapshot()).ToArray(),
            RecipeName = string.IsNullOrWhiteSpace(request.RecipeName) ? $"手工配方{DateTime.Now:yyyy-MM-dd HH:mm:ss}" : request.RecipeName };
        var completed = 0;
        var recording = false;
        var began = false;
        RecipeRunResult result;
        try
        {
            if (request.Layers.Count == 0) throw new InvalidOperationException("没有待执行配方层");
            Report(progress, request, RecipeRunState.Preflight, 0, 1);
            using var preflight = CancellationTokenSource.CreateLinkedTokenSource(token);
            preflight.CancelAfter(_options.PreflightTimeout);
            var check = await gateway.PreflightAsync(preflight.Token).ConfigureAwait(false);
            if (!check.IsReady) throw new InvalidOperationException(check.FailureReason);
            if (trendRecorder is not null)
            {
                recording = true;
                await ObserveRecordingAsync(() => trendRecorder.BeginAsync(request)).ConfigureAwait(false);
            }
            await gateway.BeginRunAsync(request, token).ConfigureAwait(false);
            began = true;
            for (var index = 0; index < request.Layers.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                Report(progress, request, RecipeRunState.Sending, completed, index + 1);
                await gateway.SendLayerAsync(request.Layers[index], token).ConfigureAwait(false);
                Report(progress, request, RecipeRunState.WaitingLayerComplete, completed, index + 1);
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
                wait.CancelAfter(_options.LayerCompleteTimeout);
                await gateway.WaitForLayerCompletedAsync(wait.Token).ConfigureAwait(false);
                completed++;
                Report(progress, request, RecipeRunState.Advancing, completed, index + 1);
            }
            Report(progress, request, RecipeRunState.WaitingProcessComplete, completed, completed);
            using var finish = CancellationTokenSource.CreateLinkedTokenSource(token);
            finish.CancelAfter(_options.ProcessCompleteTimeout);
            await gateway.CompleteRunAsync(finish.Token).ConfigureAwait(false);
            Report(progress, request, RecipeRunState.Completed, completed, completed);
            result = RecipeRunResult.Completed(completed);
        }
        catch (Exception ex)
        {
            var reason = ex is OperationCanceledException ? token.IsCancellationRequested ? "任务已取消或权限/连接丢失" : "等待PLC反馈超时" : ex.Message;
            if (completed == request.Layers.Count && completed > 0) reason = "全部层已完成，完成标志未确认；" + reason;
            result = RecipeRunResult.Aborted(request.Layers.Count, completed, reason + "；已停止自动下发，不表示设备已停机");
        }
        try { await gateway.EndRunAsync(result).ConfigureAwait(false); }
        finally
        {
            try
            {
                if (recording && trendRecorder is not null)
                    await ObserveRecordingAsync(() => trendRecorder.EndAsync(result, began, token.IsCancellationRequested)).ConfigureAwait(false);
            }
            finally { _runLock.Release(); }
        }
        return result with { RecipeName = request.RecipeName, Notice = gateway.Notice };
    }

    private static async Task ObserveRecordingAsync(Func<Task> action)
    {
        try { await action().ConfigureAwait(false); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("工艺曲线记录失败（不影响配方）：" + ex); }
    }

    private static void Report(
        IProgress<RecipeRunProgress> progress,
        RecipeRunRequest request,
        RecipeRunState state,
        int completedLayers,
        int currentPosition,
        int? currentSequence = null)
    {
        if (currentSequence is null
            && currentPosition > 0
            && currentPosition <= request.Layers.Count
            && state is not RecipeRunState.WaitingProcessComplete
            && state is not RecipeRunState.Completed)
        {
            currentSequence = request.Layers[currentPosition - 1].Sequence;
        }

        progress.Report(new RecipeRunProgress(
            request.Mode,
            state,
            request.Layers.Count,
            completedLayers,
            currentPosition,
            currentSequence,
            request.SequenceSummary));
    }


}
