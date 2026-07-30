using CommunityToolkit.Mvvm.ComponentModel;
using Small_square_cavity_coating_machine.Models;

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

    private ApplicationStatusViewModel()
    {
    }
}
