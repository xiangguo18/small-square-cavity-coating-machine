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
