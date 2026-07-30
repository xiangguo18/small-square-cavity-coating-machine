namespace Small_square_cavity_coating_machine.Models.History;

/// <summary>
/// 一次完整的设备遥测采样。后续 OPC UA 数据源也应输出此模型。
/// </summary>
public sealed record TelemetrySample(
    DateTimeOffset Timestamp,
    string SessionId,
    string RecipeRunId,
    string RecipeName,
    double HighVacuumPa,
    double FilmHighVacuumPa,
    double Power1VoltageV,
    double Power1CurrentA,
    double Power2VoltageV,
    double Power2CurrentA,
    double TemperatureC,
    string DataQuality);
