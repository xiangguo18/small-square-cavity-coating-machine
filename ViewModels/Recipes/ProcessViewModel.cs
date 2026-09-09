using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Small_square_cavity_coating_machine.Models.History;
using Small_square_cavity_coating_machine.Models.Recipes;
using Small_square_cavity_coating_machine.Models.Security;
using Small_square_cavity_coating_machine.Services.History;
using Small_square_cavity_coating_machine.Services.Recipes;
using Small_square_cavity_coating_machine.Services.Security;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using Small_square_cavity_coating_machine.Services.Alarms;

namespace Small_square_cavity_coating_machine.ViewModels.Recipes;

public sealed partial class ProcessViewModel : ObservableObject, IDisposable
{
    private readonly IRecipeExcelImporter _excelImporter;
    private readonly IRecipeDispatchService _dispatchService;
    private readonly IRecipePlcGateway _plcGateway;
    private readonly IRecipeUserDialogService _dialogService;
    private readonly IOperationLogRepository _operationLogRepository;
    private readonly ApplicationStatusViewModel _applicationStatus;
    private readonly IAuthorizationService? _authorization;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly List<RecipeLayer> _selectedLayers = [];
    private CancellationTokenSource? _activeRunCancellation;
    private readonly IUiDispatcher? _dispatcher;
    private bool _disposed;

    public ProcessViewModel(
        IRecipeExcelImporter excelImporter,
        IRecipeDispatchService dispatchService,
        IRecipePlcGateway plcGateway,
        IRecipeUserDialogService dialogService,
        IOperationLogRepository operationLogRepository,
        ApplicationStatusViewModel applicationStatus,
        IAuthorizationService? authorization = null, IUiDispatcher? dispatcher = null)
    {
        _excelImporter = excelImporter;
        _dispatchService = dispatchService;
        _plcGateway = plcGateway;
        _dialogService = dialogService;
        _operationLogRepository = operationLogRepository;
        _applicationStatus = applicationStatus;
        _authorization = authorization;
        _dispatcher = dispatcher;
        _plcGateway.AvailabilityChanged += GatewayAvailabilityChanged;
        if (_authorization is not null)
        {
            _authorization.AccessChanged += Authorization_AccessChanged;
        }
        Layers.CollectionChanged += Layers_CollectionChanged;
    }

    public ObservableCollection<RecipeLayer> Layers { get; } = [];

    public bool CanOperate =>
        _authorization?.CanOperate(PermissionKey.ProcessRecipe) ?? true;

    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private string loadedFileName = string.Empty;

