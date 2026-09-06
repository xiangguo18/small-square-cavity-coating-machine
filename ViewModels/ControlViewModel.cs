using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Small_square_cavity_coating_machine.Models.History;
using Small_square_cavity_coating_machine.Models.Security;
using Small_square_cavity_coating_machine.Services;
using Small_square_cavity_coating_machine.Services.History;
using Small_square_cavity_coating_machine.Services.Security;

namespace Small_square_cavity_coating_machine.ViewModels;

/// <summary>
/// 系统状态页的离线预览数据。
/// 后续接入PLC时可由实时状态服务更新这些属性。
/// </summary>
public partial class ControlViewModel : ObservableObject
{
    private readonly IOperationLogRepository? _operationLogRepository;
    private readonly IAuthorizationService? _authorization;
    private readonly IConfirmationDialogService? _confirmation;

    public ControlViewModel()
    {
    }

    public ControlViewModel(IOperationLogRepository operationLogRepository)
        : this(operationLogRepository, null)
    {
    }

    public ControlViewModel(
        IOperationLogRepository operationLogRepository,
        IAuthorizationService? authorization,
        IConfirmationDialogService? confirmation = null)
    {
        _operationLogRepository = operationLogRepository;
        _authorization = authorization;
        _confirmation = confirmation;
        if (_authorization is not null)
        {
            _authorization.AccessChanged += Authorization_AccessChanged;
        }
    }

