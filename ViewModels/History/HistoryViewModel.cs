namespace Small_square_cavity_coating_machine.ViewModels.History;

public sealed class HistoryViewModel
{
    public HistoryViewModel(
        LiveTrendViewModel liveTrend,
        ProcessTrendViewModel processTrend,
        OperationHistoryViewModel operationHistory,
        AlarmHistoryViewModel alarmHistory)
    {
        LiveTrend = liveTrend;
        ProcessTrend = processTrend;
        OperationHistory = operationHistory;
        AlarmHistory = alarmHistory;
    }

    public LiveTrendViewModel LiveTrend { get; }

    public ProcessTrendViewModel ProcessTrend { get; }

    public OperationHistoryViewModel OperationHistory { get; }

    public AlarmHistoryViewModel AlarmHistory { get; }
}
