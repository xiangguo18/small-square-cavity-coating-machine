using System.Windows;

namespace Small_square_cavity_coating_machine.Controls;

/// <summary>
/// Displays a commandable ON/OFF switch without changing equipment feedback locally.
/// </summary>
public class ShutterSwitchButton : DeviceButtonBase
{
    static ShutterSwitchButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ShutterSwitchButton),
            new FrameworkPropertyMetadata(typeof(ShutterSwitchButton)));
    }

    public static readonly DependencyProperty IsOnProperty =
        DependencyProperty.Register(
            nameof(IsOn),
            typeof(bool),
            typeof(ShutterSwitchButton),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty OnTextProperty =
        DependencyProperty.Register(
            nameof(OnText),
            typeof(string),
            typeof(ShutterSwitchButton),
            new FrameworkPropertyMetadata("ON"));

    public static readonly DependencyProperty OffTextProperty =
        DependencyProperty.Register(
            nameof(OffText),
            typeof(string),
            typeof(ShutterSwitchButton),
            new FrameworkPropertyMetadata("OFF"));

    public bool IsOn
    {
        get => (bool)GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    public string OnText
    {
        get => (string)GetValue(OnTextProperty);
        set => SetValue(OnTextProperty, value);
    }

    public string OffText
    {
        get => (string)GetValue(OffTextProperty);
        set => SetValue(OffTextProperty, value);
    }
}
