using CommunityToolkit.Mvvm.ComponentModel;
using Small_square_cavity_coating_machine.Models;
using Small_square_cavity_coating_machine.ViewModels.History;

namespace Small_square_cavity_coating_machine.ViewModels;

/// <summary>
/// 所有子页面共享的应用状态。
/// PLC 通信服务接入后应通过此实例发布连接状态。
/// </summary>
public partial class ApplicationStatusViewModel : ObservableObject
{
    public static ApplicationStatusViewModel Instance { get; } = new();

    [ObservableProperty]
    private PlcConnectionState plcConnectionState = PlcConnectionState.Disconnected;

    [ObservableProperty]
    private string recipeStatusText = string.Empty;

    [ObservableProperty]
    private string historyRecordingError = string.Empty;

    [ObservableProperty]
    private AlarmStatusViewModel? alarmStatus;

    [ObservableProperty]
    private string plcConnectionText = "PLC未连接";

    [ObservableProperty]
    private PlcConnectionStatusViewModel? plcConnection;

    [ObservableProperty]
    private bool maintenanceBypassActive;

    public string MaintenanceWarningText => MaintenanceBypassActive ? "！维修模式，请谨慎控制" : string.Empty;

    partial void OnMaintenanceBypassActiveChanged(bool value) => OnPropertyChanged(nameof(MaintenanceWarningText));

    private ApplicationStatusViewModel()
    {
    }
}
