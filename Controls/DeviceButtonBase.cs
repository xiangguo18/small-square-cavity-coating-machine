using System.Windows;
using System.Windows.Controls;

namespace Small_square_cavity_coating_machine.Controls;

/// <summary>
/// 可操作设备图形的公共基类。
/// 只承载可绑定状态，不在控件内部切换设备反馈。
/// </summary>
public abstract class DeviceButtonBase : Button
{
    public static readonly DependencyProperty DeviceNameProperty =
        DependencyProperty.Register(
            nameof(DeviceName),
            typeof(string),
            typeof(DeviceButtonBase),
            new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsInterlockReleasedProperty =
        DependencyProperty.Register(
            nameof(IsInterlockReleased),
            typeof(bool),
            typeof(DeviceButtonBase),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsFaultedProperty =
        DependencyProperty.Register(
            nameof(IsFaulted),
            typeof(bool),
            typeof(DeviceButtonBase),
            new FrameworkPropertyMetadata(false));

    public string DeviceName
    {
        get => (string)GetValue(DeviceNameProperty);
        set => SetValue(DeviceNameProperty, value);
    }

    public bool IsInterlockReleased
    {
        get => (bool)GetValue(IsInterlockReleasedProperty);
        set => SetValue(IsInterlockReleasedProperty, value);
    }

    public bool IsFaulted
    {
        get => (bool)GetValue(IsFaultedProperty);
        set => SetValue(IsFaultedProperty, value);
    }
}
