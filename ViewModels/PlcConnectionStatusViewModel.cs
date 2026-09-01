using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Small_square_cavity_coating_machine.Models;
using Small_square_cavity_coating_machine.Services.Equipment;
using Small_square_cavity_coating_machine.Services.Alarms;

namespace Small_square_cavity_coating_machine.ViewModels;

public sealed partial class PlcConnectionStatusViewModel : ObservableObject, IDisposable
{
    private readonly IAlarmConnectionController? _controller;
    private readonly IUiDispatcher _dispatcher;
    private bool _disposed;

    public PlcConnectionStatusViewModel(IAlarmConnectionController? controller, IUiDispatcher dispatcher)
    {
        _controller = controller;
        _dispatcher = dispatcher;
        if (controller is not null) controller.StateChanged += OnStateChanged;
        Refresh();
        DraftEndpoint = EndpointUrl;
    }

    public event EventHandler? CloseRequested;
    [ObservableProperty] private string endpointUrl = OpcUaAlarmOptions.DefaultEndpoint;
    [ObservableProperty] private string draftEndpoint = OpcUaAlarmOptions.DefaultEndpoint;
    [ObservableProperty] private string statusText = "PLC未连接";
    [ObservableProperty] private string detailText = string.Empty;
    [ObservableProperty] private string validationError = string.Empty;
    [ObservableProperty] private PlcConnectionState connectionState;
    [ObservableProperty] private bool isConnecting;
    [ObservableProperty] private bool isApplying;
    [ObservableProperty] private bool isWriting;
    private bool IsSimulated => _controller is IOpcUaEquipmentClient { IsSimulated: true };
    public bool CanApply => _controller is not null && !IsConnecting && !IsApplying && !IsWriting && !IsSimulated;
    partial void OnIsWritingChanged(bool value) { OnPropertyChanged(nameof(CanApply)); ApplyCommand.NotifyCanExecuteChanged(); }

    partial void OnIsConnectingChanged(bool value) { OnPropertyChanged(nameof(CanApply)); ApplyCommand.NotifyCanExecuteChanged(); }
    partial void OnIsApplyingChanged(bool value) { OnPropertyChanged(nameof(CanApply)); ApplyCommand.NotifyCanExecuteChanged(); }

    [RelayCommand] private void BeginEdit()
    {
        DraftEndpoint = EndpointUrl;
        ValidationError = string.Empty;
    }

    [RelayCommand] private void Cancel()
    {
        BeginEdit();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        ValidationError = string.Empty;
        try
        {
            var endpoint = OpcUaAlarmOptions.ValidateEndpoint(DraftEndpoint);
            IsApplying = true;
            await _controller!.ApplyEndpointAsync(endpoint);
            DraftEndpoint = endpoint;
        }
        catch (Exception ex) { ValidationError = ex.Message; }
        finally { IsApplying = false; }
    }

    private void OnStateChanged(object? sender, EventArgs args) => _dispatcher.Post(() => { if (!_disposed) Refresh(); });

    private void Refresh()
    {
        var state = _controller?.Status;
        EndpointUrl = state?.EndpointUrl ?? OpcUaAlarmOptions.DefaultEndpoint;
        IsWriting = _controller?.IsWriteInProgress == true;
        IsConnecting = state?.Phase == AlarmConnectionPhase.Connecting;
        StatusText = state?.Phase switch
        {
            AlarmConnectionPhase.Connected => "PLC已连接",
            AlarmConnectionPhase.Connecting => "正在连接PLC…",
            AlarmConnectionPhase.Failed => "PLC连接失败",
            _ => "PLC未连接"
        };
        ConnectionState = state?.Phase switch
        {
            AlarmConnectionPhase.Connected => PlcConnectionState.Connected,
            AlarmConnectionPhase.Connecting => PlcConnectionState.Connecting,
            AlarmConnectionPhase.Failed => PlcConnectionState.ConnectionFailed,
            _ => PlcConnectionState.Disconnected
        };
        if (IsSimulated) { StatusText = "模拟设备（PLC未连接）"; ConnectionState = PlcConnectionState.Disconnected; }
        DetailText = state?.Message ?? "当前为模拟模式或报警定义初始化失败；真实连接不可用。模拟模式请去掉--simulate-alarms后重启。";
    }

    public void Dispose()
    {
        _disposed = true;
        if (_controller is not null) _controller.StateChanged -= OnStateChanged;
    }
}
