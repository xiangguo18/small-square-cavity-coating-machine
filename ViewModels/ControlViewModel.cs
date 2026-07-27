using CommunityToolkit.Mvvm.ComponentModel;

namespace Small_square_cavity_coating_machine.ViewModels;

/// <summary>
/// 系统状态页的离线预览数据。
/// 后续接入PLC时可由实时状态服务更新这些属性。
/// </summary>
public partial class ControlViewModel : ObservableObject
{
    [ObservableProperty]
    private bool mainValveIsOpen = true;

    [ObservableProperty]
    private bool mainValveInterlockReleased = true;

    [ObservableProperty]
    private bool branchValveIsOpen;

    [ObservableProperty]
    private bool branchValveInterlockReleased;

    [ObservableProperty]
    private bool turboPumpIsRunning;

    [ObservableProperty]
    private bool turboPumpInterlockReleased = true;

    [ObservableProperty]
    private bool dryPumpIsRunning;

    [ObservableProperty]
    private bool dryPumpInterlockReleased = true;

    [ObservableProperty]
    private bool vacuumPathIsFlowing = true;

    [ObservableProperty]
    private bool branchPathIsFlowing = true;

    [ObservableProperty]
    private double argonCurrentFlow = 0.6;

    [ObservableProperty]
    private double argonSetpointFlow = 1.0;

    [ObservableProperty]
    private double chamberVacuum = 13.6;
}
