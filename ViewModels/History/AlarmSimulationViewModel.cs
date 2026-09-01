using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Small_square_cavity_coating_machine.Models.Alarms;
using Small_square_cavity_coating_machine.Services.Alarms;

namespace Small_square_cavity_coating_machine.ViewModels.History;

public sealed partial class AlarmSimulationViewModel : ObservableObject
{
    private readonly ISimulatedAlarmControl _source;
    public AlarmSimulationViewModel(ISimulatedAlarmControl source, IReadOnlyList<AlarmDefinition> definitions)
    {
        _source = source;
        Definitions = definitions;
        SelectedDefinition = definitions.FirstOrDefault();
    }
    public IReadOnlyList<AlarmDefinition> Definitions { get; }
    [ObservableProperty] private AlarmDefinition? selectedDefinition;
    [RelayCommand] private void Raise() { if (SelectedDefinition is { } d) _source.Set(d.Address, true); }
    [RelayCommand] private void Clear() { if (SelectedDefinition is { } d) _source.Set(d.Address, false); }
    [RelayCommand] private void BadQuality() { if (SelectedDefinition is { } d) _source.SetBadQuality(d.Address); }
    [RelayCommand] private void Disconnect() => _source.Disconnect();
    [RelayCommand] private void Reconnect() => _source.Connect();
}
