using System.Windows;

namespace Small_square_cavity_coating_machine.Controls;

/// <summary>
/// 支持四向旋转以及分流、汇流动画的三通管道。
/// </summary>
public class TeePipeControl : PipeControlBase
{
    static TeePipeControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(TeePipeControl),
            new FrameworkPropertyMetadata(typeof(TeePipeControl)));
    }

    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(
            nameof(Orientation),
            typeof(TeeOrientation),
            typeof(TeePipeControl),
            new FrameworkPropertyMetadata(TeeOrientation.BranchTop));

    public TeeOrientation Orientation
    {
        get => (TeeOrientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }
}
