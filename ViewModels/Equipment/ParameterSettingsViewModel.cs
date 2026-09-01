using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Small_square_cavity_coating_machine.Models.Alarms;
using Small_square_cavity_coating_machine.Models.Equipment;
using Small_square_cavity_coating_machine.Models.Security;
using Small_square_cavity_coating_machine.Services.Alarms;
using Small_square_cavity_coating_machine.Services.Equipment;
using Small_square_cavity_coating_machine.Services.Security;
using System.Collections.ObjectModel;

namespace Small_square_cavity_coating_machine.ViewModels.Equipment;

public sealed partial class ParameterRowViewModel : ObservableObject
{
    private readonly IOpcUaEquipmentClient _client;
    private readonly IUiDispatcher _dispatcher;
    private EquipmentPoint? _point;
    private object? _baseline;
    private long _baselineEpoch;
    private bool _syncing, _editing, _dirty;
    public ParameterRowViewModel(ParameterDefinition definition, IOpcUaEquipmentClient client, IUiDispatcher dispatcher)
    { Definition = definition; _client = client; _dispatcher = dispatcher; }
    public ParameterDefinition Definition { get; }
    public string Name => Definition.DisplayName;
    public string RangeText => $"{Definition.MinValue:G} ～ {Definition.MaxValue:G}";
    public string Details => $"{Definition.Name}\n{Definition.Address}\n位置：{Definition.Location}｜分类：{Definition.Type}\n默认值：{Definition.DefaultValue:G} {Definition.Unit}";
    [ObservableProperty] private string currentText = "—";
    [ObservableProperty] private string draftText = "";
    [ObservableProperty] private string referenceText = "";
    [ObservableProperty] private string statusText = "等待PLC数据";
    [ObservableProperty] private string availabilityText = "未连接";
    [ObservableProperty] private bool canEdit;
    [ObservableProperty] private bool hasDraft;
    public bool IsEditorReadOnly => !CanEdit;
    partial void OnCanEditChanged(bool value) { OnPropertyChanged(nameof(IsEditorReadOnly)); SubmitCommand.NotifyCanExecuteChanged(); }
    partial void OnDraftTextChanged(string value)
    {
        if (_syncing) return;
        if (!_dirty && _baseline is null && _point?.Quality == AlarmQuality.Good)
        { _baseline = _point.Value; _baselineEpoch = _point.Epoch; }
        _dirty = true; HasDraft = true;
    }
    private void SetDraft(string text) { _syncing = true; DraftText = text; _syncing = false; }

    [RelayCommand] private void BeginEdit()
    {
        _editing = true;
        if (!_dirty) { _baseline = _point?.Value; _baselineEpoch = _point?.Epoch ?? -1; SetDraft(_point?.Quality == AlarmQuality.Good ? ParameterValueCodec.Format(_point.Value) : ""); }
    }
    [RelayCommand] private void EndEdit() => _editing = false;
    [RelayCommand] private void CancelEdit()
    {
        _dirty = false; HasDraft = false;
        _baseline = _point?.Value; _baselineEpoch = _point?.Epoch ?? -1;
        SetDraft(_point?.Quality == AlarmQuality.Good ? ParameterValueCodec.Format(_point.Value) : "");
        StatusText = "已取消编辑，未写入";
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task SubmitAsync()
    {
        if (_baseline is null) { StatusText = "缺少有效PLC原值，请重新读取"; return; }
        StatusText = "正在提交当前参数…";
        ParameterWriteResult result;
        try { result = await _client.WriteParameterAsync(new(Definition.Address, DraftText, _baseline, _baselineEpoch)).ConfigureAwait(false); }
        catch (Exception ex) { _dispatcher.Post(() => StatusText = "提交未完成：" + ex.Message); return; }
        _dispatcher.Post(() =>
        {
            StatusText = result.Message;
            if (result.Outcome is ParameterWriteOutcome.Confirmed or ParameterWriteOutcome.Unchanged)
            {
                _dirty = false; HasDraft = false;
                _baseline = result.ConfirmedValue;
                SetDraft(ParameterValueCodec.Format(result.ConfirmedValue));
            }
        });
    }

    public void Update(EquipmentPoint? point, EquipmentSnapshot snapshot, bool authorized, ParameterCache? cache)
    {
        _point = point;
        if (snapshot.IsWriting && snapshot.WriteAddress == Definition.Address) StatusText = snapshot.WriteStage;
        var valid = snapshot.IsConnected && point?.Quality == AlarmQuality.Good && ParameterValueCodec.IsValid(point.Value);
        CurrentText = valid ? point!.Value switch { float f => f.ToString("G7"), double d => d.ToString("G12"), _ => ParameterValueCodec.Format(point!.Value) } : "—";
        if (valid && StatusText == "等待PLC数据") StatusText = "尚未提交";
        CanEdit = authorized && valid && point!.CanWrite && !snapshot.IsWriting;
        AvailabilityText = !authorized ? "需参数设置权限" : !valid ? "PLC数据未知/过期"
            : !point!.CanWrite ? "PLC节点只读" : snapshot.IsWriting ? "参数写入处理中" : "可编辑，Enter提交";
        ReferenceText = cache is not null ? $"本机已确认值：{cache.Value}（{cache.ConfirmedAt:MM-dd HH:mm}）"
            : $"数据库参考值：{Definition.Value:G7}";
        if (!_dirty && !_editing)
        {
            _baseline = valid ? point!.Value : null;
            _baselineEpoch = point?.Epoch ?? -1;
            SetDraft(valid ? ParameterValueCodec.Format(point!.Value) : "");
        }
    }
}

public sealed partial class ParameterSettingsViewModel : ObservableObject, IDisposable
{
    private readonly IOpcUaEquipmentClient _client;
    private readonly IEquipmentRuntimeRepository _runtime;
    private readonly IAuthorizationService _authorization;
    private readonly IUiDispatcher _dispatcher;
    private readonly IEquipmentControlService? _controlService;
    private bool _disposed;
    private int _pending;
    public ParameterSettingsViewModel(IReadOnlyList<ParameterDefinition> definitions, IOpcUaEquipmentClient client,
        IEquipmentRuntimeRepository runtime, IAuthorizationService authorization, IUiDispatcher dispatcher,
        IEquipmentControlService? controlService = null)
    {
        _client = client; _runtime = runtime; _authorization = authorization; _dispatcher = dispatcher;
        _controlService = controlService;
        Rows = new(definitions.Select(d => new ParameterRowViewModel(d, client, dispatcher)));
        Diagnostics = new((controlService?.Definitions.Data.Values ?? [])
            .Where(d => d.Id is >= 13 and <= 20)
            .OrderBy(d => d.Id).Select(d => new ControlDiagnosticRowViewModel(d)));
        client.SnapshotChanged += OnChanged; authorization.AccessChanged += OnChanged;
        Refresh();
    }
    public ObservableCollection<ParameterRowViewModel> Rows { get; }
    public ObservableCollection<ControlDiagnosticRowViewModel> Diagnostics { get; }
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private string turboCommandStatus = "等待PLC连接";

