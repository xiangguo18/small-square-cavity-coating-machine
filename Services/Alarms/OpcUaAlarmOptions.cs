namespace Small_square_cavity_coating_machine.Services.Alarms;

/// <summary>Confirmed contract: anonymous None, full EQ_Alarm array, true/1 active.</summary>
public sealed record OpcUaAlarmOptions
{
    public const string DefaultEndpoint = "opc.tcp://192.168.0.10:4840";
    public bool Enabled { get; init; } = true;
    public string EndpointUrl { get; init; } = DefaultEndpoint;
    public string ArrayBrowseName { get; init; } = "EQ_Alarm";
    public int SamplingIntervalMs { get; init; } = 100;
    public int PublishingIntervalMs { get; init; } = 250;
    public int ReconnectDelayMs { get; init; } = 5000;

    public static string ValidateEndpoint(string text)
    {
        var value = text.Trim();
        if (value.Any(char.IsWhiteSpace) || value.Contains('`') || value.Contains('\\')
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "opc.tcp"
            || string.IsNullOrEmpty(uri.Host) || uri.Port is < 1 or > 65535
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("请输入完整地址，例如 opc.tcp://192.168.0.10:4840（必须包含端口）");
        return value;
    }

    public void Validate()
    {
        ValidateEndpoint(EndpointUrl);
        if (ArrayBrowseName != "EQ_Alarm") throw new InvalidOperationException("本机已确认的报警数组名称为EQ_Alarm");
        if (SamplingIntervalMs < 20 || PublishingIntervalMs < 20 || ReconnectDelayMs < 1000)
            throw new InvalidOperationException("订阅/重连周期配置无效");
    }
}
