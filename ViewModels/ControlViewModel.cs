using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Small_square_cavity_coating_machine.ViewModels;

/// <summary>
/// 系统状态页的离线预览数据。
/// 后续接入PLC时可由实时状态服务更新这些属性。
/// </summary>
public partial class ControlViewModel : ObservableObject
{
    [ObservableProperty]
    private bool argonUpperValveIsOpen;

    [ObservableProperty]
    private bool argonUpperValveInterlockReleased = true;

    [ObservableProperty]
    private bool argonLowerValveIsOpen;

    [ObservableProperty]
    private bool argonLowerValveInterlockReleased = true;

    [ObservableProperty]
    private bool nitrogenUpperValveIsOpen;

    [ObservableProperty]
    private bool nitrogenUpperValveInterlockReleased = true;

    [ObservableProperty]
    private bool nitrogenLowerValveIsOpen;

    [ObservableProperty]
    private bool nitrogenLowerValveInterlockReleased = true;

    [ObservableProperty]
    private bool oxygenUpperValveIsOpen;

    [ObservableProperty]
    private bool oxygenUpperValveInterlockReleased = true;

    [ObservableProperty]
    private bool oxygenLowerValveIsOpen;

    [ObservableProperty]
    private bool oxygenLowerValveInterlockReleased = true;

    [ObservableProperty]
    private bool bypassValveIsOpen;

    [ObservableProperty]
    private bool bypassValveInterlockReleased = true;

    [ObservableProperty]
    private bool rightForelineValveIsOpen;

    [ObservableProperty]
    private bool rightForelineValveInterlockReleased = true;

    [ObservableProperty]
    private bool lowerForelineValveIsOpen;

    [ObservableProperty]
    private bool lowerForelineValveInterlockReleased = true;

    [ObservableProperty]
    private bool turboPumpIsRunning;

    [ObservableProperty]
    private bool turboPumpInterlockReleased = true;

    [ObservableProperty]
    private bool dryPumpIsRunning;

    [ObservableProperty]
    private bool dryPumpInterlockReleased = true;

    [ObservableProperty]
    private bool vacuumPathIsFlowing = true;

    [ObservableProperty]
    private bool branchPathIsFlowing = true;

    [ObservableProperty]
    private double argonCurrentFlow = 0.6;

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
    private bool chillerIsOn;

    [ObservableProperty]
    private bool chillerInterlockReleased = true;

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

    [RelayCommand]
    private void ToggleArgonUpperValve()
    {
        ArgonUpperValveIsOpen = !ArgonUpperValveIsOpen;
    }

    [RelayCommand]
    private void ToggleArgonLowerValve()
    {
        ArgonLowerValveIsOpen = !ArgonLowerValveIsOpen;
    }

    [RelayCommand]
    private void ToggleNitrogenUpperValve()
    {
        NitrogenUpperValveIsOpen = !NitrogenUpperValveIsOpen;
    }

    [RelayCommand]
    private void ToggleNitrogenLowerValve()
    {
        NitrogenLowerValveIsOpen = !NitrogenLowerValveIsOpen;
    }

    [RelayCommand]
    private void ToggleOxygenUpperValve()
    {
        OxygenUpperValveIsOpen = !OxygenUpperValveIsOpen;
    }

    [RelayCommand]
    private void ToggleOxygenLowerValve()
    {
        OxygenLowerValveIsOpen = !OxygenLowerValveIsOpen;
    }

    [RelayCommand]
    private void ToggleBypassValve()
    {
        BypassValveIsOpen = !BypassValveIsOpen;
    }

    [RelayCommand]
    private void ToggleRightForelineValve()
    {
        RightForelineValveIsOpen = !RightForelineValveIsOpen;
    }

    [RelayCommand]
    private void ToggleLowerForelineValve()
    {
        LowerForelineValveIsOpen = !LowerForelineValveIsOpen;
    }

    [RelayCommand]
    private void ToggleTurboPump()
    {
        TurboPumpIsRunning = !TurboPumpIsRunning;
    }

    [RelayCommand]
    private void ToggleDryPump()
    {
        DryPumpIsRunning = !DryPumpIsRunning;
    }

    [RelayCommand]
    private void ToggleFilmGauge()
    {
        FilmGaugeIsReadingEnabled = !FilmGaugeIsReadingEnabled;
    }

    [RelayCommand]
    private void SetSampleStageSpeed(double value)
    {
        SampleStageSetpointSpeed = value;
    }

    [RelayCommand]
    private void ToggleChiller()
    {
        ChillerIsOn = !ChillerIsOn;
    }

    [RelayCommand]
    private void ToggleSampleShutter()
    {
        SampleShutterIsOn = !SampleShutterIsOn;
    }

    [RelayCommand]
    private void ToggleTarget1Shutter()
    {
        Target1ShutterIsOn = !Target1ShutterIsOn;
    }

    [RelayCommand]
    private void ToggleTarget2Shutter()
    {
        Target2ShutterIsOn = !Target2ShutterIsOn;
    }

    [RelayCommand]
    private void ToggleHeater()
    {
        HeaterIsRunning = !HeaterIsRunning;
    }

    [RelayCommand]
    private void SetHeaterTemperature(double value)
    {
        HeaterSetpointTemperature = value;
    }

    [RelayCommand]
    private void TogglePower1()
    {
        Power1IsRunning = !Power1IsRunning;
    }

    [RelayCommand]
    private void SetPower1(double value)
    {
        Power1Setpoint = value;
    }

    [RelayCommand]
    private void TogglePower2()
    {
        Power2IsRunning = !Power2IsRunning;
    }

    [RelayCommand]
    private void SetPower2(double value)
    {
        Power2Setpoint = value;
    }

    [RelayCommand]
    private void ToggleApcMode()
    {
        ApcIsPositioningMode = !ApcIsPositioningMode;
    }

    [RelayCommand]
    private void SetApcPosition(double value)
    {
        ApcPositionSetpoint = value;
    }

    [RelayCommand]
    private void SetApcPressure(double value)
    {
        ApcPressureSetpoint = value;
    }
}
