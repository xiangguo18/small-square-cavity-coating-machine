using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Small_square_cavity_coating_machine.Controls;

/// <summary>
/// 名称、当前流量和设定流量三行显示的MFC用户控件。
/// </summary>
public partial class MfcFlowMeterControl : UserControl
{
    public MfcFlowMeterControl()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty DisplayNameProperty =
        DependencyProperty.Register(
            nameof(DisplayName),
            typeof(string),
            typeof(MfcFlowMeterControl),
            new FrameworkPropertyMetadata("MFC"));

    public static readonly DependencyProperty CurrentFlowProperty =
        DependencyProperty.Register(
            nameof(CurrentFlow),
            typeof(double),
            typeof(MfcFlowMeterControl),
            new FrameworkPropertyMetadata(0d));

    public static readonly DependencyProperty SetpointFlowProperty =
        DependencyProperty.Register(
            nameof(SetpointFlow),
            typeof(double),
            typeof(MfcFlowMeterControl),
            new FrameworkPropertyMetadata(0d));

    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(
            nameof(Unit),
            typeof(string),
            typeof(MfcFlowMeterControl),
            new FrameworkPropertyMetadata("sccm"));

    public static readonly DependencyProperty ValueFormatProperty =
        DependencyProperty.Register(
            nameof(ValueFormat),
            typeof(string),
            typeof(MfcFlowMeterControl),
            new FrameworkPropertyMetadata("0.0"));

    public static readonly DependencyProperty SetpointCommandProperty =
        DependencyProperty.Register(
            nameof(SetpointCommand),
            typeof(ICommand),
            typeof(MfcFlowMeterControl),
            new FrameworkPropertyMetadata(null));

    public string DisplayName
    {
        get => (string)GetValue(DisplayNameProperty);
        set => SetValue(DisplayNameProperty, value);
    }

    public double CurrentFlow
    {
        get => (double)GetValue(CurrentFlowProperty);
        set => SetValue(CurrentFlowProperty, value);
    }

    public double SetpointFlow
    {
        get => (double)GetValue(SetpointFlowProperty);
        set => SetValue(SetpointFlowProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public string ValueFormat
    {
        get => (string)GetValue(ValueFormatProperty);
        set => SetValue(ValueFormatProperty, value);
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
            || requestedValue < 0d)
        {
            ShowInvalidSetpoint();
            return;
        }

        if (SetpointCommand is null)
        {
            SetCurrentValue(SetpointFlowProperty, requestedValue);
        }
        else if (SetpointCommand.CanExecute(requestedValue))
        {
            SetpointCommand.Execute(requestedValue);
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
            "设定流量必须是大于或等于 0 的数字。",
            "MFC流量设置",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        RestoreSetpointText();
        SetpointTextBox.Focus();
        SetpointTextBox.SelectAll();
    }

    private void RestoreSetpointText()
    {
        SetpointTextBox.Text = SetpointFlow.ToString(ValueFormat, CultureInfo.CurrentCulture);
    }
}
