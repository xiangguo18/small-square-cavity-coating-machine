using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Small_square_cavity_coating_machine.Controls;

/// <summary>
/// Displays sample-stage speed feedback and submits a validated speed setpoint.
/// </summary>
public partial class SampleStageSpeedControl : UserControl
{
    public SampleStageSpeedControl()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty DisplayNameProperty =
        DependencyProperty.Register(
            nameof(DisplayName),
            typeof(string),
            typeof(SampleStageSpeedControl),
            new FrameworkPropertyMetadata("样品台转速"));

    public static readonly DependencyProperty CurrentSpeedProperty =
        DependencyProperty.Register(
            nameof(CurrentSpeed),
            typeof(double),
            typeof(SampleStageSpeedControl),
            new FrameworkPropertyMetadata(0d));

    public static readonly DependencyProperty SetpointSpeedProperty =
        DependencyProperty.Register(
            nameof(SetpointSpeed),
            typeof(double),
            typeof(SampleStageSpeedControl),
            new FrameworkPropertyMetadata(
                0d,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(
            nameof(Unit),
            typeof(string),
            typeof(SampleStageSpeedControl),
            new FrameworkPropertyMetadata("rpm"));

    public static readonly DependencyProperty SetpointCommandProperty =
        DependencyProperty.Register(
            nameof(SetpointCommand),
            typeof(ICommand),
            typeof(SampleStageSpeedControl),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty IsForwardRunningProperty =
        DependencyProperty.Register(
            nameof(IsForwardRunning),
            typeof(bool),
            typeof(SampleStageSpeedControl),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsReverseRunningProperty =
        DependencyProperty.Register(
            nameof(IsReverseRunning),
            typeof(bool),
            typeof(SampleStageSpeedControl),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty ForwardCommandProperty =
        DependencyProperty.Register(
            nameof(ForwardCommand),
            typeof(ICommand),
            typeof(SampleStageSpeedControl),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty ReverseCommandProperty =
        DependencyProperty.Register(
            nameof(ReverseCommand),
            typeof(ICommand),
            typeof(SampleStageSpeedControl),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty StopCommandProperty =
        DependencyProperty.Register(
            nameof(StopCommand),
            typeof(ICommand),
            typeof(SampleStageSpeedControl),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty IsStoppedProperty =
        DependencyProperty.Register(
            nameof(IsStopped),
            typeof(bool),
            typeof(SampleStageSpeedControl),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty ShowDirectionControlsProperty =
        DependencyProperty.Register(
            nameof(ShowDirectionControls),
            typeof(bool),
            typeof(SampleStageSpeedControl),
            new FrameworkPropertyMetadata(true));

    public static readonly DependencyProperty ShowStatusIndicatorProperty =
        DependencyProperty.Register(
            nameof(ShowStatusIndicator),
            typeof(bool),
            typeof(SampleStageSpeedControl),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsRunningProperty =
        DependencyProperty.Register(
            nameof(IsRunning),
            typeof(bool),
            typeof(SampleStageSpeedControl),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsFaultedProperty =
        DependencyProperty.Register(
            nameof(IsFaulted),
            typeof(bool),
            typeof(SampleStageSpeedControl),
            new FrameworkPropertyMetadata(false));

    public string DisplayName
    {
        get => (string)GetValue(DisplayNameProperty);
        set => SetValue(DisplayNameProperty, value);
    }

    public double CurrentSpeed
    {
        get => (double)GetValue(CurrentSpeedProperty);
        set => SetValue(CurrentSpeedProperty, value);
    }

    public double SetpointSpeed
    {
        get => (double)GetValue(SetpointSpeedProperty);
        set => SetValue(SetpointSpeedProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public ICommand? SetpointCommand
    {
        get => (ICommand?)GetValue(SetpointCommandProperty);
        set => SetValue(SetpointCommandProperty, value);
    }

    public bool IsForwardRunning
    {
        get => (bool)GetValue(IsForwardRunningProperty);
        set => SetValue(IsForwardRunningProperty, value);
    }

    public bool IsReverseRunning
    {
        get => (bool)GetValue(IsReverseRunningProperty);
        set => SetValue(IsReverseRunningProperty, value);
    }

    public ICommand? ForwardCommand
    {
        get => (ICommand?)GetValue(ForwardCommandProperty);
        set => SetValue(ForwardCommandProperty, value);
    }

    public ICommand? ReverseCommand
    {
        get => (ICommand?)GetValue(ReverseCommandProperty);
        set => SetValue(ReverseCommandProperty, value);
    }

    public ICommand? StopCommand
    {
        get => (ICommand?)GetValue(StopCommandProperty);
        set => SetValue(StopCommandProperty, value);
    }

    public bool IsStopped
    {
        get => (bool)GetValue(IsStoppedProperty);
        set => SetValue(IsStoppedProperty, value);
    }

    public bool ShowDirectionControls
    {
        get => (bool)GetValue(ShowDirectionControlsProperty);
        set => SetValue(ShowDirectionControlsProperty, value);
    }

    public bool ShowStatusIndicator
    {
        get => (bool)GetValue(ShowStatusIndicatorProperty);
        set => SetValue(ShowStatusIndicatorProperty, value);
    }

    public bool IsRunning
    {
        get => (bool)GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    public bool IsFaulted
    {
        get => (bool)GetValue(IsFaultedProperty);
        set => SetValue(IsFaultedProperty, value);
    }

    private void SetpointTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        if (!double.TryParse(
                SetpointTextBox.Text,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out var requestedValue)
            || !double.IsFinite(requestedValue)
            || requestedValue < 0)
        {
            ShowInvalidSetpoint();
            return;
        }

        if (SetpointCommand?.CanExecute(requestedValue) == true)
        {
            SetpointCommand.Execute(requestedValue);
        }
        else
        {
            SetCurrentValue(SetpointSpeedProperty, requestedValue);
        }

        RestoreSetpointText();
        Keyboard.ClearFocus();
    }

    private void SetpointTextBox_LostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        RestoreSetpointText();
    }

    private void ShowInvalidSetpoint()
    {
        MessageBox.Show(
            Window.GetWindow(this),
            "样品台转速必须是大于或等于 0 的数字。",
            "样品台转速设置",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        RestoreSetpointText();
        SetpointTextBox.Focus();
        SetpointTextBox.SelectAll();
    }

    private void RestoreSetpointText()
    {
        SetpointTextBox.Text =
            SetpointSpeed.ToString("0.0", CultureInfo.CurrentCulture);
    }
}
