# 单腔室磁控溅射镀膜机上位机

用于单腔室磁控溅射镀膜机的 Windows 上位机控制软件。项目采用 WPF + MVVM 架构，通过 OPC UA 与 PLC 通讯，并使用 SQLite 保存本地运行追溯数据。

当前版本：`v2.1`

## 已实现功能

- **设备与系统状态**：I/O 只读监视、参数设定、PLC 连接状态，以及腔体、真空阀、泵、MFC、APC 阀、样品台和电源的状态显示与控制。
- **动态示意图**：阀门、泵和管道动画均由 PLC 反馈驱动；APC 阀显示当前开度、设定开度及控压/定位状态；样品台转速设定范围为 `0–50 rpm`。
- **工艺配方**：新建配方层、Excel/CSV 导入、UTF-8 BOM CSV 导出、配方层选择和逐层 PLC 下发；下发过程显示反馈、进度和异常结果。
- **历史与追溯**：实时数据曲线、工艺记录、操作记录和报警记录；两条真空曲线使用以 Pa 为单位的十进制对数纵轴；支持 CSV 导出和 SQLite 归档。
- **报警与用户**：报警实时监视、确认/恢复记录、用户登录与权限管理；历史报警页提供蜂鸣器命令及 PLC 反馈状态显示。
- **离线验证**：内置 OPC UA 模拟设备会话与自动化测试，支持不接入现场 PLC 的界面和流程验证。

## 技术栈

| 类别 | 组件 |
| --- | --- |
| 桌面界面 | WPF、CommunityToolkit.Mvvm |
| 目标框架 | .NET 10（`net10.0-windows`） |
| PLC 通讯 | OPC Foundation .NET Standard OPC UA Client |
| 本地存储 | SQLite / Microsoft.Data.Sqlite |
| 曲线绘制 | OxyPlot.Wpf |
| 配方文件 | ExcelDataReader、CSV |

## 开发与运行

### 前提条件

- Windows 10/11
- .NET 10 SDK
- 可选：Visual Studio 2022 及 WPF/.NET 桌面开发工作负载

### 构建与测试

在仓库根目录执行：

```powershell
dotnet restore 'Small square cavity coating machine.csproj'
dotnet build 'Small square cavity coating machine.csproj' -c Release --no-restore
dotnet test 'Tests/SmallSquareCavityCoatingMachine.Tests.csproj' -c Release --no-restore
```

### 离线模拟运行

以下命令启用模拟设备会话和独立的模拟历史库，不会连接或写入现场 PLC：

```powershell
dotnet run --project 'Small square cavity coating machine.csproj' -- --simulate-alarms
```

## PLC 配置与本地数据

应用使用 OPC UA 连接 PLC。连接地址可在界面左下角的 PLC 连接设置中维护，保存路径为：

```text
%LocalAppData%\SmallSquareCavityCoatingMachine\alarms.opcua.json
```

配置字段和默认示例见 [Config/alarms.opcua.example.json](Config/alarms.opcua.example.json)。示例地址仅供配置参考，现场应由 PLC 工程师提供并确认实际端点、节点类型和访问权限。

正常运行时，本地数据保存在：

```text
%LocalAppData%\SmallSquareCavityCoatingMachine\
├─ users.db                    用户与权限数据
├─ equipment-runtime.db        操作审计、参数确认与配方运行记录
├─ alarm-history.db            报警历史
├─ trend-history.db            工艺趋势归档
└─ ProcessTrends\              自动及手动导出的工艺 CSV
```

模拟模式使用名称带 `.simulation` 的独立运行记录和趋势目录，不会混入真实运行数据。

## 项目结构

```text
Controls/       自定义阀门、泵、管道、真空计、APC 和样品台控件
Models/         设备、报警、配方、历史与权限模型
Services/       OPC UA 会话、PLC 控制、SQLite、报警、配方与趋势服务
ViewModels/     MVVM 状态、命令与界面绑定逻辑
Views/          I/O、参数、系统状态、工艺配方、历史和用户界面
Resources/      内嵌点表数据库、图标、字体和图片资源
Tests/          单元、OPC UA 回环及 WPF 呈现测试
docs/           面向现场集成的详细协议说明
```

## 详细文档

- [设备 I/O、参数与 OPC UA 集成](docs/equipment-integration.md)
- [配方导入与逐层 PLC 下发](docs/recipe-integration.md)
- [历史趋势与工艺归档](docs/process-trends.md)
- [报警接入与历史记录](docs/alarm-integration.md)
- [自定义设备控件说明](Controls/README.md)

## 现场使用边界

- PLC 是设备联锁与安全保护的最终执行者；上位机检查仅用于操作引导和风险提示。
- 命令写入成功只表示 PLC 已接收命令；阀门、泵、管道动画和状态灯以 PLC 反馈为准，不代表界面本地点击结果。
- 配方完成以 PLC 完成握手为准，不以界面计时或数据曲线推断实际工艺完成。
- 接入现场前，必须由 PLC 工程师确认 OPC UA 端点、BrowseName/节点、数组长度与数据类型、读写权限、联锁条件和配方握手语义。
- 自动化测试与 `--simulate-alarms` 模式仅使用本地模拟/回环环境；现场联调、设备动作与工艺质量仍需单独验收。
