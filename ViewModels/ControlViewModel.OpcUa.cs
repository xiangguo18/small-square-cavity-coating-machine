using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;
using Small_square_cavity_coating_machine.Models.Alarms;
using Small_square_cavity_coating_machine.Models.Equipment;
using Small_square_cavity_coating_machine.Models.Security;
using Small_square_cavity_coating_machine.Controls;
using Small_square_cavity_coating_machine.Services.Alarms;
using Small_square_cavity_coating_machine.Services.Equipment;
using Small_square_cavity_coating_machine.Services.History;
using Small_square_cavity_coating_machine.Services.Security;
using Small_square_cavity_coating_machine.Services;

namespace Small_square_cavity_coating_machine.ViewModels;

public partial class ControlViewModel : IDisposable
{
    private IEquipmentControlService? _controlService;
    private IUiDispatcher? _dispatcher;
    private readonly Dictionary<string, bool> _requestedStates = new(StringComparer.Ordinal);
    private bool _disposed;

    // 左侧系统控制：同组三个命令地址互斥（目标写 1，其余写 0）。
    private static readonly Dictionary<string, string[]> SystemMutexGroups = new(StringComparer.Ordinal)
    {
        ["Start"] = ["Stop", "Reset"],
        ["Stop"] = ["Start", "Reset"],
        ["Reset"] = ["Start", "Stop"],
        ["Auto"] = ["Semi", "Manual"],
        ["Semi"] = ["Auto", "Manual"],
        ["Manual"] = ["Auto", "Semi"],
    };

    // 真空控制：三个流程命令互斥。
    private static readonly Dictionary<int, int[]> WorkflowMutexGroups = new()
    {
        [980] = [981, 982],
        [981] = [980, 982],
        [982] = [980, 981],
    };

    public ControlViewModel(IOperationLogRepository operationLogRepository, IAuthorizationService authorization,
        IEquipmentControlService controlService, IUiDispatcher dispatcher,
        IConfirmationDialogService? confirmation = null)
        : this(operationLogRepository, authorization, confirmation)
    {
        _controlService = controlService;
        _dispatcher = dispatcher;
        _controlService.Changed += ControlServiceChanged;
        ApplySnapshot(_controlService.Snapshot());
    }

    [ObservableProperty]
    private string controlStatusText = "等待PLC控制点连接";

    [ObservableProperty] private bool dryPumpIsFaulted;
    [ObservableProperty] private bool turboPumpIsFaulted;
    [ObservableProperty] private bool dryPumpStateKnown;
    [ObservableProperty] private bool turboPumpStateKnown;
    [ObservableProperty] private int? dryPumpRawState;
    [ObservableProperty] private int? turboPumpRawState;
    [ObservableProperty] private PumpVisualState dryPumpVisualState;
    [ObservableProperty] private PumpVisualState turboPumpVisualState;
    [ObservableProperty] private bool apcIsFaulted;
    [ObservableProperty] private bool heaterIsFaulted;
    [ObservableProperty] private bool bypassValveIsFaulted;
    [ObservableProperty] private bool rightForelineValveIsFaulted;
    [ObservableProperty] private bool lowerForelineValveIsFaulted;
    [ObservableProperty] private bool argonUpperValveIsFaulted;
    [ObservableProperty] private bool argonLowerValveIsFaulted;
    [ObservableProperty] private bool nitrogenUpperValveIsFaulted;
    [ObservableProperty] private bool nitrogenLowerValveIsFaulted;
    [ObservableProperty] private bool oxygenUpperValveIsFaulted;
    [ObservableProperty] private bool oxygenLowerValveIsFaulted;
    [ObservableProperty] private bool sampleShutterIsFaulted;
    [ObservableProperty] private bool target1ShutterIsFaulted;
    [ObservableProperty] private bool target2ShutterIsFaulted;
    [ObservableProperty] private bool power1IsFaulted;
    [ObservableProperty] private bool power2IsFaulted;
    [ObservableProperty] private bool forelineGaugeIsFaulted;
    [ObservableProperty] private bool highVacuumGaugeIsFaulted;
    [ObservableProperty] private bool filmGaugeIsFaulted;
    [ObservableProperty] private bool filmGaugeValveIsFaulted;

    [ObservableProperty]
    private double turboPumpCurrentSpeed = double.NaN;

