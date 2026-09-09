using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Small_square_cavity_coating_machine.Controls;

/// <summary>
/// 独立的真空计设备符号。命令与反馈均由外部绑定提供。
/// </summary>
public partial class VacuumGaugeSymbolControl : UserControl
{
    public VacuumGaugeSymbolControl()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty SymbolRotationProperty =
        DependencyProperty.Register(
            nameof(SymbolRotation),
            typeof(double),
            typeof(VacuumGaugeSymbolControl),
            new FrameworkPropertyMetadata(0d));

    public static readonly DependencyProperty IsReadingEnabledProperty =
        DependencyProperty.Register(
            nameof(IsReadingEnabled),
            typeof(bool),
            typeof(VacuumGaugeSymbolControl),
            new FrameworkPropertyMetadata(true));

    public static readonly DependencyProperty IsFaultedProperty =
        DependencyProperty.Register(
            nameof(IsFaulted),
            typeof(bool),
            typeof(VacuumGaugeSymbolControl),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty ShowValveStatusProperty =
        DependencyProperty.Register(
            nameof(ShowValveStatus),
            typeof(bool),
            typeof(VacuumGaugeSymbolControl),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsValveOpenProperty =
        DependencyProperty.Register(
            nameof(IsValveOpen),
            typeof(bool),
            typeof(VacuumGaugeSymbolControl),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsValveFaultedProperty =
        DependencyProperty.Register(
            nameof(IsValveFaulted),
            typeof(bool),
            typeof(VacuumGaugeSymbolControl),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty CanToggleProperty =
        DependencyProperty.Register(
            nameof(CanToggle),
            typeof(bool),
            typeof(VacuumGaugeSymbolControl),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty ToggleCommandProperty =
        DependencyProperty.Register(
            nameof(ToggleCommand),
            typeof(ICommand),
            typeof(VacuumGaugeSymbolControl),
            new FrameworkPropertyMetadata(null));

    public double SymbolRotation
    {
        get => (double)GetValue(SymbolRotationProperty);
        set => SetValue(SymbolRotationProperty, value);
    }

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
