using System.Windows;

namespace Small_square_cavity_coating_machine.Controls;

/// <summary>
/// 中心为两个空心同心圆的分子泵符号。
/// </summary>
public class TurbomolecularPumpControl : PumpControlBase
{
    static TurbomolecularPumpControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(TurbomolecularPumpControl),
            new FrameworkPropertyMetadata(typeof(TurbomolecularPumpControl)));
    }
}
