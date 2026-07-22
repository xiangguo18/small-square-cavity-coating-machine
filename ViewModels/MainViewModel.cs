using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Small_square_cavity_coating_machine.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        // 页面标题
        [ObservableProperty]
        private string title = "小方腔镀膜机控制系统";

        // 设备状态
        [ObservableProperty]
        private string deviceStatus = "设备未连接";

        // 连接按钮是否可用
        [ObservableProperty]
        private bool isConnected;

        /// <summary>
        /// 连接设备命令
        /// </summary>
        [RelayCommand]
        private void ConnectDevice()
        {
            IsConnected = true;
            DeviceStatus = "设备已连接";
        }

        /// <summary>
        /// 断开设备命令
        /// </summary>
        [RelayCommand]
        private void DisconnectDevice()
        {
            IsConnected = false;
            DeviceStatus = "设备未连接";
        }
    }
}