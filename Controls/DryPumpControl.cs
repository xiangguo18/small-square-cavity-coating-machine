using System.Windows;

namespace Small_square_cavity_coating_machine.Controls;

/// <summary>
/// 中心为实心圆的干泵符号。
/// </summary>
public class DryPumpControl : PumpControlBase
{
    static DryPumpControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(DryPumpControl),
            new FrameworkPropertyMetadata(typeof(DryPumpControl)));
    }
}