    public string TurboPumpSpeedDisplay => double.IsFinite(TurboPumpCurrentSpeed)
        ? TurboPumpCurrentSpeed.ToString("0.0", CultureInfo.InvariantCulture) + " %"
        : "-- %";

    partial void OnTurboPumpCurrentSpeedChanged(double value) =>
        OnPropertyChanged(nameof(TurboPumpSpeedDisplay));

    [ObservableProperty]
    private double turboPumpSetpointSpeed;

    [ObservableProperty]
    private double nitrogenSetpointFlow = 1d;

    [ObservableProperty]
    private double oxygenSetpointFlow = 1d;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SetTurboPumpSpeed(double value)
    {
        if (!EnsureCanOperate()) return;
        TurboPumpSetpointSpeed = value;
        _ = TryQueueSetpoint(0, value);
    }

    private void ControlServiceChanged(object? sender, EventArgs e)
    {
        var service = _controlService;
        if (service is null || _disposed) return;
        var snapshot = service.Snapshot();
        _dispatcher?.Post(() =>
        {
            if (!_disposed) ApplySnapshot(snapshot);
        });
    }

    private void ApplySnapshot(EquipmentSnapshot snapshot)
    {
        var dryPump = DryPumpState(snapshot);
        var turboPump = TurboPumpState(snapshot);
        var apc = State(snapshot, 2);
        var heater = State(snapshot, 3);
        var sampleStage = State(snapshot, 4);
        var sampleShutter = State(snapshot, 5);
        var power1 = State(snapshot, 6);
        var power2 = State(snapshot, 7);
        var argonValve = State(snapshot, 11);
        var cathode1Valve = State(snapshot, 12);
        var nitrogenValve = State(snapshot, 13);
        var cathode2Valve = State(snapshot, 14);
        var oxygenValve = State(snapshot, 15);
        var cathode3Valve = State(snapshot, 16);
        var forelineGauge = State(snapshot, 17);
        var highVacuumGauge = State(snapshot, 18);
        var filmGauge = State(snapshot, 19);
        var filmGaugeValve = State(snapshot, 20);
        var bypassValve = State(snapshot, 21);
        var forelineValve = State(snapshot, 22);
        var ventValve = State(snapshot, 23);
        var target1Shutter = State(snapshot, 24);
        var target2Shutter = State(snapshot, 25);

        ArgonUpperValveIsOpen = cathode1Valve.Open;
        ArgonUpperValveIsFaulted = cathode1Valve.Fault;
        NitrogenUpperValveIsOpen = cathode2Valve.Open;
        NitrogenUpperValveIsFaulted = cathode2Valve.Fault;
        OxygenUpperValveIsOpen = cathode3Valve.Open;
        OxygenUpperValveIsFaulted = cathode3Valve.Fault;
        ArgonLowerValveIsOpen = argonValve.Open;
        ArgonLowerValveIsFaulted = argonValve.Fault;
        NitrogenLowerValveIsOpen = nitrogenValve.Open;
        NitrogenLowerValveIsFaulted = nitrogenValve.Fault;
        OxygenLowerValveIsOpen = oxygenValve.Open;
        OxygenLowerValveIsFaulted = oxygenValve.Fault;
        SampleShutterIsOn = sampleShutter.Open;
        SampleShutterIsFaulted = sampleShutter.Fault;
        Target1ShutterIsOn = target1Shutter.Open;
        Target1ShutterIsFaulted = target1Shutter.Fault;
        Target2ShutterIsOn = target2Shutter.Open;
        Target2ShutterIsFaulted = target2Shutter.Fault;
        Power1IsRunning = power1.Open;
        Power1IsFaulted = power1.Fault;
        Power2IsRunning = power2.Open;
        Power2IsFaulted = power2.Fault;
        SampleStageIsRunning = sampleStage.Open;
        SampleStageIsFaulted = sampleStage.Fault;
        DryPumpRawState = dryPump.RawState;
        DryPumpStateKnown = dryPump.Known;
        DryPumpIsRunning = dryPump.Running;
        DryPumpIsFaulted = dryPump.Faulted;
        DryPumpVisualState = dryPump.VisualState;
        TurboPumpRawState = turboPump.RawState;
        TurboPumpStateKnown = turboPump.Known;
        TurboPumpIsRunning = turboPump.Running;
        TurboPumpIsFaulted = turboPump.Faulted;
        TurboPumpVisualState = turboPump.VisualState;
        HeaterIsRunning = heater.Open;
        HeaterIsFaulted = heater.Fault;
        ApcIsOpen = apc.Open;
        ApcIsFaulted = apc.Fault;

        // 读数可用性由实测真空度数据点质量决定；Part_State 开位仅用于故障/阀状态标识，不再门控读数。
        ForelineVacuumValue = Number(snapshot, "Part_Data1[10]");
        ForelineGaugeIsReadingEnabled = double.IsFinite(ForelineVacuumValue);
        ForelineGaugeIsFaulted = forelineGauge.Fault;
        HighVacuumValue = Number(snapshot, "Part_Data1[11]");
        HighVacuumGaugeIsReadingEnabled = double.IsFinite(HighVacuumValue);
        HighVacuumGaugeIsFaulted = highVacuumGauge.Fault;
        FilmHighVacuumValue = Number(snapshot, "Part_Data1[12]");
        FilmGaugeIsReadingEnabled = double.IsFinite(FilmHighVacuumValue);
        FilmGaugeIsFaulted = filmGauge.Fault;
        FilmGaugeValveIsOpen = filmGaugeValve.Open;
        FilmGaugeValveIsFaulted = filmGaugeValve.Fault;
        BypassValveIsOpen = bypassValve.Open;
        BypassValveIsFaulted = bypassValve.Fault;
        RightForelineValveIsOpen = forelineValve.Open;
        RightForelineValveIsFaulted = forelineValve.Fault;
        LowerForelineValveIsOpen = ventValve.Open;
        LowerForelineValveIsFaulted = ventValve.Fault;

        TurboPumpCurrentSpeed = Number(snapshot, "Part_Data1[0]");
        ApcCurrentPosition = Number(snapshot, "Part_Data1[1]");
        ApcIsPositioningMode = true;
        ApcCurrentPressure = double.NaN;
        ApcPressureSetpoint = double.NaN;
        HeaterCurrentTemperature = Number(snapshot, "Part_Data1[2]");
        SampleStageCurrentSpeed = Number(snapshot, "Part_Data1[3]");
        ArgonCurrentFlow = Number(snapshot, "Part_Data1[7]");
        NitrogenCurrentFlow = Number(snapshot, "Part_Data1[8]");
        OxygenCurrentFlow = Number(snapshot, "Part_Data1[9]");
        Power1MeasuredPower = Number(snapshot, "Part_Data1[21]");
        Power1MeasuredVoltage = Number(snapshot, "Part_Data1[22]");
        Power1MeasuredCurrent = Number(snapshot, "Part_Data1[23]");
        Power2MeasuredPower = Number(snapshot, "Part_Data1[24]");
        Power2MeasuredVoltage = Number(snapshot, "Part_Data1[25]");
        Power2MeasuredCurrent = Number(snapshot, "Part_Data1[26]");

        // 写入进行中不覆盖刚刚录入的乐观设定值，写入完成后由快照回读校准。
        if (!snapshot.IsWriting)
        {
            TurboPumpSetpointSpeed = Number(snapshot, "Part_Data_Set1[0]");
            ApcPositionSetpoint = Number(snapshot, "Part_Data_Set1[1]");
            HeaterSetpointTemperature = Number(snapshot, "Part_Data_Set1[2]");
            SampleStageSetpointSpeed = Number(snapshot, "Part_Data_Set2[2]");
            ArgonSetpointFlow = Number(snapshot, "Part_Data_Set1[4]");
            NitrogenSetpointFlow = Number(snapshot, "Part_Data_Set1[5]");
            OxygenSetpointFlow = Number(snapshot, "Part_Data_Set1[6]");
            Power1Setpoint = Number(snapshot, "Part_Data_Set2[0]");
            Power2Setpoint = Number(snapshot, "Part_Data_Set2[1]");
        }

        ApcInterlockReleased = Boolean(snapshot, ApcIsOpen ? "EQ_Interlock[7]" : "EQ_Interlock[6]");
        BypassValveInterlockReleased = Boolean(snapshot, BypassValveIsOpen ? "EQ_Interlock[1]" : "EQ_Interlock[0]");
        RightForelineValveInterlockReleased = Boolean(snapshot, RightForelineValveIsOpen ? "EQ_Interlock[3]" : "EQ_Interlock[2]");
        LowerForelineValveInterlockReleased = Boolean(snapshot, LowerForelineValveIsOpen ? "EQ_Interlock[9]" : "EQ_Interlock[8]");
        ArgonLowerValveInterlockReleased = Boolean(snapshot, ArgonLowerValveIsOpen ? "EQ_Interlock[11]" : "EQ_Interlock[10]");
        NitrogenLowerValveInterlockReleased = Boolean(snapshot, NitrogenLowerValveIsOpen ? "EQ_Interlock[13]" : "EQ_Interlock[12]");
        OxygenLowerValveInterlockReleased = Boolean(snapshot, OxygenLowerValveIsOpen ? "EQ_Interlock[15]" : "EQ_Interlock[14]");

        // 按钮绿灯由 Part_State[30..35] 持续订阅驱动；断连/质量无效返回 false，fail-closed。
        SystemIsRunning = ButtonFeedback(snapshot, "Start");
        SystemIsStopped = ButtonFeedback(snapshot, "Stop");
        SystemResetIsActive = ButtonFeedback(snapshot, "Reset");
        AutomaticModeIsSelected = ButtonFeedback(snapshot, "Auto");
        SemiAutomaticModeIsSelected = ButtonFeedback(snapshot, "Semi");
        ManualModeIsSelected = ButtonFeedback(snapshot, "Manual");
        VacuumingIsSelected = ButtonFeedback(snapshot, "PumpStart");
        VentingIsSelected = ButtonFeedback(snapshot, "VentStart");
        PressureHoldingIsSelected = ButtonFeedback(snapshot, "HP_Start");

        var bypass = Boolean(snapshot, EquipmentGroups.PassInterlock);
        ApplicationStatusViewModel.Instance.MaintenanceBypassActive = bypass;
        if (!snapshot.IsConnected) ResetCommandActiveStates();
        ControlStatusText = snapshot.IsConnected ? "PLC控制点已连接" : "PLC控制点未连接，设备状态不可用";
    }

