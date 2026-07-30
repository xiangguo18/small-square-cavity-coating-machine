namespace Small_square_cavity_coating_machine.Models.History;

public sealed record OperationLogRecord(
    DateTimeOffset Timestamp,
    string UserName,
    string Target,
    string Action,
    string SetValue,
    bool IsSuccessful,
    bool TriggeredAlarm,
    string FailureReason,
    bool IsSimulated)
{
    public string ResultText => IsSuccessful ? "成功" : "失败";

    public string AlarmText => TriggeredAlarm ? "是" : "否";

    public string DataSourceText => IsSimulated ? "模拟" : "实际";
}
