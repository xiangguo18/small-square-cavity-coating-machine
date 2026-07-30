using Small_square_cavity_coating_machine.Models.Recipes;

namespace Small_square_cavity_coating_machine.Services.Recipes;

public enum RecipeDispatchMode
{
    All,
    Selected
}

public enum RecipeRunState
{
    Idle,
    Preflight,
    ReadyToSend,
    Sending,
    AwaitAccepted,
    WaitingLayerComplete,
    Advancing,
    WaitingProcessComplete,
    Completed,
    Aborted
}

public sealed record RecipeRunRequest(
    RecipeDispatchMode Mode,
    IReadOnlyList<RecipeLayer> Layers,
    string SequenceSummary);

public sealed record RecipeRunProgress(
    RecipeDispatchMode Mode,
    RecipeRunState State,
    int TotalLayers,
    int CompletedLayers,
    int CurrentPosition,
    int? CurrentSequence,
    string SequenceSummary);

public sealed record RecipeRunResult(
    bool IsCompleted,
    int TotalLayers,
    int CompletedLayers,
    string FailureReason)
{
    public static RecipeRunResult Completed(int totalLayers)
        => new(true, totalLayers, totalLayers, string.Empty);

    public static RecipeRunResult Aborted(int totalLayers, int completedLayers, string reason)
        => new(false, totalLayers, completedLayers, reason);
}

public sealed record RecipeGatewayPreflightResult(bool IsReady, string FailureReason)
{
    public static RecipeGatewayPreflightResult Ready { get; } = new(true, string.Empty);

    public static RecipeGatewayPreflightResult Blocked(string reason) => new(false, reason);
}

public sealed class RecipeDispatchOptions
{
    public TimeSpan PreflightTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan LayerAcceptedTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan LayerCompleteTimeout { get; init; } = TimeSpan.FromHours(24);

    public TimeSpan ProcessCompleteTimeout { get; init; } = TimeSpan.FromMinutes(1);
}

public interface IRecipeExcelImporter
{
    RecipeImportResult Import(string filePath);
}

/// <summary>
/// 隔离真实 OPC UA 点位和配方调度逻辑。真实实现必须在方法内部完成
/// 权限/互锁检查、参数块写入、请求脉冲以及反馈边沿去重。
/// </summary>
public interface IRecipePlcGateway
{
    bool IsAvailable { get; }

    bool IsSimulated { get; }

    Task<RecipeGatewayPreflightResult> PreflightAsync(CancellationToken cancellationToken);

    Task SendLayerAsync(RecipeLayer layer, CancellationToken cancellationToken);

    Task WaitForLayerAcceptedAsync(CancellationToken cancellationToken);

    Task WaitForLayerCompletedAsync(CancellationToken cancellationToken);

    Task WaitForProcessCompletedAsync(CancellationToken cancellationToken);
}

public interface IRecipeDispatchService
{
    Task<RecipeRunResult> RunAsync(
        RecipeRunRequest request,
        IProgress<RecipeRunProgress> progress,
        CancellationToken cancellationToken);
}

public interface IRecipeUserDialogService
{
    string? SelectRecipeFile();

    bool ConfirmReplaceExistingRecipe();

    RecipeLayer? ShowNewLayerDialog(int nextSequence);

    void ShowImportErrors(IReadOnlyList<RecipeImportError> errors);

    void ShowInformation(string message, string title);

    void ShowRunFinished(RecipeRunResult result);
}
