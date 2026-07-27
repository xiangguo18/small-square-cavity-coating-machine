using System.Windows;

namespace Small_square_cavity_coating_machine.Controls;

/// <summary>
/// 可旋转到四个方向的90°弯头。
/// </summary>
public class ElbowPipeControl : PipeControlBase
{
    static ElbowPipeControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ElbowPipeControl),
            new FrameworkPropertyMetadata(typeof(ElbowPipeControl)));
    }

    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(
            nameof(Orientation),
            typeof(ElbowOrientation),
            typeof(ElbowPipeControl),
            new FrameworkPropertyMetadata(ElbowOrientation.TopRight));

    public ElbowOrientation Orientation
    {
        get => (ElbowOrientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }
}
