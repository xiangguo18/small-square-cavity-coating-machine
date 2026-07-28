using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Small_square_cavity_coating_machine.Controls;

/// <summary>
/// APC valve readout and setpoint editor. The valve symbol itself is not clickable.
/// </summary>
public partial class ApcValveControl : UserControl
{
    public ApcValveControl()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty DisplayNameProperty =
        DependencyProperty.Register(
            nameof(DisplayName),
            typeof(string),
            typeof(ApcValveControl),
            new FrameworkPropertyMetadata("APC阀"));

    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(
            nameof(Orientation),
            typeof(Orientation),
            typeof(ApcValveControl),
            new FrameworkPropertyMetadata(Orientation.Horizontal));

    public static readonly DependencyProperty PlacementProperty =
        DependencyProperty.Register(
            nameof(Placement),
            typeof(ApcLayoutPlacement),
            typeof(ApcValveControl),
            new FrameworkPropertyMetadata(
                ApcLayoutPlacement.TopRight,
                OnPlacementChanged));

    public static readonly DependencyProperty IsPositioningModeProperty =
        DependencyProperty.Register(
            nameof(IsPositioningMode),
            typeof(bool),
            typeof(ApcValveControl),
            new FrameworkPropertyMetadata(true, OnIsPositioningModeChanged));

    public static readonly DependencyProperty CurrentPositionProperty =
        DependencyProperty.Register(
            nameof(CurrentPosition),
            typeof(double),
            typeof(ApcValveControl),
            new FrameworkPropertyMetadata(0d));

