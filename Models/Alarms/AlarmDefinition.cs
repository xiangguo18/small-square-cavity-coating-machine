namespace Small_square_cavity_coating_machine.Models.Alarms;

public sealed record AlarmDefinition(int Id, string Name, int Priority, string Address,
    string Message, string Chinese, string English)
{
    public string Severity => Priority switch { 7 => "报警", 6 => "警告", _ => $"等级 {Priority}" };

    public string DisplayMessage
    {
        get
        {
            var texts = new[] { Chinese, Message, English };
            var text = texts.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)
                && value.Trim() != "空" && !value.Trim().Equals("Space", StringComparison.OrdinalIgnoreCase));
            return text?.Trim() ?? (texts.Any(value => value.Trim() == "空"
                || value.Trim().Equals("Space", StringComparison.OrdinalIgnoreCase))
                ? $"预留报警点触发：{Address}" : Name);
        }
    }
}

public enum AlarmQuality { Unknown, Good, Bad }

public sealed record AlarmSignal(string Address, bool? IsActive, AlarmQuality Quality,
    DateTimeOffset ObservedAt, DateTimeOffset? SourceTimestamp = null);

public sealed record AlarmConnection(bool IsConnected, string Message);
