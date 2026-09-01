using CommunityToolkit.Mvvm.ComponentModel;
using Small_square_cavity_coating_machine.Models.Alarms;
using Small_square_cavity_coating_machine.Models.Equipment;
using Small_square_cavity_coating_machine.Models.Security;
using Small_square_cavity_coating_machine.Services.Alarms;
using Small_square_cavity_coating_machine.Services.Equipment;
using Small_square_cavity_coating_machine.Services.History;
using Small_square_cavity_coating_machine.Services.Security;

namespace Small_square_cavity_coating_machine.ViewModels;

public partial class ControlViewModel : IDisposable
{
    private IEquipmentControlService? _controlService;
    private IUiDispatcher? _dispatcher;
    private readonly Dictionary<string, bool> _requestedStates = new(StringComparer.Ordinal);
    private string _requestedStageDirection = "Stop";
    private bool _disposed;

    public ControlViewModel(IOperationLogRepository operationLogRepository, IAuthorizationService authorization,
        IEquipmentControlService controlService, IUiDispatcher dispatcher)
        : this(operationLogRepository, authorization)
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
    private double turboPumpCurrentSpeed;

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
        if (!TryQueueSetpoint(0, value)) TurboPumpSetpointSpeed = value;
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
        var dryPump = State(snapshot, 0);
        var turboPump = State(snapshot, 1);
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
        SampleStageIsForwardRunning = false;
        SampleStageIsReverseRunning = false;
        SampleStageIsRunning = sampleStage.Open;
        SampleStageIsFaulted = sampleStage.Fault;
        DryPumpIsRunning = dryPump.Open;
        DryPumpIsFaulted = dryPump.Fault;
        TurboPumpIsRunning = turboPump.Open;
        TurboPumpIsFaulted = turboPump.Fault;
        HeaterIsRunning = heater.Open;
        HeaterIsFaulted = heater.Fault;
        ApcIsOpen = apc.Open;
        ApcIsFaulted = apc.Fault;

        ForelineGaugeIsReadingEnabled = forelineGauge.Open;
        ForelineGaugeIsFaulted = forelineGauge.Fault;
        HighVacuumGaugeIsReadingEnabled = highVacuumGauge.Open;
        HighVacuumGaugeIsFaulted = highVacuumGauge.Fault;
        FilmGaugeIsReadingEnabled = filmGauge.Open;
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
        TurboPumpSetpointSpeed = Number(snapshot, "Part_Data_Set1[0]");
        ApcCurrentPosition = Number(snapshot, "Part_Data1[1]");
        ApcPositionSetpoint = Number(snapshot, "Part_Data_Set1[1]");
        ApcIsPositioningMode = true;
        ApcCurrentPressure = double.NaN;
        ApcPressureSetpoint = double.NaN;
        HeaterCurrentTemperature = Number(snapshot, "Part_Data1[2]");
        HeaterSetpointTemperature = Number(snapshot, "Part_Data_Set1[2]");
        SampleStageCurrentSpeed = Number(snapshot, "Part_Data1[3]");
        SampleStageSetpointSpeed = Number(snapshot, "Part_Data_Set2[2]");
        ArgonCurrentFlow = Number(snapshot, "Part_Data1[7]");
        NitrogenCurrentFlow = Number(snapshot, "Part_Data1[8]");
        OxygenCurrentFlow = Number(snapshot, "Part_Data1[9]");
        ArgonSetpointFlow = Number(snapshot, "Part_Data_Set1[4]");
        NitrogenSetpointFlow = Number(snapshot, "Part_Data_Set1[5]");
        OxygenSetpointFlow = Number(snapshot, "Part_Data_Set1[6]");
        ForelineVacuumValue = Number(snapshot, "Part_Data1[10]");
        HighVacuumValue = Number(snapshot, "Part_Data1[11]");
        FilmHighVacuumValue = Number(snapshot, "Part_Data1[12]");
        Power1MeasuredPower = Number(snapshot, "Part_Data1[21]");
        Power1MeasuredVoltage = Number(snapshot, "Part_Data1[22]");
        Power1MeasuredCurrent = Number(snapshot, "Part_Data1[23]");
        Power1Setpoint = Number(snapshot, "Part_Data_Set2[0]");
        Power2MeasuredPower = Number(snapshot, "Part_Data1[24]");
        Power2MeasuredVoltage = Number(snapshot, "Part_Data1[25]");
        Power2MeasuredCurrent = Number(snapshot, "Part_Data1[26]");
        Power2Setpoint = Number(snapshot, "Part_Data_Set2[1]");

