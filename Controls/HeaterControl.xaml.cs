using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Small_square_cavity_coating_machine.Controls;

/// <summary>
/// Displays heater feedback and sends validated setpoint and start/stop commands.
/// </summary>
public partial class HeaterControl : UserControl
{
    public HeaterControl()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty DisplayNameProperty =
        DependencyProperty.Register(
            nameof(DisplayName),
            typeof(string),
            typeof(HeaterControl),
            new FrameworkPropertyMetadata("加热"));

    public static readonly DependencyProperty CurrentTemperatureProperty =
        DependencyProperty.Register(
            nameof(CurrentTemperature),
            typeof(double),
            typeof(HeaterControl),
            new FrameworkPropertyMetadata(0d));

    public static readonly DependencyProperty SetpointTemperatureProperty =
        DependencyProperty.Register(
            nameof(SetpointTemperature),
            typeof(double),
            typeof(HeaterControl),
            new FrameworkPropertyMetadata(
                0d,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty IsRunningProperty =
        DependencyProperty.Register(
            nameof(IsRunning),
            typeof(bool),
            typeof(HeaterControl),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsInterlockReleasedProperty =
        DependencyProperty.Register(
            nameof(IsInterlockReleased),
            typeof(bool),
            typeof(HeaterControl),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsFaultedProperty =
        DependencyProperty.Register(
            nameof(IsFaulted),
            typeof(bool),
            typeof(HeaterControl),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty ToggleCommandProperty =
        DependencyProperty.Register(
            nameof(ToggleCommand),
            typeof(ICommand),
            typeof(HeaterControl),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty SetpointCommandProperty =
        DependencyProperty.Register(
            nameof(SetpointCommand),
            typeof(ICommand),
            typeof(HeaterControl),
            new FrameworkPropertyMetadata(null));

    public string DisplayName
    {
        get => (string)GetValue(DisplayNameProperty);
        set => SetValue(DisplayNameProperty, value);
    }

    public double CurrentTemperature
    {
        get => (double)GetValue(CurrentTemperatureProperty);
        set => SetValue(CurrentTemperatureProperty, value);
    }

    public double SetpointTemperature
    {
        get => (double)GetValue(SetpointTemperatureProperty);
        set => SetValue(SetpointTemperatureProperty, value);
    }

    public bool IsRunning
    {
        get => (bool)GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
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

    public ICommand? ToggleCommand
    {
        get => (ICommand?)GetValue(ToggleCommandProperty);
        set => SetValue(ToggleCommandProperty, value);
    }

    public ICommand? SetpointCommand
    {
        get => (ICommand?)GetValue(SetpointCommandProperty);
        set => SetValue(SetpointCommandProperty, value);
    }

    private void SetpointTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            RestoreSetpointText();
            Keyboard.ClearFocus();
            return;
        }

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
            SetCurrentValue(SetpointTemperatureProperty, requestedValue);
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
            "预设温度必须是大于或等于 0 的数字。",
            "加热温度设置",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        RestoreSetpointText();
        SetpointTextBox.Focus();
        SetpointTextBox.SelectAll();
    }

    private void RestoreSetpointText()
    {
        SetpointTextBox.Text =
            SetpointTemperature.ToString("0.0", CultureInfo.CurrentCulture);
    }
}
