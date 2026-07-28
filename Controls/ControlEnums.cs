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
/// 三通管道中单独支口的朝向。
/// </summary>
public enum TeeOrientation
{
    BranchTop,
    BranchRight,
    BranchBottom,
    BranchLeft
}

/// <summary>
/// APC布局方位：前半部分是文本框相对阀符号的位置，
/// 后半部分是模式按钮相对文本框的位置。
/// </summary>
public enum ApcLayoutPlacement
{
    TopTop = 0,
    TopRight = 1,
    TopBottom = 2,
    TopLeft = 3,
    RightTop = 4,
    RightRight = 5,
    RightBottom = 6,
    RightLeft = 7,
    BottomTop = 8,
    BottomRight = 9,
    BottomBottom = 10,
    BottomLeft = 11,
    LeftTop = 12,
    LeftRight = 13,
    LeftBottom = 14,
    LeftLeft = 15
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