    [RelayCommand]
    private async Task TurboLowSpeedAsync()
    {
        if (_controlService is null) { TurboCommandStatus = "控制点定义未加载"; return; }
        var result = await _controlService.ExecutePartCommandAsync(4, "分子泵", "低速").ConfigureAwait(false);
        _dispatcher.Post(() => TurboCommandStatus = result.Message);
    }

    [RelayCommand]
    private async Task TurboResetAsync()
    {
        if (_controlService is null) { TurboCommandStatus = "控制点定义未加载"; return; }
        var result = await _controlService.ExecutePartCommandAsync(5, "分子泵", "复位").ConfigureAwait(false);
        _dispatcher.Post(() => TurboCommandStatus = result.Message);
    }
    private void OnChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _pending, 1) != 0) return;
        _dispatcher.Post(() => { Interlocked.Exchange(ref _pending, 0); if (!_disposed) Refresh(); });
    }
    private void Refresh()
    {
        var snapshot = _client.Snapshot();
        var group = snapshot.Groups[EquipmentGroups.Parameter];
        var cache = _runtime.LoadCache(snapshot.Endpoint).ToDictionary(c => c.Address);
        foreach (var row in Rows) row.Update(group.Points.GetValueOrDefault(row.Definition.Address), snapshot,
            _authorization.CanOperate(PermissionKey.ParameterSettings), cache.GetValueOrDefault(row.Definition.Address));
        foreach (var row in Diagnostics)
        {
            var point = snapshot.Groups.GetValueOrDefault(EquipmentGroups.PartData)?.Points.GetValueOrDefault(row.Definition.Address);
            row.Update(point, snapshot.IsConnected);
        }
        StatusText = (snapshot.IsSimulated ? "[模拟] " : "") + group.Message + (string.IsNullOrWhiteSpace(_runtime.StorageError) ? "" : "｜" + _runtime.StorageError);
    }
    public void Dispose()
    {
        _disposed = true; _client.SnapshotChanged -= OnChanged; _authorization.AccessChanged -= OnChanged;
    }
}

public sealed partial class ControlDiagnosticRowViewModel(PartDataDefinition definition) : ObservableObject
{
    public PartDataDefinition Definition { get; } = definition;
    public string Name => Definition.DisplayName;
    public string Address => Definition.Address;
    public string Unit => Definition.Unit;
    [ObservableProperty] private string valueText = "—";
    [ObservableProperty] private string qualityText = "未连接";

    public void Update(EquipmentPoint? point, bool connected)
    {
        var valid = connected && point is { Quality: AlarmQuality.Good } && ParameterValueCodec.IsValid(point.Value);
        ValueText = valid ? ParameterValueCodec.Format(point!.Value) : "—";
        QualityText = valid ? "有效" : connected ? "质量无效" : "未连接";
    }
}
