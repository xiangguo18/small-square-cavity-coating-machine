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
    WaitingLayerComplete,
    Advancing,
    WaitingProcessComplete,
    Completed,
    Aborted
}

public sealed record RecipeRunRequest(
    RecipeDispatchMode Mode,
    IReadOnlyList<RecipeLayer> Layers,
    string SequenceSummary)
{
    public string RecipeName { get; init; } = "";
    public Guid RunId { get; init; } = Guid.NewGuid();
};

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
    public string RecipeName { get; init; } = "";
    public string Notice { get; init; } = "";
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
    public TimeSpan PreflightTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan LayerCompleteTimeout { get; init; } = TimeSpan.FromHours(24);

    public TimeSpan ProcessCompleteTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

public interface IRecipeExcelImporter
{
    RecipeImportResult Import(string filePath);
}

/// <summary>Business protocol: begin, verified layer/reset, wait for completion, commit CoatOK.</summary>
public interface IRecipePlcGateway
{
    bool IsAvailable { get; }
    bool IsSimulated { get; }
    string Notice => "";
    event EventHandler? AvailabilityChanged { add { } remove { } }
    Task<RecipeGatewayPreflightResult> PreflightAsync(CancellationToken cancellationToken);
    Task BeginRunAsync(RecipeRunRequest request, CancellationToken cancellationToken);
    Task SendLayerAsync(RecipeLayer layer, CancellationToken cancellationToken);
    Task WaitForLayerCompletedAsync(CancellationToken cancellationToken);
    Task CompleteRunAsync(CancellationToken cancellationToken);
    Task EndRunAsync(RecipeRunResult result) => Task.CompletedTask;
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

    bool ConfirmClearRecipe();

    RecipeLayer? ShowNewLayerDialog(int nextSequence);

    void ShowImportErrors(IReadOnlyList<RecipeImportError> errors);

    void ShowInformation(string message, string title);

    void ShowRunFinished(RecipeRunResult result);
}
