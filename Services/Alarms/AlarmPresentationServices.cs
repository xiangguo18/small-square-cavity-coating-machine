using System.Windows.Threading;

namespace Small_square_cavity_coating_machine.Services.Alarms;

public interface IUiDispatcher { void Post(Action action); }

public sealed class WpfUiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public void Post(Action action)
    {
        if (dispatcher.HasShutdownStarted) return;
        if (dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action, DispatcherPriority.DataBind);
    }
}

public interface IAlarmNavigationService { void ShowAlarmHistory(); }

public sealed class AlarmNavigationService : IAlarmNavigationService
{
    public event EventHandler? HistoryRequested;
    public void ShowAlarmHistory() => HistoryRequested?.Invoke(this, EventArgs.Empty);
}
