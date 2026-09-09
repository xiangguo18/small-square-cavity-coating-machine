using System.Windows;
using System.Windows.Controls;

namespace Small_square_cavity_coating_machine.Controls;

/// <summary>
/// 独立的真空计名称和数值显示面板，不承载任何设备命令。
/// </summary>
public partial class VacuumGaugeReadoutControl : UserControl
{
    public VacuumGaugeReadoutControl()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty DisplayNameProperty =
        DependencyProperty.Register(
            nameof(DisplayName),
            typeof(string),
            typeof(VacuumGaugeReadoutControl),
            new FrameworkPropertyMetadata("真空度"));

    public static readonly DependencyProperty VacuumValueProperty =
        DependencyProperty.Register(
            nameof(VacuumValue),
            typeof(double),
            typeof(VacuumGaugeReadoutControl),
            new FrameworkPropertyMetadata(100000d));

    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(
            nameof(Unit),
            typeof(string),
            typeof(VacuumGaugeReadoutControl),
            new FrameworkPropertyMetadata("Pa"));

    public static readonly DependencyProperty ValueFormatProperty =
        DependencyProperty.Register(
            nameof(ValueFormat),
            typeof(string),
            typeof(VacuumGaugeReadoutControl),
            new FrameworkPropertyMetadata("0.0E+0"));

    public static readonly DependencyProperty IsReadingEnabledProperty =
        DependencyProperty.Register(
            nameof(IsReadingEnabled),
            typeof(bool),
            typeof(VacuumGaugeReadoutControl),
            new FrameworkPropertyMetadata(true));

    public string DisplayName
    {
        get => (string)GetValue(DisplayNameProperty);
        set => SetValue(DisplayNameProperty, value);
    }

    public double VacuumValue
    {
        get => (double)GetValue(VacuumValueProperty);
        set => SetValue(VacuumValueProperty, value);
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

    public bool IsReadingEnabled
    {
        get => (bool)GetValue(IsReadingEnabledProperty);
        set => SetValue(IsReadingEnabledProperty, value);
    }
}