    public bool CanOperate =>
        _authorization?.CanOperate(PermissionKey.SystemStatus) ?? true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChamberToMixingPipeIsFlowing))]
    [NotifyPropertyChangedFor(nameof(ArgonMixingPipeIsFlowing))]
    private bool argonUpperValveIsOpen;

    [ObservableProperty]
    private bool argonUpperValveInterlockReleased = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ArgonLowerValveToMixingPipeIsFlowing))]
    [NotifyPropertyChangedFor(nameof(ArgonMfcPipeIsFlowing))]
    private bool argonLowerValveIsOpen;

    [ObservableProperty]
    private bool argonLowerValveInterlockReleased = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChamberToMixingPipeIsFlowing))]
    [NotifyPropertyChangedFor(nameof(NitrogenMixingPipeIsFlowing))]
    private bool nitrogenUpperValveIsOpen;

    [ObservableProperty]
    private bool nitrogenUpperValveInterlockReleased = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NitrogenLowerValveToMixingPipeIsFlowing))]
    [NotifyPropertyChangedFor(nameof(NitrogenMfcPipeIsFlowing))]
    private bool nitrogenLowerValveIsOpen;

    [ObservableProperty]
    private bool nitrogenLowerValveInterlockReleased = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChamberToMixingPipeIsFlowing))]
    [NotifyPropertyChangedFor(nameof(OxygenMixingPipeIsFlowing))]
    private bool oxygenUpperValveIsOpen;

    [ObservableProperty]
    private bool oxygenUpperValveInterlockReleased = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OxygenLowerValveToMixingPipeIsFlowing))]
    [NotifyPropertyChangedFor(nameof(OxygenMfcPipeIsFlowing))]
    private bool oxygenLowerValveIsOpen;

    [ObservableProperty]
    private bool oxygenLowerValveInterlockReleased = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChamberToBypassPipeIsFlowing))]
    [NotifyPropertyChangedFor(nameof(BypassToDryPumpPipeIsFlowing))]
    private bool bypassValveIsOpen;

    [ObservableProperty]
    private bool bypassValveInterlockReleased = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RightForelineToDryPumpPipeIsFlowing))]
    private bool rightForelineValveIsOpen;

    [ObservableProperty]
    private bool rightForelineValveInterlockReleased = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChamberToLowerForelinePipeIsFlowing))]
    private bool lowerForelineValveIsOpen;

    [ObservableProperty]
    private bool lowerForelineValveInterlockReleased = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApcToTurboPipeIsFlowing))]
    [NotifyPropertyChangedFor(nameof(TurboToRightForelinePipeIsFlowing))]
    private bool turboPumpIsRunning;

    [ObservableProperty]
    private bool turboPumpInterlockReleased = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RightForelineToDryPumpPipeIsFlowing))]
    [NotifyPropertyChangedFor(nameof(BypassToDryPumpPipeIsFlowing))]
    private bool dryPumpIsRunning;

    [ObservableProperty]
    private bool dryPumpInterlockReleased = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ArgonMfcPipeIsFlowing))]
    private double argonCurrentFlow = 0.6;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NitrogenMfcPipeIsFlowing))]
    private double nitrogenCurrentFlow = 0.6;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OxygenMfcPipeIsFlowing))]
    private double oxygenCurrentFlow = 0.6;

    [ObservableProperty]
    private double argonSetpointFlow = 1.0;

    [ObservableProperty]
    private double highVacuumValue = 13.6;

    [ObservableProperty]
    private double filmHighVacuumValue = 13.6;

    [ObservableProperty]
    private double forelineVacuumValue = 13.6;

    [ObservableProperty]
    private bool filmGaugeIsReadingEnabled = true;

    [ObservableProperty]
    private bool forelineGaugeIsReadingEnabled = true;

    [ObservableProperty]
    private bool highVacuumGaugeIsReadingEnabled = true;

    [ObservableProperty]
    private bool filmGaugeValveIsOpen;

    [ObservableProperty]
    private bool sampleShutterIsOn;

    [ObservableProperty]
    private bool sampleShutterInterlockReleased = true;

    [ObservableProperty]
    private bool target1ShutterIsOn;

    [ObservableProperty]
    private bool target1ShutterInterlockReleased = true;

    [ObservableProperty]
    private bool target2ShutterIsOn;

    [ObservableProperty]
    private bool target2ShutterInterlockReleased = true;

    [ObservableProperty]
    private double sampleStageCurrentSpeed = 12d;

    [ObservableProperty]
    private double sampleStageSetpointSpeed = 15d;

    [ObservableProperty]
    private bool sampleStageIsForwardRunning;

    [ObservableProperty]
    private bool sampleStageIsReverseRunning;

    [ObservableProperty]
    private bool sampleStageIsStopped;

    [ObservableProperty]
    private bool sampleStageIsRunning;

    [ObservableProperty]
    private bool sampleStageIsFaulted;

    [ObservableProperty]
    private bool systemIsRunning;

    [ObservableProperty]
    private bool systemIsStopped;

    [ObservableProperty]
    private bool systemResetIsActive;

    [ObservableProperty]
    private bool automaticModeIsSelected;

    [ObservableProperty]
    private bool semiAutomaticModeIsSelected;

    [ObservableProperty]
    private bool manualModeIsSelected;

    [ObservableProperty]
    private bool vacuumingIsSelected;

    [ObservableProperty]
    private bool ventingIsSelected;

    [ObservableProperty]
    private bool pressureHoldingIsSelected;

    [ObservableProperty]
    private double heaterCurrentTemperature = 186.5;

    [ObservableProperty]
    private double heaterSetpointTemperature = 200d;

    [ObservableProperty]
    private bool heaterIsRunning = true;

    [ObservableProperty]
    private bool heaterInterlockReleased = true;

    [ObservableProperty]
    private double power1Setpoint = 1000d;

    [ObservableProperty]
    private double power1MeasuredPower = 860d;

    [ObservableProperty]
    private double power1MeasuredVoltage = 480d;

    [ObservableProperty]
    private double power1MeasuredCurrent = 1.8d;

    [ObservableProperty]
    private bool power1IsRunning = true;

    [ObservableProperty]
    private bool power1InterlockReleased = true;

    [ObservableProperty]
    private double power2Setpoint = 1000d;

    [ObservableProperty]
    private double power2MeasuredPower = 860d;

    [ObservableProperty]
    private double power2MeasuredVoltage = 480d;

    [ObservableProperty]
    private double power2MeasuredCurrent = 1.8d;

    [ObservableProperty]
    private bool power2IsRunning = true;

    [ObservableProperty]
    private bool power2InterlockReleased = true;

    [ObservableProperty]
    private bool apcIsPositioningMode = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChamberToApcPipeIsFlowing))]
    [NotifyPropertyChangedFor(nameof(ApcToTurboPipeIsFlowing))]
    private double apcCurrentPosition = 45d;

    [ObservableProperty]
    private double apcPositionSetpoint = 55d;

    [ObservableProperty]
    private double apcCurrentPressure = 1.2d;

    [ObservableProperty]
    private double apcPressureSetpoint = 1.5d;

    [ObservableProperty]
    private bool apcIsOpen = true;

    [ObservableProperty]
    private bool apcInterlockReleased = true;

    public bool ChamberToApcPipeIsFlowing => IsPositiveFinite(ApcCurrentPosition);

    public bool ApcToTurboPipeIsFlowing =>
        ChamberToApcPipeIsFlowing && TurboPumpIsRunning;

    public bool TurboToRightForelinePipeIsFlowing => TurboPumpIsRunning;

    public bool RightForelineToDryPumpPipeIsFlowing =>
        RightForelineValveIsOpen && DryPumpIsRunning;

    public bool ChamberToBypassPipeIsFlowing => BypassValveIsOpen;

    public bool BypassToDryPumpPipeIsFlowing =>
        BypassValveIsOpen && DryPumpIsRunning;

    public bool ChamberToLowerForelinePipeIsFlowing => LowerForelineValveIsOpen;

    public bool ChamberToMixingPipeIsFlowing =>
        ArgonUpperValveIsOpen
        || NitrogenUpperValveIsOpen
        || OxygenUpperValveIsOpen;

    public bool ArgonMixingPipeIsFlowing => ArgonUpperValveIsOpen;

    public bool ArgonLowerValveToMixingPipeIsFlowing => ArgonLowerValveIsOpen;

    public bool ArgonMfcPipeIsFlowing =>
        ArgonLowerValveIsOpen && IsPositiveFinite(ArgonCurrentFlow);

    public bool NitrogenMixingPipeIsFlowing => NitrogenUpperValveIsOpen;

    public bool NitrogenLowerValveToMixingPipeIsFlowing => NitrogenLowerValveIsOpen;

    public bool NitrogenMfcPipeIsFlowing =>
        NitrogenLowerValveIsOpen && IsPositiveFinite(NitrogenCurrentFlow);

    public bool OxygenMixingPipeIsFlowing => OxygenUpperValveIsOpen;

    public bool OxygenLowerValveToMixingPipeIsFlowing => OxygenLowerValveIsOpen;

    public bool OxygenMfcPipeIsFlowing =>
        OxygenLowerValveIsOpen && IsPositiveFinite(OxygenCurrentFlow);

    private static bool IsPositiveFinite(double value) =>
        double.IsFinite(value) && value > 0d;

    [RelayCommand]
    private void ToggleArgonUpperValve()
    {
        if (!EnsureCanOperate()) return;
        if (TryQueuePartToggle("Cathode1GasValve", ArgonUpperValveIsOpen, 29, 30, "阴极1气阀")) return;
        ArgonUpperValveIsOpen = !ArgonUpperValveIsOpen;
        LogOperation("Ar前级阀（上）", ArgonUpperValveIsOpen ? "打开阀门" : "关闭阀门");
    }

    [RelayCommand]
    private void ToggleArgonLowerValve()
    {
        if (!EnsureCanOperate()) return;
        if (TryQueuePartToggle("ArgonLowerValve", ArgonLowerValveIsOpen, 27, 28, "Ar气阀")) return;
        ArgonLowerValveIsOpen = !ArgonLowerValveIsOpen;
        LogOperation("Ar前级阀（下）", ArgonLowerValveIsOpen ? "打开阀门" : "关闭阀门");
    }

    [RelayCommand]
    private void ToggleNitrogenUpperValve()
    {
        if (!EnsureCanOperate()) return;
        if (TryQueuePartToggle("Cathode2GasValve", NitrogenUpperValveIsOpen, 33, 34, "阴极2气阀")) return;
        NitrogenUpperValveIsOpen = !NitrogenUpperValveIsOpen;
        LogOperation("N₂前级阀（上）", NitrogenUpperValveIsOpen ? "打开阀门" : "关闭阀门");
    }

    [RelayCommand]
    private void ToggleNitrogenLowerValve()
    {
        if (!EnsureCanOperate()) return;
        if (TryQueuePartToggle("NitrogenLowerValve", NitrogenLowerValveIsOpen, 35, 36, "N₂气阀")) return;
        NitrogenLowerValveIsOpen = !NitrogenLowerValveIsOpen;
        LogOperation("N₂前级阀（下）", NitrogenLowerValveIsOpen ? "打开阀门" : "关闭阀门");
    }

    [RelayCommand]
    private void ToggleOxygenUpperValve()
    {
        if (!EnsureCanOperate()) return;
        if (TryQueuePartToggle("Cathode3GasValve", OxygenUpperValveIsOpen, 37, 38, "阴极3气阀")) return;
        OxygenUpperValveIsOpen = !OxygenUpperValveIsOpen;
        LogOperation("O₂前级阀（上）", OxygenUpperValveIsOpen ? "打开阀门" : "关闭阀门");
    }

    [RelayCommand]
    private void ToggleOxygenLowerValve()
    {
        if (!EnsureCanOperate()) return;
        if (TryQueuePartToggle("OxygenLowerValve", OxygenLowerValveIsOpen, 31, 32, "O₂气阀")) return;
        OxygenLowerValveIsOpen = !OxygenLowerValveIsOpen;
        LogOperation("O₂前级阀（下）", OxygenLowerValveIsOpen ? "打开阀门" : "关闭阀门");
    }

    [RelayCommand]
    private void ToggleBypassValve()
    {
        if (!EnsureCanOperate()) return;
        if (TryQueuePartToggle("BypassValve", BypassValveIsOpen, 41, 42, "旁抽阀")) return;
        BypassValveIsOpen = !BypassValveIsOpen;
        LogOperation("旁抽阀", BypassValveIsOpen ? "打开阀门" : "关闭阀门");
    }

    [RelayCommand]
    private void ToggleRightForelineValve()
    {
        if (!EnsureCanOperate()) return;
        if (TryQueuePartToggle("ForelineValve", RightForelineValveIsOpen, 43, 44, "前级阀")) return;
        RightForelineValveIsOpen = !RightForelineValveIsOpen;
        LogOperation("右侧前级阀", RightForelineValveIsOpen ? "打开阀门" : "关闭阀门");
    }

    [RelayCommand]
    private void ToggleLowerForelineValve()
    {
        if (!EnsureCanOperate()) return;
        if (!LowerForelineValveIsOpen && !ConfirmAction("放气阀操作确认", "确定打开放气阀？")) return;
        if (TryQueuePartToggle("VentValve", LowerForelineValveIsOpen, 45, 46, "放气阀")) return;
        LowerForelineValveIsOpen = !LowerForelineValveIsOpen;
        LogOperation("下部前级阀", LowerForelineValveIsOpen ? "打开阀门" : "关闭阀门");
    }

    [RelayCommand]
    private void ToggleTurboPump()
    {
        if (!EnsureCanOperate()) return;
        if (!TurboPumpIsRunning && !ConfirmAction("分子泵操作确认", "确定启动分子泵？")) return;
        if (TryQueuePartToggle("TurboPump", TurboPumpIsRunning, 2, 3, "分子泵")) return;
        TurboPumpIsRunning = !TurboPumpIsRunning;
        LogOperation("分子泵", TurboPumpIsRunning ? "启动" : "停止");
    }

    [RelayCommand]
    private void ToggleDryPump()
    {
        if (!EnsureCanOperate()) return;
        if (TryQueuePartToggle("DryPump", DryPumpIsRunning, 0, 1, "干泵")) return;
        DryPumpIsRunning = !DryPumpIsRunning;
        LogOperation("干泵", DryPumpIsRunning ? "启动" : "停止");
    }

    [RelayCommand]
    private void ToggleFilmGauge()
    {
        if (!EnsureCanOperate()) return;
        if (TryQueuePartToggle("FilmGaugeValve", FilmGaugeValveIsOpen, 39, 40, "薄膜真空度计阀")) return;
        FilmGaugeIsReadingEnabled = !FilmGaugeIsReadingEnabled;
        LogOperation("薄膜高真空度计", FilmGaugeIsReadingEnabled ? "启用读数" : "停止读数");
    }

    [RelayCommand]
    private void SetSampleStageSpeed(double value)
    {
        if (!EnsureCanOperate()) return;
        if (TryQueueSetpoint(3, value)) return;
        SampleStageSetpointSpeed = value;
        LogOperation("样品台", "设置转速", $"{value:0.###} rpm");
    }

    [RelayCommand]
    private void StartSampleStageForward()
    {
        if (!EnsureCanOperate()) return;
        if (TryQueueStageDirection("Forward", 10)) return;
        SampleStageIsForwardRunning = true;
        SampleStageIsReverseRunning = false;
        SampleStageIsStopped = false;
        LogOperation("样品台", "启动正转", $"{SampleStageSetpointSpeed:0.###} rpm");
    }

    [RelayCommand]
    private void StartSampleStageReverse()
    {
        if (!EnsureCanOperate()) return;
        if (TryQueueStageDirection("Reverse", 11)) return;
        SampleStageIsForwardRunning = false;
        SampleStageIsReverseRunning = true;
        SampleStageIsStopped = false;
        LogOperation("样品台", "启动反转", $"{SampleStageSetpointSpeed:0.###} rpm");
    }

    [RelayCommand]
    private void StopSampleStage()
    {
        if (!EnsureCanOperate()) return;
        if (TryQueueStageDirection("Stop", 12)) return;
        SampleStageIsForwardRunning = false;
        SampleStageIsReverseRunning = false;
        SampleStageIsStopped = true;
        LogOperation("样品台", "停止");
    }

    [RelayCommand]
    private void StartSystem()
    {
        if (!EnsureCanOperate()) return;
        if (TryQueueSystemCommand("Start")) return;
        SystemIsRunning = true;
        SystemIsStopped = false;
        SystemResetIsActive = false;
        LogOperation("系统控制", "开启");
    }

    [RelayCommand]
    private void StopSystem()
    {
        if (!EnsureCanOperate()) return;
        if (TryQueueSystemCommand("Stop")) return;
        SystemIsRunning = false;
        SystemIsStopped = true;
        SystemResetIsActive = false;
        LogOperation("系统控制", "停止");
    }

    [RelayCommand]
    private void ResetSystem()
    {
        if (!EnsureCanOperate()) return;
        if (TryQueueSystemCommand("Reset")) return;
        SystemIsRunning = false;
        SystemIsStopped = false;
        SystemResetIsActive = true;
        ClearModeAndWorkflowAndStageSelection();
        LogOperation("系统控制", "复位");
    }

    [RelayCommand]
    private void SelectAutomaticMode()
    {
        if (!EnsureCanOperate()) return;
        if (!ConfirmAction("运行模式确认", "确定切换到自动模式？")) return;
        if (TryQueueSystemCommand("Auto")) return;
        AutomaticModeIsSelected = true;
        SemiAutomaticModeIsSelected = false;
        ManualModeIsSelected = false;
        LogOperation("系统控制", "切换模式", "自动");
    }

    [RelayCommand]
    private void SelectSemiAutomaticMode()
    {
        if (!EnsureCanOperate()) return;
        if (TryQueueSystemCommand("Semi")) return;
        AutomaticModeIsSelected = false;
        SemiAutomaticModeIsSelected = true;
        ManualModeIsSelected = false;
        LogOperation("系统控制", "切换模式", "半自动");
    }

    [RelayCommand]
    private void SelectManualMode()
    {
        if (!EnsureCanOperate()) return;
        if (TryQueueSystemCommand("Manual")) return;
        AutomaticModeIsSelected = false;
        SemiAutomaticModeIsSelected = false;
        ManualModeIsSelected = true;
        LogOperation("系统控制", "切换模式", "手动");
    }

    [RelayCommand]
    private void StartVacuum()
    {
        if (!EnsureCanOperate()) return;
        if (!ConfirmAction("真空控制确认", "确定执行抽真空流程？")) return;
        if (TryQueueWorkflowCommand(980, "抽真空")) return;
        ToggleWorkflowSelection(980);
        LogOperation("系统控制", "抽真空");
    }

    [RelayCommand]
    private void BreakVacuum()
    {
        if (!EnsureCanOperate()) return;
        if (!ConfirmAction("真空控制确认", "确定执行破真空流程？")) return;
        if (TryQueueWorkflowCommand(981, "破真空")) return;
        ToggleWorkflowSelection(981);
        LogOperation("系统控制", "破真空");
    }

    [RelayCommand]
    private void HoldPressure()
    {
        if (!EnsureCanOperate()) return;
        if (!ConfirmAction("真空控制确认", "确定执行保压流程？")) return;
        if (TryQueueWorkflowCommand(982, "保压")) return;
        ToggleWorkflowSelection(982);
        LogOperation("系统控制", "保压");
    }

    [RelayCommand]
    private void ToggleSampleShutter()
    {
        if (!EnsureCanOperate()) return;
        if (TryQueuePartToggle("SampleShutter", SampleShutterIsOn, 13, 14, "样品挡板")) return;
        SampleShutterIsOn = !SampleShutterIsOn;
        LogOperation("样品挡板", SampleShutterIsOn ? "打开" : "关闭");
    }

    [RelayCommand]
    private void ToggleTarget1Shutter()
    {
        if (!EnsureCanOperate()) return;
        if (TryQueuePartToggle("Target1Shutter", Target1ShutterIsOn, 15, 16, "靶1挡板")) return;
        Target1ShutterIsOn = !Target1ShutterIsOn;
        LogOperation("靶1挡板", Target1ShutterIsOn ? "打开" : "关闭");
    }

    [RelayCommand]
    private void ToggleTarget2Shutter()
    {
        if (!EnsureCanOperate()) return;
        if (TryQueuePartToggle("Target2Shutter", Target2ShutterIsOn, 17, 18, "靶2挡板")) return;
        Target2ShutterIsOn = !Target2ShutterIsOn;
        LogOperation("靶2挡板", Target2ShutterIsOn ? "打开" : "关闭");
    }

    [RelayCommand]
    private void SetArgonFlow(double value)
    {
        if (!EnsureCanOperate()) return;
        if (TryQueueSetpoint(7, value)) return;
        ArgonSetpointFlow = value;
        LogOperation("Ar MFC", "设置流量", $"{value:0.###} sccm");
    }

    [RelayCommand]
    private void SetNitrogenFlow(double value)
    {
        if (!EnsureCanOperate()) return;
        if (TryQueueSetpoint(8, value)) return;
        NitrogenSetpointFlow = value;
        LogOperation("N₂ MFC", "设置流量", $"{value:0.###} sccm");
    }

    [RelayCommand]
    private void SetOxygenFlow(double value)
    {
        if (!EnsureCanOperate()) return;
        if (TryQueueSetpoint(9, value)) return;
        OxygenSetpointFlow = value;
        LogOperation("O₂ MFC", "设置流量", $"{value:0.###} sccm");
    }

    [RelayCommand]
    private void ToggleHeater()
    {
        if (!EnsureCanOperate()) return;
        if (TryQueuePartToggle("Heater", HeaterIsRunning, 8, 9, "加热")) return;
        HeaterIsRunning = !HeaterIsRunning;
        LogOperation("加热", HeaterIsRunning ? "启动" : "停止");
    }

    [RelayCommand]
    private void SetHeaterTemperature(double value)
    {
        if (!EnsureCanOperate()) return;
        if (TryQueueSetpoint(2, value)) return;
        HeaterSetpointTemperature = value;
        LogOperation("加热", "设置目标温度", $"{value:0.###} ℃");
    }

    [RelayCommand]
    private void TogglePower1()
    {
        if (!EnsureCanOperate()) return;
        if (TryQueuePartToggle("Power1", Power1IsRunning, 21, 22, "电源1")) return;
        Power1IsRunning = !Power1IsRunning;
        LogOperation("电源1", Power1IsRunning ? "启动" : "停止");
    }

    [RelayCommand]
    private void SetPower1(double value)
    {
        if (!EnsureCanOperate()) return;
        if (TryQueueSetpoint(21, value)) return;
        Power1Setpoint = value;
        LogOperation("电源1", "设置功率", $"{value:0.###} W");
    }

    [RelayCommand]
    private void TogglePower2()
    {
        if (!EnsureCanOperate()) return;
        if (TryQueuePartToggle("Power2", Power2IsRunning, 23, 24, "电源2")) return;
        Power2IsRunning = !Power2IsRunning;
        LogOperation("电源2", Power2IsRunning ? "启动" : "停止");
    }

    [RelayCommand]
    private void SetPower2(double value)
    {
        if (!EnsureCanOperate()) return;
        if (TryQueueSetpoint(24, value)) return;
        Power2Setpoint = value;
        LogOperation("电源2", "设置功率", $"{value:0.###} W");
    }

    [RelayCommand]
    private void ToggleApcMode()
    {
        if (!EnsureCanOperate()) return;
        if (_controlService is not null) { ControlStatusText = "第一阶段APC固定为定位模式"; return; }
        ApcIsPositioningMode = !ApcIsPositioningMode;
        LogOperation("APC阀", "切换控制模式", ApcIsPositioningMode ? "定位模式" : "控压模式");
    }

    [RelayCommand]
    private void SetApcPosition(double value)
    {
        if (!EnsureCanOperate()) return;
        if (!double.IsFinite(value) || value < 0d || value > 100d)
        {
            ControlStatusText = "APC开度范围：0～100 %";
            return;
        }
        var action = value == 0d ? "关闭" : "开启";
        if (!ConfirmAction("APC阀设定确认", $"确定将 APC 开度设为 {value:0.###}%（{action}）？")) return;
        if (TryQueueApcPosition(value)) return;
        ApcPositionSetpoint = value;
        LogOperation("APC阀", "设置位置", $"{value:0.###} %");
    }

    [RelayCommand]
    private void SetApcPressure(double value)
    {
        if (!EnsureCanOperate()) return;
        if (_controlService is not null) { ControlStatusText = "APC控压模式地址尚未提供，本次未写入"; return; }
        ApcPressureSetpoint = value;
        LogOperation("APC阀", "设置压力", $"{value:0.###} Pa");
    }

    private void LogOperation(string target, string action, string setValue = "")
    {
        _operationLogRepository?.Add(new OperationLogRecord(
            DateTimeOffset.Now,
            _authorization?.CurrentUserName ?? "本地模拟用户",
            target,
            action,
            setValue,
            true,
            false,
            string.Empty,
            true));
    }

    private bool EnsureCanOperate() =>
        _authorization?.TryAuthorize(PermissionKey.SystemStatus) ?? true;

    private bool ConfirmAction(string title, string message) =>
        _confirmation?.Confirm(title, message) ?? true;

    private void Authorization_AccessChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CanOperate));
    }

    private void NotifyCommandCanExecuteChanged()
    {
        ToggleArgonUpperValveCommand.NotifyCanExecuteChanged();
        ToggleArgonLowerValveCommand.NotifyCanExecuteChanged();
        ToggleNitrogenUpperValveCommand.NotifyCanExecuteChanged();
        ToggleNitrogenLowerValveCommand.NotifyCanExecuteChanged();
        ToggleOxygenUpperValveCommand.NotifyCanExecuteChanged();
        ToggleOxygenLowerValveCommand.NotifyCanExecuteChanged();
        ToggleBypassValveCommand.NotifyCanExecuteChanged();
        ToggleRightForelineValveCommand.NotifyCanExecuteChanged();
        ToggleLowerForelineValveCommand.NotifyCanExecuteChanged();
        ToggleTurboPumpCommand.NotifyCanExecuteChanged();
        ToggleDryPumpCommand.NotifyCanExecuteChanged();
        ToggleFilmGaugeCommand.NotifyCanExecuteChanged();
        SetSampleStageSpeedCommand.NotifyCanExecuteChanged();
        StartSampleStageForwardCommand.NotifyCanExecuteChanged();
        StartSampleStageReverseCommand.NotifyCanExecuteChanged();
        StopSampleStageCommand.NotifyCanExecuteChanged();
        StartSystemCommand.NotifyCanExecuteChanged();
        StopSystemCommand.NotifyCanExecuteChanged();
        ResetSystemCommand.NotifyCanExecuteChanged();
        SelectAutomaticModeCommand.NotifyCanExecuteChanged();
        SelectSemiAutomaticModeCommand.NotifyCanExecuteChanged();
        SelectManualModeCommand.NotifyCanExecuteChanged();
        StartVacuumCommand.NotifyCanExecuteChanged();
        BreakVacuumCommand.NotifyCanExecuteChanged();
        HoldPressureCommand.NotifyCanExecuteChanged();
        ToggleSampleShutterCommand.NotifyCanExecuteChanged();
        ToggleTarget1ShutterCommand.NotifyCanExecuteChanged();
        ToggleTarget2ShutterCommand.NotifyCanExecuteChanged();
        ToggleHeaterCommand.NotifyCanExecuteChanged();
        SetHeaterTemperatureCommand.NotifyCanExecuteChanged();
        TogglePower1Command.NotifyCanExecuteChanged();
        SetPower1Command.NotifyCanExecuteChanged();
        TogglePower2Command.NotifyCanExecuteChanged();
        SetPower2Command.NotifyCanExecuteChanged();
        ToggleApcModeCommand.NotifyCanExecuteChanged();
        SetApcPositionCommand.NotifyCanExecuteChanged();
        SetApcPressureCommand.NotifyCanExecuteChanged();
    }
}