    /// <summary>读取某系统按钮的 PLC 反馈点，决定绿灯是否点亮；命名命令优先取定义库的 FeedbackAddress。</summary>
    private bool ButtonFeedback(EquipmentSnapshot snapshot, string key)
    {
        var address = key switch
        {
            "Start" => SystemFeedback("Start") ?? "Part_State[33]",
            "Stop" => SystemFeedback("Stop") ?? "Part_State[34]",
            "Reset" => SystemFeedback("Reset") ?? "Part_State[35]",
            "Auto" => SystemFeedback("Auto") ?? "Part_State[32]",
            "Semi" => SystemFeedback("Semi") ?? "Part_State[31]",
            "Manual" => SystemFeedback("Manual") ?? "Part_State[30]",
            "PumpStart" => "fbButtonPumpStart_Output",
            "VentStart" => "fbButtonVentStart_Output",
            "HP_Start" => "fbButtonHP_Start_Output",
            _ => "",
        };
        return !string.IsNullOrWhiteSpace(address) && Truthy(snapshot, address);
    }

    private string? SystemFeedback(string name) =>
        _controlService?.Definitions.SystemCommands.GetValueOrDefault(name)?.FeedbackAddress;

    private static (bool Known, bool Open, bool Fault) State(EquipmentSnapshot snapshot, int index)
    {
        var address = $"Part_State[{index}]";
        var point = Point(snapshot, address);
        if (point is not { Quality: AlarmQuality.Good } || point.Value is null) return (false, false, false);
        try
        {
            var word = Convert.ToUInt64(point.Value);
            var open = (word & (1UL << 1)) != 0;
            var closed = (word & 1UL) != 0;
            var fault = (word & (1UL << 7)) != 0;
            var positionValid = open != closed;
            return (fault || positionValid, positionValid && open, fault);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            return (false, false, false);
        }
    }

