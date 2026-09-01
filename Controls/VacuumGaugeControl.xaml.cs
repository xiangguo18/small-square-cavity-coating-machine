using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Small_square_cavity_coating_machine.Controls;

/// <summary>
/// 高真空计符号、名称和真空度读数的用户控件。
/// </summary>
public partial class VacuumGaugeControl : UserControl
{
    public VacuumGaugeControl()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty DisplayNameProperty =
        DependencyProperty.Register(
            nameof(DisplayName),
            typeof(string),
            typeof(VacuumGaugeControl),
            new FrameworkPropertyMetadata("真空度"));

    public static readonly DependencyProperty VacuumValueProperty =
        DependencyProperty.Register(
            nameof(VacuumValue),
            typeof(double),
            typeof(VacuumGaugeControl),
            new FrameworkPropertyMetadata(100000d));

    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(
            nameof(Unit),
            typeof(string),
            typeof(VacuumGaugeControl),
            new FrameworkPropertyMetadata("Pa"));

    public static readonly DependencyProperty ValueFormatProperty =
        DependencyProperty.Register(
            nameof(ValueFormat),
            typeof(string),
            typeof(VacuumGaugeControl),
            new FrameworkPropertyMetadata("0.0E+0"));

    public static readonly DependencyProperty SymbolRotationProperty =
        DependencyProperty.Register(
            nameof(SymbolRotation),
            typeof(double),
            typeof(VacuumGaugeControl),
            new FrameworkPropertyMetadata(0d));

    public static readonly DependencyProperty TextPlacementProperty =
        DependencyProperty.Register(
            nameof(TextPlacement),
            typeof(GaugeTextPlacement),
            typeof(VacuumGaugeControl),
            new FrameworkPropertyMetadata(GaugeTextPlacement.Right));

    public static readonly DependencyProperty ReadoutWidthProperty =
        DependencyProperty.Register(
            nameof(ReadoutWidth),
            typeof(double),
            typeof(VacuumGaugeControl),
            new FrameworkPropertyMetadata(204d));

    public static readonly DependencyProperty ReadoutHeightProperty =
        DependencyProperty.Register(
            nameof(ReadoutHeight),
            typeof(double),
            typeof(VacuumGaugeControl),
            new FrameworkPropertyMetadata(68d));

    public static readonly DependencyProperty IsReadingEnabledProperty =
        DependencyProperty.Register(
            nameof(IsReadingEnabled),
            typeof(bool),
            typeof(VacuumGaugeControl),
            new FrameworkPropertyMetadata(true));

    public static readonly DependencyProperty IsFaultedProperty =
        DependencyProperty.Register(
            nameof(IsFaulted),
            typeof(bool),
            typeof(VacuumGaugeControl),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty ShowValveStatusProperty =
        DependencyProperty.Register(
            nameof(ShowValveStatus),
            typeof(bool),
            typeof(VacuumGaugeControl),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsValveOpenProperty =
        DependencyProperty.Register(
            nameof(IsValveOpen),
            typeof(bool),
            typeof(VacuumGaugeControl),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsValveFaultedProperty =
        DependencyProperty.Register(
            nameof(IsValveFaulted),
            typeof(bool),
            typeof(VacuumGaugeControl),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty CanToggleProperty =
        DependencyProperty.Register(
            nameof(CanToggle),
            typeof(bool),
            typeof(VacuumGaugeControl),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty ToggleCommandProperty =
        DependencyProperty.Register(
            nameof(ToggleCommand),
            typeof(ICommand),
            typeof(VacuumGaugeControl),
            new FrameworkPropertyMetadata(null));

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

    /// <summary>
    /// 只旋转圆圈和连接杆，圆内图案、名称和数值保持正向。
    /// </summary>
    public double SymbolRotation
    {
        get => (double)GetValue(SymbolRotationProperty);
        set => SetValue(SymbolRotationProperty, value);
    }

    /// <summary>
    /// 名称和数值文本框相对圆形符号的位置。
    /// </summary>
    public GaugeTextPlacement TextPlacement
    {
        get => (GaugeTextPlacement)GetValue(TextPlacementProperty);
        set => SetValue(TextPlacementProperty, value);
    }

    /// <summary>
    /// 名称和真空度文本框在控件内部坐标系中的宽度。
    /// </summary>
    public double ReadoutWidth
    {
        get => (double)GetValue(ReadoutWidthProperty);
        set => SetValue(ReadoutWidthProperty, value);
    }

    /// <summary>
    /// 名称和真空度文本框在控件内部坐标系中的高度。
    /// </summary>
    public double ReadoutHeight
    {
        get => (double)GetValue(ReadoutHeightProperty);
        set => SetValue(ReadoutHeightProperty, value);
    }

    /// <summary>
    /// Indicates whether the gauge valve is open and its live reading is available.
    /// </summary>
    public bool IsReadingEnabled
    {
        get => (bool)GetValue(IsReadingEnabledProperty);
        set => SetValue(IsReadingEnabledProperty, value);
    }

    public bool IsFaulted
    {
        get => (bool)GetValue(IsFaultedProperty);
        set => SetValue(IsFaultedProperty, value);
    }

    public bool ShowValveStatus
    {
        get => (bool)GetValue(ShowValveStatusProperty);
        set => SetValue(ShowValveStatusProperty, value);
    }

    public bool IsValveOpen
    {
        get => (bool)GetValue(IsValveOpenProperty);
        set => SetValue(IsValveOpenProperty, value);
    }

    public bool IsValveFaulted
    {
        get => (bool)GetValue(IsValveFaultedProperty);
        set => SetValue(IsValveFaultedProperty, value);
    }

    /// <summary>
    /// Allows the gauge symbol to issue a toggle command.
    /// </summary>
    public bool CanToggle
    {
        get => (bool)GetValue(CanToggleProperty);
        set => SetValue(CanToggleProperty, value);
    }

    public ICommand? ToggleCommand
    {
        get => (ICommand?)GetValue(ToggleCommandProperty);
        set => SetValue(ToggleCommandProperty, value);
    }
}
