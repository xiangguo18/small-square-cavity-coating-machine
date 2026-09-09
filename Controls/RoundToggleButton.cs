using System.Windows;

namespace Small_square_cavity_coating_machine.Controls;

/// <summary>
/// 圆形触点开关：只承载 IS_ON / 故障 / 联锁等可绑定反馈，
/// 命令由按钮发出，不在控件内部切换设备反馈。
/// 用于独立展示某个设备的开/关状态，例如“CG前级阀”。
/// </summary>
public class RoundToggleButton : DeviceButtonBase
{
    static RoundToggleButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(RoundToggleButton),
            new FrameworkPropertyMetadata(typeof(RoundToggleButton)));
    }

    public static readonly DependencyProperty IsOnProperty =
        DependencyProperty.Register(
            nameof(IsOn),
            typeof(bool),
            typeof(RoundToggleButton),
            new FrameworkPropertyMetadata(false));

    public bool IsOn
    {
        get => (bool)GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }
}
