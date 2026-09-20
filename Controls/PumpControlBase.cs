using System.Windows;

namespace Small_square_cavity_coating_machine.Controls;

/// <summary>
/// 真空泵图形的公共状态。
/// </summary>
public abstract class PumpControlBase : DeviceButtonBase
{
    public static readonly DependencyProperty IsRunningProperty =
        DependencyProperty.Register(
            nameof(IsRunning),
            typeof(bool),
            typeof(PumpControlBase),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty VisualStateProperty =
        DependencyProperty.Register(
            nameof(VisualState),
            typeof(PumpVisualState),
            typeof(PumpControlBase),
            new FrameworkPropertyMetadata(PumpVisualState.Idle));

    public static readonly DependencyProperty LabelPlacementProperty =
        DependencyProperty.Register(
            nameof(LabelPlacement),
            typeof(PumpLabelPlacement),
            typeof(PumpControlBase),
            new FrameworkPropertyMetadata(PumpLabelPlacement.Right));

    public bool IsRunning
    {
        get => (bool)GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    /// <summary>
    /// 由 PLC 状态字映射出的图标颜色与闪烁状态，不作为设备运行判据。
    /// </summary>
    public PumpVisualState VisualState
    {
        get => (PumpVisualState)GetValue(VisualStateProperty);
        set => SetValue(VisualStateProperty, value);
    }

    public PumpLabelPlacement LabelPlacement
    {
        get => (PumpLabelPlacement)GetValue(LabelPlacementProperty);
        set => SetValue(LabelPlacementProperty, value);
    }
}
