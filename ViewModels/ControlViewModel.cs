using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Small_square_cavity_coating_machine.Models.History;
using Small_square_cavity_coating_machine.Models.Security;
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

    public ControlViewModel()
    {
    }

    public ControlViewModel(IOperationLogRepository operationLogRepository)
        : this(operationLogRepository, null)
    {
    }

    public ControlViewModel(
        IOperationLogRepository operationLogRepository,
        IAuthorizationService? authorization)
    {
        _operationLogRepository = operationLogRepository;
        _authorization = authorization;
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
    [NotifyPropertyChangedFor(nameof(ArgonValvePairPipeIsFlowing))]
    private bool argonUpperValveIsOpen;

    [ObservableProperty]
    private bool argonUpperValveInterlockReleased = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ArgonValvePairPipeIsFlowing))]
    [NotifyPropertyChangedFor(nameof(ArgonMfcPipeIsFlowing))]
    private bool argonLowerValveIsOpen;

    [ObservableProperty]
    private bool argonLowerValveInterlockReleased = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChamberToMixingPipeIsFlowing))]
    [NotifyPropertyChangedFor(nameof(NitrogenMixingPipeIsFlowing))]
    [NotifyPropertyChangedFor(nameof(NitrogenValvePairPipeIsFlowing))]
    private bool nitrogenUpperValveIsOpen;

    [ObservableProperty]
    private bool nitrogenUpperValveInterlockReleased = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NitrogenValvePairPipeIsFlowing))]
    [NotifyPropertyChangedFor(nameof(NitrogenMfcPipeIsFlowing))]
    private bool nitrogenLowerValveIsOpen;

    [ObservableProperty]
    private bool nitrogenLowerValveInterlockReleased = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChamberToMixingPipeIsFlowing))]
    [NotifyPropertyChangedFor(nameof(OxygenMixingPipeIsFlowing))]
    [NotifyPropertyChangedFor(nameof(OxygenValvePairPipeIsFlowing))]
    private bool oxygenUpperValveIsOpen;

    [ObservableProperty]
    private bool oxygenUpperValveInterlockReleased = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OxygenValvePairPipeIsFlowing))]
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
    [NotifyPropertyChangedFor(nameof(TurboToRightForelinePipeIsFlowing))]
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
    private bool systemIsRunning;

    [ObservableProperty]
    private bool systemIsStopped = true;

    [ObservableProperty]
    private bool automaticModeIsSelected;

    [ObservableProperty]
    private bool semiAutomaticModeIsSelected;

    [ObservableProperty]
    private bool manualModeIsSelected = true;

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

    public bool TurboToRightForelinePipeIsFlowing =>
        TurboPumpIsRunning && RightForelineValveIsOpen;

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

    public bool ArgonValvePairPipeIsFlowing =>
        ArgonUpperValveIsOpen && ArgonLowerValveIsOpen;

    public bool ArgonMfcPipeIsFlowing =>
        ArgonLowerValveIsOpen && IsPositiveFinite(ArgonCurrentFlow);

    public bool NitrogenMixingPipeIsFlowing => NitrogenUpperValveIsOpen;

    public bool NitrogenValvePairPipeIsFlowing =>
        NitrogenUpperValveIsOpen && NitrogenLowerValveIsOpen;

    public bool NitrogenMfcPipeIsFlowing =>
        NitrogenLowerValveIsOpen && IsPositiveFinite(NitrogenCurrentFlow);

    public bool OxygenMixingPipeIsFlowing => OxygenUpperValveIsOpen;

    public bool OxygenValvePairPipeIsFlowing =>
        OxygenUpperValveIsOpen && OxygenLowerValveIsOpen;

    public bool OxygenMfcPipeIsFlowing =>
        OxygenLowerValveIsOpen && IsPositiveFinite(OxygenCurrentFlow);

    private static bool IsPositiveFinite(double value) =>
        double.IsFinite(value) && value > 0d;

    [RelayCommand]
    private void ToggleArgonUpperValve()
    {
        if (!EnsureCanOperate()) return;
        ArgonUpperValveIsOpen = !ArgonUpperValveIsOpen;
        LogOperation("Ar前级阀（上）", ArgonUpperValveIsOpen ? "打开阀门" : "关闭阀门");
    }

    [RelayCommand]
    private void ToggleArgonLowerValve()
    {
        if (!EnsureCanOperate()) return;
        ArgonLowerValveIsOpen = !ArgonLowerValveIsOpen;
        LogOperation("Ar前级阀（下）", ArgonLowerValveIsOpen ? "打开阀门" : "关闭阀门");
    }

    [RelayCommand]
    private void ToggleNitrogenUpperValve()
    {
        if (!EnsureCanOperate()) return;
        NitrogenUpperValveIsOpen = !NitrogenUpperValveIsOpen;
        LogOperation("N₂前级阀（上）", NitrogenUpperValveIsOpen ? "打开阀门" : "关闭阀门");
    }

    [RelayCommand]
    private void ToggleNitrogenLowerValve()
    {
        if (!EnsureCanOperate()) return;
        NitrogenLowerValveIsOpen = !NitrogenLowerValveIsOpen;
        LogOperation("N₂前级阀（下）", NitrogenLowerValveIsOpen ? "打开阀门" : "关闭阀门");
    }

    [RelayCommand]
    private void ToggleOxygenUpperValve()
    {
        if (!EnsureCanOperate()) return;
        OxygenUpperValveIsOpen = !OxygenUpperValveIsOpen;
        LogOperation("O₂前级阀（上）", OxygenUpperValveIsOpen ? "打开阀门" : "关闭阀门");
    }

    [RelayCommand]
    private void ToggleOxygenLowerValve()
    {
        if (!EnsureCanOperate()) return;
        OxygenLowerValveIsOpen = !OxygenLowerValveIsOpen;
        LogOperation("O₂前级阀（下）", OxygenLowerValveIsOpen ? "打开阀门" : "关闭阀门");
    }

    [RelayCommand]
    private void ToggleBypassValve()
    {
        if (!EnsureCanOperate()) return;
        BypassValveIsOpen = !BypassValveIsOpen;
        LogOperation("旁抽阀", BypassValveIsOpen ? "打开阀门" : "关闭阀门");
    }

    [RelayCommand]
    private void ToggleRightForelineValve()
    {
        if (!EnsureCanOperate()) return;
        RightForelineValveIsOpen = !RightForelineValveIsOpen;
        LogOperation("右侧前级阀", RightForelineValveIsOpen ? "打开阀门" : "关闭阀门");
    }

    [RelayCommand]
    private void ToggleLowerForelineValve()
    {
        if (!EnsureCanOperate()) return;
        LowerForelineValveIsOpen = !LowerForelineValveIsOpen;
        LogOperation("下部前级阀", LowerForelineValveIsOpen ? "打开阀门" : "关闭阀门");
    }

    [RelayCommand]
    private void ToggleTurboPump()
    {
        if (!EnsureCanOperate()) return;
        TurboPumpIsRunning = !TurboPumpIsRunning;
        LogOperation("分子泵", TurboPumpIsRunning ? "启动" : "停止");
    }

    [RelayCommand]
    private void ToggleDryPump()
    {
        if (!EnsureCanOperate()) return;
        DryPumpIsRunning = !DryPumpIsRunning;
        LogOperation("干泵", DryPumpIsRunning ? "启动" : "停止");
    }

    [RelayCommand]
    private void ToggleFilmGauge()
    {
        if (!EnsureCanOperate()) return;
        FilmGaugeIsReadingEnabled = !FilmGaugeIsReadingEnabled;
        LogOperation("薄膜高真空度计", FilmGaugeIsReadingEnabled ? "启用读数" : "停止读数");
    }

    [RelayCommand]
    private void SetSampleStageSpeed(double value)
    {
        if (!EnsureCanOperate()) return;
        SampleStageSetpointSpeed = value;
        LogOperation("样品台", "设置转速", $"{value:0.###} rpm");
    }

    [RelayCommand]
    private void StartSampleStageForward()
    {
        if (!EnsureCanOperate()) return;
        SampleStageIsForwardRunning = true;
        SampleStageIsReverseRunning = false;
        LogOperation("样品台", "启动正转", $"{SampleStageSetpointSpeed:0.###} rpm");
    }

    [RelayCommand]
    private void StartSampleStageReverse()
    {
        if (!EnsureCanOperate()) return;
        SampleStageIsForwardRunning = false;
        SampleStageIsReverseRunning = true;
        LogOperation("样品台", "启动反转", $"{SampleStageSetpointSpeed:0.###} rpm");
    }

    [RelayCommand]
    private void StartSystem()
    {
        if (!EnsureCanOperate()) return;
        SystemIsRunning = true;
        SystemIsStopped = false;
        LogOperation("系统控制", "开启");
    }

    [RelayCommand]
    private void StopSystem()
    {
        if (!EnsureCanOperate()) return;
        SystemIsRunning = false;
        SystemIsStopped = true;
        LogOperation("系统控制", "停止");
    }

    [RelayCommand]
    private void ResetSystem()
    {
        if (!EnsureCanOperate()) return;
        SystemIsRunning = false;
        SystemIsStopped = true;
        AutomaticModeIsSelected = false;
        SemiAutomaticModeIsSelected = false;
        ManualModeIsSelected = true;
        VacuumingIsSelected = false;
        VentingIsSelected = false;
        PressureHoldingIsSelected = false;
        SampleStageIsForwardRunning = false;
        SampleStageIsReverseRunning = false;
        LogOperation("系统控制", "复位");
    }

    [RelayCommand]
    private void SelectAutomaticMode()
    {
        if (!EnsureCanOperate()) return;
        AutomaticModeIsSelected = true;
        SemiAutomaticModeIsSelected = false;
        ManualModeIsSelected = false;
        LogOperation("系统控制", "切换模式", "自动");
    }

    [RelayCommand]
    private void SelectSemiAutomaticMode()
    {
        if (!EnsureCanOperate()) return;
        AutomaticModeIsSelected = false;
        SemiAutomaticModeIsSelected = true;
        ManualModeIsSelected = false;
        LogOperation("系统控制", "切换模式", "半自动");
    }

    [RelayCommand]
    private void SelectManualMode()
    {
        if (!EnsureCanOperate()) return;
        AutomaticModeIsSelected = false;
        SemiAutomaticModeIsSelected = false;
        ManualModeIsSelected = true;
        LogOperation("系统控制", "切换模式", "手动");
    }

    [RelayCommand]
    private void StartVacuum()
    {
        if (!EnsureCanOperate()) return;
        VacuumingIsSelected = true;
        VentingIsSelected = false;
        PressureHoldingIsSelected = false;
        LogOperation("系统控制", "抽真空");
    }

    [RelayCommand]
    private void BreakVacuum()
    {
        if (!EnsureCanOperate()) return;
        VacuumingIsSelected = false;
        VentingIsSelected = true;
        PressureHoldingIsSelected = false;
        LogOperation("系统控制", "破真空");
    }

    [RelayCommand]
    private void HoldPressure()
    {
        if (!EnsureCanOperate()) return;
        VacuumingIsSelected = false;
        VentingIsSelected = false;
        PressureHoldingIsSelected = true;
        LogOperation("系统控制", "保压");
    }

    [RelayCommand]
    private void ToggleSampleShutter()
    {
        if (!EnsureCanOperate()) return;
        SampleShutterIsOn = !SampleShutterIsOn;
        LogOperation("样品挡板", SampleShutterIsOn ? "打开" : "关闭");
    }

    [RelayCommand]
    private void ToggleTarget1Shutter()
    {
        if (!EnsureCanOperate()) return;
        Target1ShutterIsOn = !Target1ShutterIsOn;
        LogOperation("靶1挡板", Target1ShutterIsOn ? "打开" : "关闭");
    }

    [RelayCommand]
    private void ToggleTarget2Shutter()
    {
        if (!EnsureCanOperate()) return;
        Target2ShutterIsOn = !Target2ShutterIsOn;
        LogOperation("靶2挡板", Target2ShutterIsOn ? "打开" : "关闭");
    }

    [RelayCommand]
    private void ToggleHeater()
    {
        if (!EnsureCanOperate()) return;
        HeaterIsRunning = !HeaterIsRunning;
        LogOperation("加热", HeaterIsRunning ? "启动" : "停止");
    }

    [RelayCommand]
    private void SetHeaterTemperature(double value)
    {
        if (!EnsureCanOperate()) return;
        HeaterSetpointTemperature = value;
        LogOperation("加热", "设置目标温度", $"{value:0.###} ℃");
    }

    [RelayCommand]
    private void TogglePower1()
    {
        if (!EnsureCanOperate()) return;
        Power1IsRunning = !Power1IsRunning;
        LogOperation("电源1", Power1IsRunning ? "启动" : "停止");
    }

    [RelayCommand]
    private void SetPower1(double value)
    {
        if (!EnsureCanOperate()) return;
        Power1Setpoint = value;
        LogOperation("电源1", "设置功率", $"{value:0.###} W");
    }

    [RelayCommand]
    private void TogglePower2()
    {
        if (!EnsureCanOperate()) return;
        Power2IsRunning = !Power2IsRunning;
        LogOperation("电源2", Power2IsRunning ? "启动" : "停止");
    }

    [RelayCommand]
    private void SetPower2(double value)
    {
        if (!EnsureCanOperate()) return;
        Power2Setpoint = value;
        LogOperation("电源2", "设置功率", $"{value:0.###} W");
    }

    [RelayCommand]
    private void ToggleApcMode()
    {
        if (!EnsureCanOperate()) return;
        ApcIsPositioningMode = !ApcIsPositioningMode;
        LogOperation("APC阀", "切换控制模式", ApcIsPositioningMode ? "定位模式" : "控压模式");
    }

    [RelayCommand]
    private void SetApcPosition(double value)
    {
        if (!EnsureCanOperate()) return;
        ApcPositionSetpoint = value;
        LogOperation("APC阀", "设置位置", $"{value:0.###} %");
    }

    [RelayCommand]
    private void SetApcPressure(double value)
    {
        if (!EnsureCanOperate()) return;
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
