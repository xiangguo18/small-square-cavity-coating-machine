using Small_square_cavity_coating_machine.Models.History;
using Small_square_cavity_coating_machine.Models.Recipes;
using Small_square_cavity_coating_machine.Services.History;

namespace Small_square_cavity_coating_machine.Services.Recipes;

public sealed class RecipeDispatchService : IRecipeDispatchService
{
    private readonly IRecipePlcGateway _gateway;
    private readonly IOperationLogRepository _operationLogRepository;
    private readonly RecipeDispatchOptions _options;
    private readonly SemaphoreSlim _runLock = new(1, 1);

    public RecipeDispatchService(
        IRecipePlcGateway gateway,
        IOperationLogRepository operationLogRepository,
        RecipeDispatchOptions? options = null)
    {
        _gateway = gateway;
        _operationLogRepository = operationLogRepository;
        _options = options ?? new RecipeDispatchOptions();
    }

    public async Task<RecipeRunResult> RunAsync(
        RecipeRunRequest request,
        IProgress<RecipeRunProgress> progress,
        CancellationToken cancellationToken)
    {
        if (!await _runLock.WaitAsync(0, cancellationToken))
        {
            return RecipeRunResult.Aborted(request.Layers.Count, 0, "已有配方任务正在运行");
        }

        var completedLayers = 0;
        try
        {
            Report(progress, request, RecipeRunState.Preflight, completedLayers, 1);
            var preflight = await _gateway
                .PreflightAsync(cancellationToken)
                .WaitAsync(_options.PreflightTimeout, cancellationToken);

            if (!preflight.IsReady)
            {
                Log("启动配方", string.Empty, false, preflight.FailureReason);
                return RecipeRunResult.Aborted(
                    request.Layers.Count,
                    completedLayers,
                    preflight.FailureReason);
            }

            Log(
                "启动配方",
                $"{ModeText(request.Mode)}，共 {request.Layers.Count} 层",
                true,
                string.Empty);

            for (var index = 0; index < request.Layers.Count; index++)
            {
                var layer = request.Layers[index];
                Report(progress, request, RecipeRunState.ReadyToSend, completedLayers, index + 1);
                Report(progress, request, RecipeRunState.Sending, completedLayers, index + 1);

                await _gateway.SendLayerAsync(layer, cancellationToken);
                Log(
                    $"配方层 {layer.Sequence}",
                    $"发送参数，样品台转速 {layer.StageSpeedRpm:0.###} rpm",
                    true,
                    string.Empty);

                Report(progress, request, RecipeRunState.AwaitAccepted, completedLayers, index + 1);
                await _gateway
                    .WaitForLayerAcceptedAsync(cancellationToken)
                    .WaitAsync(_options.LayerAcceptedTimeout, cancellationToken);

                Report(progress, request, RecipeRunState.WaitingLayerComplete, completedLayers, index + 1);
                await _gateway
                    .WaitForLayerCompletedAsync(cancellationToken)
                    .WaitAsync(_options.LayerCompleteTimeout, cancellationToken);

                completedLayers++;
                Log($"配方层 {layer.Sequence}", "镀膜完成", true, string.Empty);
                Report(progress, request, RecipeRunState.Advancing, completedLayers, index + 1);
            }

            Report(
                progress,
                request,
                RecipeRunState.WaitingProcessComplete,
                completedLayers,
                request.Layers.Count,
                null);
            await _gateway
                .WaitForProcessCompletedAsync(cancellationToken)
                .WaitAsync(_options.ProcessCompleteTimeout, cancellationToken);

            Log("自动配方", "镀膜结束", true, string.Empty);
            Report(
                progress,
                request,
                RecipeRunState.Completed,
                completedLayers,
                request.Layers.Count,
                null);
            return RecipeRunResult.Completed(request.Layers.Count);
        }
        catch (TimeoutException)
        {
            const string reason = "等待 PLC 反馈超时";
            Log("自动配方", "镀膜中止", false, reason);
            return RecipeRunResult.Aborted(request.Layers.Count, completedLayers, reason);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            const string reason = "任务已取消";
            Log("自动配方", "镀膜中止", false, reason);
            return RecipeRunResult.Aborted(request.Layers.Count, completedLayers, reason);
        }
        catch (Exception exception)
        {
            var reason = string.IsNullOrWhiteSpace(exception.Message)
                ? "PLC 配方调度异常"
                : exception.Message;
            Log("自动配方", "镀膜中止", false, reason);
            return RecipeRunResult.Aborted(request.Layers.Count, completedLayers, reason);
        }
        finally
        {
            _runLock.Release();
        }
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

    private void Log(string target, string action, bool successful, string failureReason)
    {
        _operationLogRepository.Add(new OperationLogRecord(
            DateTimeOffset.Now,
            "本地操作员",
            target,
            action,
            string.Empty,
            successful,
            !successful,
            failureReason,
            _gateway.IsSimulated));
    }

    private static string ModeText(RecipeDispatchMode mode)
        => mode == RecipeDispatchMode.All ? "下发所有层" : "下发选中层";
}

public sealed class SimulatedRecipePlcGateway : IRecipePlcGateway
{
    private RecipeLayer? _currentLayer;

    public bool IsAvailable => true;

    public bool IsSimulated => true;

    public RecipeLayer? LastSentLayer => _currentLayer?.Snapshot();

    public async Task<RecipeGatewayPreflightResult> PreflightAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(150, cancellationToken);
        return RecipeGatewayPreflightResult.Ready;
    }

    public Task SendLayerAsync(RecipeLayer layer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 保留有符号转速，模拟真实 PLC 参数块写入。
        _currentLayer = layer.Snapshot();
        return Task.CompletedTask;
    }

    public Task WaitForLayerAcceptedAsync(CancellationToken cancellationToken)
        => Task.Delay(250, cancellationToken);

    public Task WaitForLayerCompletedAsync(CancellationToken cancellationToken)
        => Task.Delay(900, cancellationToken);

    public Task WaitForProcessCompletedAsync(CancellationToken cancellationToken)
        => Task.Delay(350, cancellationToken);
}

public sealed class UnavailableRecipePlcGateway : IRecipePlcGateway
{
    public bool IsAvailable => false;

    public bool IsSimulated => false;

    public Task<RecipeGatewayPreflightResult> PreflightAsync(CancellationToken cancellationToken)
        => Task.FromResult(RecipeGatewayPreflightResult.Blocked("真实 PLC 配方点位尚未配置"));

    public Task SendLayerAsync(RecipeLayer layer, CancellationToken cancellationToken)
        => Task.FromException(new InvalidOperationException("真实 PLC 配方点位尚未配置"));

    public Task WaitForLayerAcceptedAsync(CancellationToken cancellationToken)
        => Task.FromException(new InvalidOperationException("真实 PLC 配方点位尚未配置"));

    public Task WaitForLayerCompletedAsync(CancellationToken cancellationToken)
        => Task.FromException(new InvalidOperationException("真实 PLC 配方点位尚未配置"));

    public Task WaitForProcessCompletedAsync(CancellationToken cancellationToken)
        => Task.FromException(new InvalidOperationException("真实 PLC 配方点位尚未配置"));
}
