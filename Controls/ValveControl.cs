using System.Windows;
using System.Windows.Controls;

namespace Small_square_cavity_coating_machine.Controls;

/// <summary>
/// 两个三角形组成的通用阀门符号。
/// </summary>
public class ValveControl : DeviceButtonBase
{
    static ValveControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ValveControl),
            new FrameworkPropertyMetadata(typeof(ValveControl)));
    }

    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(
            nameof(Orientation),
            typeof(Orientation),
            typeof(ValveControl),
            new FrameworkPropertyMetadata(Orientation.Vertical));

    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(
            nameof(IsOpen),
            typeof(bool),
            typeof(ValveControl),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty UseOpeningPercentageProperty =
        DependencyProperty.Register(
            nameof(UseOpeningPercentage),
            typeof(bool),
            typeof(ValveControl),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty OpeningPercentageProperty =
        DependencyProperty.Register(
            nameof(OpeningPercentage),
            typeof(double),
            typeof(ValveControl),
            new FrameworkPropertyMetadata(0d));

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>
    /// Uses a proportional active fill instead of the binary IsOpen fill.
    /// </summary>
    public bool UseOpeningPercentage
    {
        get => (bool)GetValue(UseOpeningPercentageProperty);
        set => SetValue(UseOpeningPercentageProperty, value);
    }

    /// <summary>
    /// Active fill percentage, clamped visually to the 0–100 range.
    /// </summary>
    public double OpeningPercentage
    {
        get => (double)GetValue(OpeningPercentageProperty);
        set => SetValue(OpeningPercentageProperty, value);
    }
}