    public void SetSelectedLayers(IEnumerable<RecipeLayer> selectedLayers)
    {
        _selectedLayers.Clear();
        var selected = selectedLayers.ToHashSet();
        _selectedLayers.AddRange(Layers.Where(selected.Contains));
        SendSelectedCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanImportOrCreate))]
    private void ImportRecipe()
    {
        if (!EnsureCanOperate())
        {
            return;
        }

        var path = _dialogService.SelectRecipeFile();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var result = _excelImporter.Import(path);
        if (!result.IsSuccessful)
        {
            _dialogService.ShowImportErrors(result.Errors);
            Log("导入配方", Path.GetFileName(path), false, string.Join("；", result.Errors));
            return;
        }

        if (Layers.Count > 0 && !_dialogService.ConfirmReplaceExistingRecipe())
        {
            return;
        }

        ClearCurrentHighlight();
        Layers.Clear();
        foreach (var layer in result.Layers)
        {
            Layers.Add(layer);
        }

        _selectedLayers.Clear();
        LoadedFileName = Path.GetFileName(path);
        _applicationStatus.RecipeStatusText = string.Empty;
        Log("导入配方", $"{LoadedFileName}，共 {Layers.Count} 层", true, string.Empty);
        _dialogService.ShowInformation($"成功导入 {Layers.Count} 个配方层。", "导入配方");
        RefreshCommandStates();
    }

    [RelayCommand(CanExecute = nameof(CanClearRecipe))]
    private void ClearRecipe()
    {
        if (!EnsureCanOperate())
        {
            return;
        }

        if (!_dialogService.ConfirmClearRecipe())
        {
            return;
        }

        ClearCurrentHighlight();
        Layers.Clear();
        _selectedLayers.Clear();
        LoadedFileName = string.Empty;
        _applicationStatus.RecipeStatusText = string.Empty;
        Log("清除配方", "清除全部配方层", true, string.Empty);
        RefreshCommandStates();
    }

    [RelayCommand(CanExecute = nameof(CanImportOrCreate))]
    private void NewRecipeLayer()
    {
        if (!EnsureCanOperate())
        {
            return;
        }

        var nextSequence = Layers.Count == 0 ? 1 : Layers.Max(layer => layer.Sequence) + 1;
        var layer = _dialogService.ShowNewLayerDialog(nextSequence);
        if (layer is null)
        {
            return;
        }

        InsertLayerAndShiftFollowing(layer);
        _selectedLayers.Clear();
        LoadedFileName = string.Empty;
        _applicationStatus.RecipeStatusText = string.Empty;
        Log(
            "新建配方层",
            layer.Sequence == nextSequence
                ? $"追加序号 {layer.Sequence}"
                : $"插入序号 {layer.Sequence}，后续层已顺延",
            true,
            string.Empty);
        RefreshCommandStates();
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void StopAutoDispatch() => _activeRunCancellation?.Cancel();

    [RelayCommand(CanExecute = nameof(CanSendAll))]
    private Task SendAllAsync() => EnsureCanOperate()
        ? RunRecipeAsync(RecipeDispatchMode.All, Layers)
        : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanSendSelected))]
    private Task SendSelectedAsync() => EnsureCanOperate()
        ? RunRecipeAsync(RecipeDispatchMode.Selected, _selectedLayers)
        : Task.CompletedTask;

    private bool CanImportOrCreate() => !IsRunning;

    private bool CanClearRecipe() => !IsRunning && Layers.Count > 0;

    private bool CanSendAll() =>
        !IsRunning && CanOperate && _plcGateway.IsAvailable && Layers.Count > 0;

    private bool CanSendSelected()
        => !IsRunning && CanOperate && _plcGateway.IsAvailable && _selectedLayers.Count > 0;

    private bool EnsureCanOperate() =>
        _authorization?.TryAuthorize(PermissionKey.ProcessRecipe) ?? true;

    private async Task RunRecipeAsync(
        RecipeDispatchMode mode,
        IEnumerable<RecipeLayer> sourceLayers)
    {
        var snapshots = sourceLayers.Select(layer => layer.Snapshot()).ToArray();
        if (snapshots.Length == 0)
        {
            _dialogService.ShowInformation("请先选择需要下发的配方层。", "下发配方");
            return;
        }

        IsRunning = true;
        _applicationStatus.RecipeStatusText = string.Empty;
        ClearCurrentHighlight();
        RefreshCommandStates();

        var sequenceSummary = DescribeSequences(snapshots.Select(layer => layer.Sequence));
        var request = new RecipeRunRequest(mode, snapshots, sequenceSummary) {
            RecipeName = string.IsNullOrWhiteSpace(LoadedFileName) ? $"手工配方{DateTime.Now:yyyy-MM-dd HH:mm:ss}" : Path.GetFileNameWithoutExtension(LoadedFileName) };
        var progress = new Progress<RecipeRunProgress>(UpdateRunProgress);

        RecipeRunResult result;
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        _activeRunCancellation = runCancellation;
        try
        {
            result = await _dispatchService.RunAsync(request, progress, runCancellation.Token);
        }
        finally
        {
            if (ReferenceEquals(_activeRunCancellation, runCancellation))
            {
                _activeRunCancellation = null;
            }
            ClearCurrentHighlight();
            IsRunning = false;
            RefreshCommandStates();
        }

        // 按需求：只有操作员关闭完成/异常弹窗后，底部状态栏才显示终态。
        if (_disposed) return;
        _dialogService.ShowRunFinished(result);
        _applicationStatus.RecipeStatusText = BuildTerminalStatus(mode, sequenceSummary, result);
    }

    private void UpdateRunProgress(RecipeRunProgress progress)
    {
        if (_disposed || !IsRunning) return;
        foreach (var layer in Layers)
        {
            layer.IsCurrent = progress.State == RecipeRunState.WaitingLayerComplete
                              && progress.CurrentSequence == layer.Sequence;
        }

        var modeText = progress.Mode == RecipeDispatchMode.All
            ? "下发所有层"
            : "下发选中层";
        var selectedText = progress.Mode == RecipeDispatchMode.Selected
            ? $"（{progress.SequenceSummary}）"
            : string.Empty;

        if (progress.State == RecipeRunState.WaitingProcessComplete)
        {
            _applicationStatus.RecipeStatusText =
                $"{modeText}，共{progress.TotalLayers}层，已完成{progress.CompletedLayers}层{selectedText}，正在提交并确认整批完成标志";
            return;
        }

        if (progress.CurrentSequence is { } sequence)
        {
            _applicationStatus.RecipeStatusText =
                $"{modeText}，共{progress.TotalLayers}层，已完成{progress.CompletedLayers}层{selectedText}，{(progress.State == RecipeRunState.WaitingLayerComplete ? "正在镀的是" : "正在校验/下发")}第{progress.CurrentPosition}层（序号{sequence}）";
        }
    }

    private static string BuildTerminalStatus(
        RecipeDispatchMode mode,
        string sequenceSummary,
        RecipeRunResult result)
    {
        var modeText = mode == RecipeDispatchMode.All ? "下发所有层" : "下发选中层";
        var selectedText = mode == RecipeDispatchMode.Selected
            ? $"（{sequenceSummary}）"
            : string.Empty;

        return result.IsCompleted
            ? $"{modeText}，共{result.TotalLayers}层，已完成{result.CompletedLayers}层{selectedText}，镀膜结束"
            : $"{modeText}，共{result.TotalLayers}层，已完成{result.CompletedLayers}层{selectedText}，因{result.FailureReason}异常，停止自动下发（不代表设备停机）";
    }

    private static string DescribeSequences(IEnumerable<int> sequences)
    {
        var values = sequences.ToArray();
        if (values.Length == 1)
        {
            return $"序号{values[0]}";
        }

        var isContinuous = values
            .Select((value, index) => value == values[0] + index)
            .All(isExpected => isExpected);
        return isContinuous
            ? $"序号{values[0]}-{values[^1]}"
            : $"序号{string.Join("、", values)}";
    }

    partial void OnIsRunningChanged(bool value) => RefreshCommandStates();

    private void Layers_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshCommandStates();

    private void RefreshCommandStates()
    {
        StopAutoDispatchCommand.NotifyCanExecuteChanged();
        ImportRecipeCommand.NotifyCanExecuteChanged();
        ClearRecipeCommand.NotifyCanExecuteChanged();
        NewRecipeLayerCommand.NotifyCanExecuteChanged();
        SendAllCommand.NotifyCanExecuteChanged();
        SendSelectedCommand.NotifyCanExecuteChanged();
    }

    private void Authorization_AccessChanged(object? sender, EventArgs e)
    {
        if (!CanOperate)
        {
            _activeRunCancellation?.Cancel();
        }

        OnPropertyChanged(nameof(CanOperate));
        RefreshCommandStates();
    }

    private void ClearCurrentHighlight()
    {
        foreach (var layer in Layers)
        {
            layer.IsCurrent = false;
        }
    }

    private void InsertLayerAndShiftFollowing(RecipeLayer newLayer)
    {
        for (var index = Layers.Count - 1; index >= 0; index--)
        {
            var existing = Layers[index];
            if (existing.Sequence >= newLayer.Sequence)
            {
                Layers[index] = existing.CopyWithSequence(existing.Sequence + 1);
            }
        }

        var insertionIndex = 0;
        while (insertionIndex < Layers.Count
               && Layers[insertionIndex].Sequence < newLayer.Sequence)
        {
            insertionIndex++;
        }

        Layers.Insert(insertionIndex, newLayer);
    }

    private void Log(string target, string action, bool successful, string failureReason)
    {
        _operationLogRepository.Add(new OperationLogRecord(
            DateTimeOffset.Now,
            _authorization?.CurrentUserName ?? "本地操作员",
            target,
            action,
            successful,
            !successful,
            failureReason));
    }

    private void GatewayAvailabilityChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        if (_dispatcher is not null) _dispatcher.Post(() => { if (!_disposed) RefreshCommandStates(); });
        else RefreshCommandStates();
    }

    public void Dispose()
    {
        _disposed = true;
        _plcGateway.AvailabilityChanged -= GatewayAvailabilityChanged;
        if (_authorization is not null)
        {
            _authorization.AccessChanged -= Authorization_AccessChanged;
        }
        Layers.CollectionChanged -= Layers_CollectionChanged;
        _activeRunCancellation?.Cancel();
        _shutdown.Cancel();
        _shutdown.Dispose();
    }
}