        SystemIsRunning = Boolean(snapshot, "fbButtonStart_Output");
        SystemIsStopped = Boolean(snapshot, "fbButtonStop_Output");
        SystemResetIsActive = Boolean(snapshot, "fbButtonReset_Output");
        AutomaticModeIsSelected = Boolean(snapshot, "fbButtonAuto_Output");
        SemiAutomaticModeIsSelected = Boolean(snapshot, "fbButtonSemi_Output");
        ManualModeIsSelected = Boolean(snapshot, "fbButtonManual_Output");

        ApcInterlockReleased = Boolean(snapshot, ApcIsOpen ? "EQ_Interlock[7]" : "EQ_Interlock[6]");
        BypassValveInterlockReleased = Boolean(snapshot, BypassValveIsOpen ? "EQ_Interlock[1]" : "EQ_Interlock[0]");
        RightForelineValveInterlockReleased = Boolean(snapshot, RightForelineValveIsOpen ? "EQ_Interlock[3]" : "EQ_Interlock[2]");
        LowerForelineValveInterlockReleased = Boolean(snapshot, LowerForelineValveIsOpen ? "EQ_Interlock[9]" : "EQ_Interlock[8]");
        ArgonLowerValveInterlockReleased = Boolean(snapshot, ArgonLowerValveIsOpen ? "EQ_Interlock[11]" : "EQ_Interlock[10]");
        NitrogenLowerValveInterlockReleased = Boolean(snapshot, NitrogenLowerValveIsOpen ? "EQ_Interlock[13]" : "EQ_Interlock[12]");
        OxygenLowerValveInterlockReleased = Boolean(snapshot, OxygenLowerValveIsOpen ? "EQ_Interlock[15]" : "EQ_Interlock[14]");

        var bypass = Boolean(snapshot, EquipmentGroups.PassInterlock);
        ApplicationStatusViewModel.Instance.MaintenanceBypassActive = bypass;
        ControlStatusText = snapshot.IsConnected ? "PLC控制点已连接" : "PLC控制点未连接，设备状态不可用";
    }

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

    private static EquipmentPoint? Point(EquipmentSnapshot snapshot, string address)
    {
        var group = EquipmentAddress.Group(address);
        return snapshot.Groups.GetValueOrDefault(group)?.Points.GetValueOrDefault(address);
    }

    private static bool Boolean(EquipmentSnapshot snapshot, string address) =>
        Point(snapshot, address) is { Quality: AlarmQuality.Good, Value: true };

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

    private bool TryQueuePartToggle(string key, bool feedbackOpen, int openId, int closeId, string target)
    {
        if (_controlService is null) return false;
        var knownFeedback = _controlService.Definitions.Commands.GetValueOrDefault(openId) is { PartId: var partId }
            && _controlService.Definitions.Parts.GetValueOrDefault(partId)?.StateAddress is { Length: > 0 };
        var current = knownFeedback ? feedbackOpen : Requested(key);
        QueuePartCommand(current ? closeId : openId, target, current ? "关闭" : "开启", key, !current);
        return true;
    }

    private bool TryQueueWorkflowCommand(int id, string action)
    {
        if (_controlService is null) return false;
        var service = _controlService;
        _ = Task.Run(async () =>
        {
            var result = await service.ExecutePartCommandAsync(id, "真空流程", action).ConfigureAwait(false);
            _dispatcher?.Post(() =>
            {
                ControlStatusText = result.Message;
                if (result.Outcome == ControlWriteOutcome.Confirmed) ToggleWorkflowSelection(id);
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

    private void QueuePartCommand(int id, string target, string action, string? requestedKey, bool? requestedValue)
    {
        var service = _controlService!;
        _ = Task.Run(async () =>
        {
            var result = await service.ExecutePartCommandAsync(id, target, action).ConfigureAwait(false);
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
        _ = Task.Run(async () =>
        {
            var result = await service.ExecuteSystemCommandAsync(name).ConfigureAwait(false);
            _dispatcher?.Post(() => ControlStatusText = result.Message);
        });
        return true;
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
        var nextDirection = _requestedStageDirection == direction ? "Stop" : direction;
        var nextCommand = nextDirection == "Stop" ? 12 : commandId;
        var service = _controlService;
        _ = Task.Run(async () =>
        {
            var result = await service.ExecutePartCommandAsync(nextCommand, "样品台",
                nextDirection == "Stop" ? "停止" : nextDirection == "Forward" ? "正转" : "反转").ConfigureAwait(false);
            _dispatcher?.Post(() =>
            {
                ControlStatusText = result.Message;
                if (result.Outcome == ControlWriteOutcome.Confirmed) _requestedStageDirection = nextDirection;
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
