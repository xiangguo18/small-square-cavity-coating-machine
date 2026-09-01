namespace Small_square_cavity_coating_machine.Models.History;

public sealed record ProcessTrendRun(Guid RunId, string RecipeName, DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt, string Status, string FailureReason, bool IsSimulated,
    string DefinitionsJson, int SampleCount = 0, string ExportPath = "", string RecordingError = "")
{
    public string RecordingStatus => RecordingError.Length > 0 ? "记录失败：" + RecordingError
        : Status == "程序中断" ? "已保留中断前数据"
        : EndedAt is null ? "记录中" : ExportPath.Length > 0 ? "已归档并导出CSV" : "已保存";
}