    public static readonly DependencyProperty PositionSetpointProperty =
        DependencyProperty.Register(
            nameof(PositionSetpoint),
            typeof(double),
            typeof(ApcValveControl),
            new FrameworkPropertyMetadata(
                0d,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty CurrentPressureProperty =
        DependencyProperty.Register(
            nameof(CurrentPressure),
            typeof(double),
            typeof(ApcValveControl),
            new FrameworkPropertyMetadata(0d));

    public static readonly DependencyProperty PressureSetpointProperty =
        DependencyProperty.Register(
            nameof(PressureSetpoint),
            typeof(double),
            typeof(ApcValveControl),
            new FrameworkPropertyMetadata(
                0.1d,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(
            nameof(IsOpen),
            typeof(bool),
            typeof(ApcValveControl),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsInterlockReleasedProperty =
        DependencyProperty.Register(
            nameof(IsInterlockReleased),
            typeof(bool),
            typeof(ApcValveControl),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsFaultedProperty =
        DependencyProperty.Register(
            nameof(IsFaulted),
            typeof(bool),
            typeof(ApcValveControl),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty ToggleModeCommandProperty =
        DependencyProperty.Register(
            nameof(ToggleModeCommand),
            typeof(ICommand),
            typeof(ApcValveControl),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty PositionSetpointCommandProperty =
        DependencyProperty.Register(
            nameof(PositionSetpointCommand),
            typeof(ICommand),
            typeof(ApcValveControl),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty PressureSetpointCommandProperty =
        DependencyProperty.Register(
            nameof(PressureSetpointCommand),
            typeof(ICommand),
            typeof(ApcValveControl),
            new FrameworkPropertyMetadata(null));

    public string DisplayName
    {
        get => (string)GetValue(DisplayNameProperty);
        set => SetValue(DisplayNameProperty, value);
    }

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public ApcLayoutPlacement Placement
    {
        get => (ApcLayoutPlacement)GetValue(PlacementProperty);
        set => SetValue(PlacementProperty, value);
    }

    public bool IsPositioningMode
    {
        get => (bool)GetValue(IsPositioningModeProperty);
        set => SetValue(IsPositioningModeProperty, value);
    }

    public double CurrentPosition
    {
        get => (double)GetValue(CurrentPositionProperty);
        set => SetValue(CurrentPositionProperty, value);
    }

    public double PositionSetpoint
    {
        get => (double)GetValue(PositionSetpointProperty);
        set => SetValue(PositionSetpointProperty, value);
    }

    public double CurrentPressure
    {
        get => (double)GetValue(CurrentPressureProperty);
        set => SetValue(CurrentPressureProperty, value);
    }

    public double PressureSetpoint
    {
        get => (double)GetValue(PressureSetpointProperty);
        set => SetValue(PressureSetpointProperty, value);
    }

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
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

    public ICommand? ToggleModeCommand
    {
        get => (ICommand?)GetValue(ToggleModeCommandProperty);
        set => SetValue(ToggleModeCommandProperty, value);
    }

    public ICommand? PositionSetpointCommand
    {
        get => (ICommand?)GetValue(PositionSetpointCommandProperty);
        set => SetValue(PositionSetpointCommandProperty, value);
    }

    public ICommand? PressureSetpointCommand
    {
        get => (ICommand?)GetValue(PressureSetpointCommandProperty);
        set => SetValue(PressureSetpointCommandProperty, value);
    }

    private void PositionSetpointTextBox_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        if (!double.TryParse(
                PositionSetpointTextBox.Text,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out var requestedValue)
            || !double.IsFinite(requestedValue))
        {
            ShowInvalidPosition();
            return;
        }

        var clampedValue = Math.Clamp(requestedValue, 0d, 100d);
        if (PositionSetpointCommand?.CanExecute(clampedValue) == true)
        {
            PositionSetpointCommand.Execute(clampedValue);
        }
        else
        {
            SetCurrentValue(PositionSetpointProperty, clampedValue);
        }

        RestorePositionText();
        Keyboard.ClearFocus();
    }

    private void PositionSetpointTextBox_LostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        RestorePositionText();
    }

    private void PressureSetpointTextBox_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        if (!double.TryParse(
                PressureSetpointTextBox.Text,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out var requestedValue)
            || !double.IsFinite(requestedValue)
            || requestedValue < 0.1d
            || requestedValue > 10d)
        {
            ShowInvalidPressure();
            return;
        }

        if (PressureSetpointCommand?.CanExecute(requestedValue) == true)
        {
            PressureSetpointCommand.Execute(requestedValue);
        }
        else
        {
            SetCurrentValue(PressureSetpointProperty, requestedValue);
        }

        RestorePressureText();
        Keyboard.ClearFocus();
    }

    private void PressureSetpointTextBox_LostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        RestorePressureText();
    }

    private void ShowInvalidPosition()
    {
        MessageBox.Show(
            Window.GetWindow(this),
            "设定位置必须是数字，系统会自动限制在 0–100% 范围内。",
            "APC阀位置设置",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        RestorePositionText();
        PositionSetpointTextBox.Focus();
        PositionSetpointTextBox.SelectAll();
    }

    private void ShowInvalidPressure()
    {
        MessageBox.Show(
            Window.GetWindow(this),
            "设定压力必须是 0.1–10 Pa 范围内的数字。",
            "APC阀压力设置",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        RestorePressureText();
        PressureSetpointTextBox.Focus();
        PressureSetpointTextBox.SelectAll();
    }

    private void RestorePositionText()
    {
        PositionSetpointTextBox.Text =
            PositionSetpoint.ToString("0.0", CultureInfo.CurrentCulture);
    }

    private void RestorePressureText()
    {
        PressureSetpointTextBox.Text =
            PressureSetpoint.ToString("0.00", CultureInfo.CurrentCulture);
    }

    private static void OnIsPositioningModeChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        ((ApcValveControl)dependencyObject).UpdateModeVisuals(animate: true);
    }

    private void ApcValveControl_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateModeVisuals(animate: false);
        UpdateLayoutPlacement();
    }

    private void UpdateModeVisuals(bool animate)
    {
        if (PositionPanel is null
            || PressurePanel is null
            || PositionPanel.RenderTransform is not TranslateTransform positionTransform
            || PressurePanel.RenderTransform is not TranslateTransform pressureTransform)
        {
            return;
        }

        AnimateTransform(
            positionTransform,
            IsPositioningMode ? 0d : -220d,
            animate);
        AnimateOpacity(
            PositionPanel,
            IsPositioningMode ? 1d : 0d,
            animate);
        AnimateTransform(
            pressureTransform,
            IsPositioningMode ? 220d : 0d,
            animate);
        AnimateOpacity(
            PressurePanel,
            IsPositioningMode ? 0d : 1d,
            animate);
    }

    private static void AnimateTransform(
        TranslateTransform target,
        double targetValue,
        bool animate)
    {
        var currentValue = target.X;
        target.BeginAnimation(TranslateTransform.XProperty, null);
        target.X = targetValue;

        if (!animate || Math.Abs(currentValue - targetValue) < double.Epsilon)
        {
            return;
        }

        target.BeginAnimation(
            TranslateTransform.XProperty,
            CreateModeAnimation(currentValue, targetValue));
    }

    private static void AnimateOpacity(
        UIElement target,
        double targetValue,
        bool animate)
    {
        var currentValue = target.Opacity;
        target.BeginAnimation(OpacityProperty, null);
        target.Opacity = targetValue;

        if (!animate || Math.Abs(currentValue - targetValue) < double.Epsilon)
        {
            return;
        }

        target.BeginAnimation(
            OpacityProperty,
            CreateModeAnimation(currentValue, targetValue));
    }

    private static DoubleAnimation CreateModeAnimation(
        double from,
        double to)
    {
        return new DoubleAnimation(
            from,
            to,
            TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new SineEase
            {
                EasingMode = EasingMode.EaseInOut
            },
            FillBehavior = FillBehavior.Stop
        };
    }

    private static void OnPlacementChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        ((ApcValveControl)dependencyObject).UpdateLayoutPlacement();
    }

    private void UpdateLayoutPlacement()
    {
        if (LayoutCanvas is null
            || ValveSymbol is null
            || ModeReadout is null
            || ModeSwitch is null)
        {
            return;
        }

        const double gap = 8d;
        var readoutSide = (RelativeSide)((int)Placement / 4);
        var switchSide = (RelativeSide)((int)Placement % 4);

        var valveBounds = new Rect(
            0d,
            0d,
            ValveSymbol.Width,
            ValveSymbol.Height);
        var readoutBounds = PlaceRelative(
            valveBounds,
            ModeReadout.Width,
            ModeReadout.Height,
            readoutSide,
            gap);
        var switchBounds = PlaceRelative(
            readoutBounds,
            ModeSwitch.Width,
            ModeSwitch.Height,
            switchSide,
            gap);

        var minimumX = Math.Min(
            valveBounds.Left,
            Math.Min(readoutBounds.Left, switchBounds.Left));
        var minimumY = Math.Min(
            valveBounds.Top,
            Math.Min(readoutBounds.Top, switchBounds.Top));
        var maximumX = Math.Max(
            valveBounds.Right,
            Math.Max(readoutBounds.Right, switchBounds.Right));
        var maximumY = Math.Max(
            valveBounds.Bottom,
            Math.Max(readoutBounds.Bottom, switchBounds.Bottom));

        SetCanvasBounds(ValveSymbol, valveBounds, minimumX, minimumY);
        SetCanvasBounds(ModeReadout, readoutBounds, minimumX, minimumY);
        SetCanvasBounds(ModeSwitch, switchBounds, minimumX, minimumY);

        LayoutCanvas.Width = maximumX - minimumX;
        LayoutCanvas.Height = maximumY - minimumY;
    }

    private static Rect PlaceRelative(
        Rect anchor,
        double width,
        double height,
        RelativeSide side,
        double gap)
    {
        return side switch
        {
            RelativeSide.Top => new Rect(
                anchor.Left + ((anchor.Width - width) / 2d),
                anchor.Top - height - gap,
                width,
                height),
            RelativeSide.Right => new Rect(
                anchor.Right + gap,
                anchor.Top + ((anchor.Height - height) / 2d),
                width,
                height),
            RelativeSide.Bottom => new Rect(
                anchor.Left + ((anchor.Width - width) / 2d),
                anchor.Bottom + gap,
                width,
                height),
            RelativeSide.Left => new Rect(
                anchor.Left - width - gap,
                anchor.Top + ((anchor.Height - height) / 2d),
                width,
                height),
            _ => throw new ArgumentOutOfRangeException(nameof(side))
        };
    }

    private static void SetCanvasBounds(
        FrameworkElement element,
        Rect bounds,
        double minimumX,
        double minimumY)
    {
        Canvas.SetLeft(element, bounds.Left - minimumX);
        Canvas.SetTop(element, bounds.Top - minimumY);
    }

    private enum RelativeSide
    {
        Top,
        Right,
        Bottom,
        Left
    }
}
