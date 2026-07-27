using System.Windows;
using System.Windows.Controls;

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
}
