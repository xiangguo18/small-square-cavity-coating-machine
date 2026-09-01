using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Small_square_cavity_coating_machine.Models.Alarms;
using Small_square_cavity_coating_machine.Models.Equipment;
using Small_square_cavity_coating_machine.Services.Alarms;
using Small_square_cavity_coating_machine.Services.Equipment;
using System.Collections.ObjectModel;

namespace Small_square_cavity_coating_machine.ViewModels.Equipment;

public sealed partial class IoRowViewModel(IoDefinition definition) : ObservableObject
{
    public IoDefinition Definition { get; } = definition;
    public string Description => Definition.Description is "空" or "Space" ? "预留点" : Definition.Description;
    [ObservableProperty] private string valueText = "—";
    [ObservableProperty] private string lightColor = "#CBD5E1";
    [ObservableProperty] private string qualityText = "未确认";
    [ObservableProperty] private string updatedAtText = "—";
    public void Update(EquipmentPoint? point)
    {
        var known = point?.Quality == AlarmQuality.Good && point.Value is bool;
        ValueText = known ? (bool)point!.Value! ? "1" : "0" : "—";
        LightColor = known ? (bool)point!.Value! ? "#20B65A" : "#FFFFFF" : "#CBD5E1";
        QualityText = known ? "有效" : "未知/过期";
        UpdatedAtText = point?.ObservedAt > DateTimeOffset.MinValue ? point.ObservedAt.ToLocalTime().ToString("HH:mm:ss.fff") : "—";
    }
}

public sealed partial class IoStatusViewModel : ObservableObject, IDisposable
{
    private readonly IOpcUaEquipmentClient _client;
    private readonly IUiDispatcher _dispatcher;
    private readonly IoRowViewModel[] _all;
    private bool _disposed;
    private int _pending;
    public IoStatusViewModel(IReadOnlyList<IoDefinition> definitions, IOpcUaEquipmentClient client, IUiDispatcher dispatcher)
    {
        _client = client; _dispatcher = dispatcher;
        _all = definitions.Select(d => new IoRowViewModel(d)).ToArray();
        client.SnapshotChanged += OnSnapshot;
        Filter("全部"); Refresh();
    }
    public ObservableCollection<IoRowViewModel> Rows { get; } = [];
    [ObservableProperty] private string filterText = "全部";
    [ObservableProperty] private string statusText = "IO状态未确认";
    [RelayCommand] private void Filter(string? filter)
    {
        FilterText = filter ?? "全部";
        Rows.Clear();
        foreach (var row in _all.Where(r => FilterText == "全部" || r.Definition.Type == FilterText)) Rows.Add(row);
    }
    private void OnSnapshot(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _pending, 1) != 0) return;
        _dispatcher.Post(() => { Interlocked.Exchange(ref _pending, 0); if (!_disposed) Refresh(); });
    }
    private void Refresh()
    {
        var snapshot = _client.Snapshot();
        var group = snapshot.Groups[EquipmentGroups.Io];
        foreach (var row in _all) row.Update(group.Points.GetValueOrDefault(row.Definition.Address));
        StatusText = (snapshot.IsSimulated ? "[模拟] " : "") + group.Message + "｜全部输入、输出点仅监视，不可操作";
    }
    public void Dispose() { _disposed = true; _client.SnapshotChanged -= OnSnapshot; }
}
