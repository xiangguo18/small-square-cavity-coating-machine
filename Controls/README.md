# 设备控件使用说明

本目录中的控件全部保留 C# 和 XAML 源码，可直接在 Visual Studio 中编辑。

## 控件类型

- 自定义控件：`ValveControl`、`TurbomolecularPumpControl`、`DryPumpControl`、`StraightPipeControl`、`ElbowPipeControl`
- 用户控件：`MfcFlowMeterControl`、`VacuumGaugeControl`
- 自定义控件外观：`Themes/Generic.xaml`
- MVVM 绑定示例：`Views/Controlview.xaml` 和 `ViewModels/ControlViewModel.cs`

## XAML 引用

```xml
xmlns:controls="clr-namespace:Small_square_cavity_coating_machine.Controls"
```

```xml
<controls:ValveControl
    Width="90"
    Height="110"
    DeviceName="前级阀"
    Orientation="Vertical"
    IsOpen="{Binding IsValveOpen}"
    IsInterlockReleased="{Binding IsValveInterlockReleased}"/>

<controls:StraightPipeControl
    Width="180"
    Height="24"
    IsFlowing="{Binding IsVacuumPathFlowing}"
    FlowAnimationDirection="Forward"
    FlowDashLength="9"
    FlowGapLength="6"/>

<controls:VacuumGaugeControl
    Width="290"
    Height="88"
    DisplayName="高真空度计"
    VacuumValue="{Binding ChamberVacuum}"
    SymbolRotation="90"
    ReadoutWidth="204"
    ReadoutHeight="68"/>
```

设备控件内部不会根据鼠标点击自行改变反馈。后续接入控制命令时，应将
`Command` 绑定到 ViewModel，并继续由真实设备反馈更新 `IsOpen`、`IsRunning`
和 `IsFlowing`。

`VacuumGaugeControl.SymbolRotation` 只旋转圆圈和连接杆，圆内图案、名称和读数
不会跟着旋转。`ReadoutWidth` 和 `ReadoutHeight` 可独立调整名称/数值文本框的
内部宽高，文本框中的名称和数值会随文本框等比例缩放，但圆形符号的大小
不会被文本框直接拉伸。控件会根据符号和文本框的组合尺寸，完整缩放到实例的
`Width`、`Height` 内，避免内容越过设计器选框。泵名称由 `DeviceName` 设置，
模板会自动在泵符号右侧逐字竖排。

真空度计可通过 `TextPlacement="Top|Bottom|Left|Right"` 独立选择名称/数值
文本框相对圆形符号的位置；该属性与 `SymbolRotation` 相互独立。

分子泵和干泵均可通过 `LabelPlacement="Top|Bottom|Left|Right"` 独立选择名称
位置。上、下位置为横排并水平居中；左、右位置为竖排并垂直居中。默认位置
为 `Right`。

直管和弯头默认使用相同的 `FlowDashLength` 与 `FlowGapLength`，管段改变长度时
会自动补偿缩放，因此各段管道显示出的虚线长度保持一致。
