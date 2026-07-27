namespace Small_square_cavity_coating_machine.Controls;

/// <summary>
/// 管道内介质的显示流向。
/// </summary>
public enum PipeFlowDirection
{
    Forward,
    Reverse
}

/// <summary>
/// 90°弯头的两个出口方向。
/// </summary>
public enum ElbowOrientation
{
    TopRight,
    BottomRight,
    BottomLeft,
    TopLeft
}

/// <summary>
/// 泵名称相对泵符号的显示位置。
/// </summary>
public enum PumpLabelPlacement
{
    Top,
    Bottom,
    Left,
    Right
}

/// <summary>
/// 真空度计文本框相对圆形符号的显示位置。
/// </summary>
public enum GaugeTextPlacement
{
    Top,
    Bottom,
    Left,
    Right
}