    /// <summary>干泵采用 PLC 数值状态：0 停机、2 运行、6 运行告警闪烁；其他值不驱动启停。</summary>
    private static (int? RawState, bool Known, bool Running, bool Faulted, PumpVisualState VisualState) DryPumpState(
        EquipmentSnapshot snapshot)
    {
        var raw = RawState(snapshot, 0);
        return raw switch
        {
            0 => (raw, true, false, false, PumpVisualState.Idle),
            2 => (raw, true, true, false, PumpVisualState.Green),
            6 => (raw, true, true, false, PumpVisualState.GreenBlink),
            _ => (raw, false, false, raw is not null && (raw.Value & (1 << 7)) != 0, PumpVisualState.Idle)
        };
    }

    /// <summary>分子泵采用 PLC 数值状态机，非零状态均由现场约定为非停机状态。</summary>
    private static (int? RawState, bool Known, bool Running, bool Faulted, PumpVisualState VisualState) TurboPumpState(
        EquipmentSnapshot snapshot)
    {
        var raw = RawState(snapshot, 1);
        return raw switch
        {
            0 => (raw, true, false, false, PumpVisualState.Idle),
            1 => (raw, true, true, false, PumpVisualState.Green),
            2 => (raw, true, true, false, PumpVisualState.Yellow),
            3 => (raw, true, true, false, PumpVisualState.Yellow),
            4 => (raw, true, true, true, PumpVisualState.Red),
            5 => (raw, true, true, false, PumpVisualState.GreenBlink),
            6 => (raw, true, true, false, PumpVisualState.YellowBlink),
            _ => (raw, false, false, false, PumpVisualState.Idle)
        };
    }

