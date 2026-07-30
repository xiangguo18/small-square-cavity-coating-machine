using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Small_square_cavity_coating_machine.Models.History;
using Small_square_cavity_coating_machine.Models.Recipes;
using Small_square_cavity_coating_machine.Services.History;
using Small_square_cavity_coating_machine.Services.Recipes;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;

namespace Small_square_cavity_coating_machine.ViewModels.Recipes;

public sealed partial class ProcessViewModel : ObservableObject, IDisposable
{
    private readonly IRecipeExcelImporter _excelImporter;
    private readonly IRecipeDispatchService _dispatchService;
    private readonly IRecipePlcGateway _plcGateway;
    private readonly IRecipeUserDialogService _dialogService;
    private readonly IOperationLogRepository _operationLogRepository;
    private readonly ApplicationStatusViewModel _applicationStatus;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly List<RecipeLayer> _selectedLayers = [];

    public ProcessViewModel(
        IRecipeExcelImporter excelImporter,
        IRecipeDispatchService dispatchService,
        IRecipePlcGateway plcGateway,
        IRecipeUserDialogService dialogService,
        IOperationLogRepository operationLogRepository,
        ApplicationStatusViewModel applicationStatus)
    {
        _excelImporter = excelImporter;
        _dispatchService = dispatchService;
        _plcGateway = plcGateway;
        _dialogService = dialogService;
        _operationLogRepository = operationLogRepository;
        _applicationStatus = applicationStatus;
        Layers.CollectionChanged += Layers_CollectionChanged;
    }

    public ObservableCollection<RecipeLayer> Layers { get; } = [];

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

    [RelayCommand(CanExecute = nameof(CanImportOrCreate))]
    private void NewRecipeLayer()
    {
        var nextSequence = Layers.Count == 0 ? 1 : Layers.Max(layer => layer.Sequence) + 1;
        var layer = _dialogService.ShowNewLayerDialog(nextSequence);
        if (layer is null)
        {
            return;
        }

        Layers.Add(layer);
        LoadedFileName = string.Empty;
        _applicationStatus.RecipeStatusText = string.Empty;
        Log("新建配方层", $"序号 {layer.Sequence}", true, string.Empty);
        RefreshCommandStates();
    }

    [RelayCommand(CanExecute = nameof(CanSendAll))]
    private Task SendAllAsync()
        => RunRecipeAsync(RecipeDispatchMode.All, Layers);

    [RelayCommand(CanExecute = nameof(CanSendSelected))]
    private Task SendSelectedAsync()
        => RunRecipeAsync(RecipeDispatchMode.Selected, _selectedLayers);

    private bool CanImportOrCreate() => !IsRunning;

    private bool CanSendAll() => !IsRunning && _plcGateway.IsAvailable && Layers.Count > 0;

    private bool CanSendSelected()
        => !IsRunning && _plcGateway.IsAvailable && _selectedLayers.Count > 0;

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
        var request = new RecipeRunRequest(mode, snapshots, sequenceSummary);
        var progress = new Progress<RecipeRunProgress>(UpdateRunProgress);

        RecipeRunResult result;
        try
        {
            result = await _dispatchService.RunAsync(request, progress, _shutdown.Token);
        }
        finally
        {
            ClearCurrentHighlight();
            IsRunning = false;
            RefreshCommandStates();
        }

        // 按需求：只有操作员关闭完成/异常弹窗后，底部状态栏才显示终态。
        _dialogService.ShowRunFinished(result);
        _applicationStatus.RecipeStatusText = BuildTerminalStatus(mode, sequenceSummary, result);
    }

    private void UpdateRunProgress(RecipeRunProgress progress)
    {
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
                $"{modeText}，共{progress.TotalLayers}层，已完成{progress.CompletedLayers}层{selectedText}，等待PLC镀膜完成";
            return;
        }

        if (progress.CurrentSequence is { } sequence)
        {
            _applicationStatus.RecipeStatusText =
                $"{modeText}，共{progress.TotalLayers}层，已完成{progress.CompletedLayers}层{selectedText}，正在镀的是第{progress.CurrentPosition}层（序号{sequence}）";
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
            : $"{modeText}，共{result.TotalLayers}层，已完成{result.CompletedLayers}层{selectedText}，因{result.FailureReason}异常，镀膜中止";
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
        ImportRecipeCommand.NotifyCanExecuteChanged();
        NewRecipeLayerCommand.NotifyCanExecuteChanged();
        SendAllCommand.NotifyCanExecuteChanged();
        SendSelectedCommand.NotifyCanExecuteChanged();
    }

    private void ClearCurrentHighlight()
    {
        foreach (var layer in Layers)
        {
            layer.IsCurrent = false;
        }
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
            _plcGateway.IsSimulated));
    }

    public void Dispose()
    {
        Layers.CollectionChanged -= Layers_CollectionChanged;
        _shutdown.Cancel();
        _shutdown.Dispose();
    }
}
