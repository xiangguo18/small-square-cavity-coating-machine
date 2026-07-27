using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Small_square_cavity_coating_machine.Controls;

/// <summary>
/// 动态管道的公共可绑定属性。
/// </summary>
public abstract class PipeControlBase : Control
{
    public static readonly DependencyProperty IsFlowingProperty =
        DependencyProperty.Register(
            nameof(IsFlowing),
            typeof(bool),
            typeof(PipeControlBase),
            new FrameworkPropertyMetadata(false, OnAnimationPropertyChanged));

    public static readonly DependencyProperty FlowAnimationDirectionProperty =
        DependencyProperty.Register(
            nameof(FlowAnimationDirection),
            typeof(PipeFlowDirection),
            typeof(PipeControlBase),
            new FrameworkPropertyMetadata(
                PipeFlowDirection.Forward,
                OnAnimationPropertyChanged));

    public static readonly DependencyProperty AnimationDurationProperty =
        DependencyProperty.Register(
            nameof(AnimationDuration),
            typeof(Duration),
            typeof(PipeControlBase),
            new FrameworkPropertyMetadata(
                new Duration(TimeSpan.FromSeconds(0.45)),
                OnAnimationPropertyChanged));

    public static readonly DependencyProperty PipeThicknessProperty =
        DependencyProperty.Register(
            nameof(PipeThickness),
            typeof(double),
            typeof(PipeControlBase),
            new FrameworkPropertyMetadata(16d),
            value => (double)value > 0);

    public static readonly DependencyProperty FlowDashLengthProperty =
        DependencyProperty.Register(
            nameof(FlowDashLength),
            typeof(double),
            typeof(PipeControlBase),
            new FrameworkPropertyMetadata(9d, OnAnimationPropertyChanged),
            value => (double)value > 0);

    public static readonly DependencyProperty FlowGapLengthProperty =
        DependencyProperty.Register(
            nameof(FlowGapLength),
            typeof(double),
            typeof(PipeControlBase),
            new FrameworkPropertyMetadata(6d, OnAnimationPropertyChanged),
            value => (double)value > 0);

    public bool IsFlowing
    {
        get => (bool)GetValue(IsFlowingProperty);
        set => SetValue(IsFlowingProperty, value);
    }

    public PipeFlowDirection FlowAnimationDirection
    {
        get => (PipeFlowDirection)GetValue(FlowAnimationDirectionProperty);
        set => SetValue(FlowAnimationDirectionProperty, value);
    }

    public Duration AnimationDuration
    {
        get => (Duration)GetValue(AnimationDurationProperty);
        set => SetValue(AnimationDurationProperty, value);
    }

    public double PipeThickness
    {
        get => (double)GetValue(PipeThicknessProperty);
        set => SetValue(PipeThicknessProperty, value);
    }

    /// <summary>
    /// 流动虚线中每一段实线的屏幕长度。
    /// </summary>
    public double FlowDashLength
    {
        get => (double)GetValue(FlowDashLengthProperty);
        set => SetValue(FlowDashLengthProperty, value);
    }

    /// <summary>
    /// 相邻流动虚线之间的屏幕间隔。
    /// </summary>
    public double FlowGapLength
    {
        get => (double)GetValue(FlowGapLengthProperty);
        set => SetValue(FlowGapLengthProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateFlowAnimation();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateFlowAnimation();
    }

    private static void OnAnimationPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        ((PipeControlBase)dependencyObject).UpdateFlowAnimation();
    }

    private void UpdateFlowAnimation()
    {
        AnimateTemplatePart("PART_FlowPath");
        AnimateTemplatePart("PART_HorizontalFlowPath");
        AnimateTemplatePart("PART_VerticalFlowPath");
    }

    private void AnimateTemplatePart(string partName)
    {
        if (GetTemplateChild(partName) is not Shape flowShape)
        {
            return;
        }

        flowShape.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
        flowShape.StrokeDashOffset = 0;

        var scale = GetLongitudinalScale(partName);
        var strokeThickness = flowShape.StrokeThickness;
        var dashUnit = FlowDashLength / (strokeThickness * scale);
        var gapUnit = FlowGapLength / (strokeThickness * scale);

        flowShape.StrokeDashArray = new DoubleCollection
        {
            dashUnit,
            gapUnit
        };

        if (!IsFlowing)
        {
            return;
        }

        var cycleOffset = dashUnit + gapUnit;
        var offset = FlowAnimationDirection == PipeFlowDirection.Forward
            ? -cycleOffset
            : cycleOffset;
        var animation = new DoubleAnimation(0, offset, AnimationDuration)
        {
            RepeatBehavior = RepeatBehavior.Forever
        };

        flowShape.BeginAnimation(
            Shape.StrokeDashOffsetProperty,
            animation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private double GetLongitudinalScale(string partName)
    {
        var scale = partName switch
        {
            "PART_HorizontalFlowPath" => ActualWidth / 100d,
            "PART_VerticalFlowPath" => ActualHeight / 100d,
            "PART_FlowPath" => (ActualWidth + ActualHeight) / 200d,
            _ => 1d
        };

        return double.IsFinite(scale) && scale > 0 ? scale : 1d;
    }
}