    private static int? RawState(EquipmentSnapshot snapshot, int index)
    {
        var point = Point(snapshot, $"Part_State[{index}]");
        if (point is not { Quality: AlarmQuality.Good } || point.Value is null) return null;
        try
        {
            var value = Convert.ToInt32(point.Value);
            return value >= 0 ? value : null;
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            return null;
        }
    }

    private static EquipmentPoint? Point(EquipmentSnapshot snapshot, string address)
    {
        var group = EquipmentAddress.Group(address);
        return snapshot.Groups.GetValueOrDefault(group)?.Points.GetValueOrDefault(address);
    }

    private static bool Boolean(EquipmentSnapshot snapshot, string address) =>
        Point(snapshot, address) is { Quality: AlarmQuality.Good, Value: true };

    /// <summary>Part_State 元素为数值位/0-1，系统按钮绿灯按“非零或布尔真”判读，而非仅认 C# bool true。</summary>
    private static bool Truthy(EquipmentSnapshot snapshot, string address)
    {
        var point = Point(snapshot, address);
        if (point is not { Quality: AlarmQuality.Good } || point.Value is null) return false;
        if (point.Value is bool boolean) return boolean;
        try { return Convert.ToUInt64(point.Value) != 0; }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException) { return false; }
    }

    private static double Number(EquipmentSnapshot snapshot, string address)
    {
        var point = Point(snapshot, address);
        if (point is not { Quality: AlarmQuality.Good } || point.Value is null) return double.NaN;
        try
        {
            var value = Convert.ToDouble(point.Value);
            return double.IsFinite(value) ? value : double.NaN;
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            return double.NaN;
        }
    }

    private bool Requested(string key) => _requestedStates.GetValueOrDefault(key);

    private bool TryQueuePartToggle(string key, bool feedbackOpen, int openId, int closeId, string target,
        bool stateKnown = true)
    {
        if (_controlService is null) return false;
        if (!stateKnown)
        {
            ControlStatusText = $"{target}状态值未知，未发送启停命令";
            return true;
        }
        var knownFeedback = _controlService.Definitions.Commands.GetValueOrDefault(openId) is { PartId: var partId }
            && _controlService.Definitions.Parts.GetValueOrDefault(partId)?.StateAddress is { Length: > 0 };
        var current = knownFeedback ? feedbackOpen : Requested(key);
        var assertId = current ? closeId : openId;
        var deassertId = current ? openId : closeId;
        QueuePartCommand(assertId, deassertId, target, current ? "关闭" : "开启", key, !current);
        return true;
    }

    private bool TryQueueWorkflowCommand(int id, string action)
    {
        if (_controlService is null) return false;
        var service = _controlService;
        var siblings = WorkflowMutexGroups.TryGetValue(id, out var ids) ? ids : [];
        _ = Task.Run(async () =>
        {
            var result = await service.ExecutePartCommandBatchAsync(id, siblings, "真空流程", action).ConfigureAwait(false);
            _dispatcher?.Post(() =>
            {
                ControlStatusText = result.Message;
            });
        });
        return true;
    }

    private void ToggleWorkflowSelection(int id)
    {
        var turnOff = id switch
        {
            980 => VacuumingIsSelected,
            981 => VentingIsSelected,
            982 => PressureHoldingIsSelected,
            _ => true
        };
        VacuumingIsSelected = !turnOff && id == 980;
        VentingIsSelected = !turnOff && id == 981;
        PressureHoldingIsSelected = !turnOff && id == 982;
    }

    private void QueuePartCommand(int assertId, int deassertId, string target, string action, string? requestedKey, bool? requestedValue)
    {
        var service = _controlService!;
        _ = Task.Run(async () =>
        {
            var deasserts = deassertId >= 0 ? new[] { deassertId } : [];
            var result = await service.ExecutePartCommandBatchAsync(assertId, deasserts, target, action).ConfigureAwait(false);
            _dispatcher?.Post(() =>
            {
                ControlStatusText = result.Message;
                if (result.Outcome == ControlWriteOutcome.Confirmed && requestedKey is not null && requestedValue.HasValue)
                    _requestedStates[requestedKey] = requestedValue.Value;
            });
        });
    }

    private bool TryQueueSystemCommand(string name)
    {
        if (_controlService is null) return false;
        var service = _controlService;
        var siblings = SystemMutexGroups.TryGetValue(name, out var names) ? names : [];
        _ = Task.Run(async () =>
        {
            foreach (var sibling in siblings)
                await service.WriteSystemCommandAsync(sibling, false, "系统控制", "互斥清除").ConfigureAwait(false);
            var result = await service.ExecuteSystemCommandAsync(name).ConfigureAwait(false);
            _dispatcher?.Post(() =>
            {
                ControlStatusText = result.Message;
                if (result.Outcome != ControlWriteOutcome.Confirmed) return;
                ApplySystemCommandSelection(name);
            });
        });
        return true;
    }

    /// <summary>按钮绿灯由 Part_State[30..35] 反馈驱动；此处仅保留复位的本地互斥清理（样品台方向灯无独立复盘反馈）。</summary>
    private void ApplySystemCommandSelection(string name)
    {
        if (name == "Reset") ClearSampleStageDirection();
    }

    private void ClearSampleStageDirection()
    {
        SampleStageIsForwardRunning = false;
        SampleStageIsReverseRunning = false;
        SampleStageIsStopped = false;
    }

    /// <summary>运行模式、真空控制、样品台方向三组的按钮灯全部熄灭（系统复位/断连时使用）。</summary>
    private void ClearModeAndWorkflowAndStageSelection()
    {
        AutomaticModeIsSelected = false;
        SemiAutomaticModeIsSelected = false;
        ManualModeIsSelected = false;
        VacuumingIsSelected = false;
        VentingIsSelected = false;
        PressureHoldingIsSelected = false;
        SampleStageIsForwardRunning = false;
        SampleStageIsReverseRunning = false;
        SampleStageIsStopped = false;
    }

    /// <summary>所有写入成功亮灯按钮（含系统控制组）全部熄灭；断连/重连时 fail-closed。</summary>
    private void ResetCommandActiveStates()
    {
        SystemIsRunning = false;
        SystemIsStopped = false;
        SystemResetIsActive = false;
        ClearModeAndWorkflowAndStageSelection();
    }

    private bool TryQueueSetpoint(int dataId, double value)
    {
        if (_controlService is null) return false;
        var service = _controlService;
        _ = Task.Run(async () =>
        {
            var result = await service.WriteSetpointAsync(dataId, value, PermissionKey.SystemStatus).ConfigureAwait(false);
            _dispatcher?.Post(() => ControlStatusText = result.Message);
        });
        return true;
    }

    private bool TryQueueStageDirection(string direction, int commandId)
    {
        if (_controlService is null) return false;
        var deasserts = commandId switch
        {
            10 => new[] { 11, 12 },
            11 => new[] { 10, 12 },
            12 => new[] { 10, 11 },
            _ => new[] { 10, 11 },
        };
        var service = _controlService;
        _ = Task.Run(async () =>
        {
            var result = await service.ExecutePartCommandBatchAsync(commandId, deasserts, "样品台",
                direction == "Stop" ? "停止" : direction == "Forward" ? "正转" : "反转").ConfigureAwait(false);
            _dispatcher?.Post(() =>
            {
                ControlStatusText = result.Message;
                if (result.Outcome != ControlWriteOutcome.Confirmed) return;
                SampleStageIsForwardRunning = direction == "Forward";
                SampleStageIsReverseRunning = direction == "Reverse";
                SampleStageIsStopped = direction == "Stop";
            });
        });
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_controlService is not null) _controlService.Changed -= ControlServiceChanged;
        if (_authorization is not null) _authorization.AccessChanged -= Authorization_AccessChanged;
    }
}
