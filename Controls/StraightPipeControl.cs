using System.Windows;
using System.Windows.Controls;

namespace Small_square_cavity_coating_machine.Controls;

/// <summary>
/// 可横向或竖向放置的直管。
/// </summary>
public class StraightPipeControl : PipeControlBase
{
    static StraightPipeControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(StraightPipeControl),
            new FrameworkPropertyMetadata(typeof(StraightPipeControl)));
    }

    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(
            nameof(Orientation),
            typeof(Orientation),
            typeof(StraightPipeControl),
            new FrameworkPropertyMetadata(Orientation.Horizontal));

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }
}
