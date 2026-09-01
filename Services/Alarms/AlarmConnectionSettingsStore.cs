using System.IO;
using System.Text.Json;

namespace Small_square_cavity_coating_machine.Services.Alarms;

public interface IAlarmConnectionSettingsStore
{
    OpcUaAlarmOptions Load();
    Task SaveAsync(OpcUaAlarmOptions options, CancellationToken token);
}

public sealed class AlarmConnectionSettingsStore(string path) : IAlarmConnectionSettingsStore
{
    public OpcUaAlarmOptions Load()
    {
        if (!File.Exists(path)) return new OpcUaAlarmOptions();
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        // Never silently downgrade an existing secured/manual connection.
        if (document.RootElement.TryGetProperty("SecurityMode", out var security)
            && security.GetString() != "None")
            throw new InvalidOperationException("旧配置使用安全连接；请在连接弹窗确认新的匿名无安全连接地址");
        var result = document.Deserialize<OpcUaAlarmOptions>()
            ?? throw new InvalidOperationException("报警连接配置为空");
        result.Validate();
        return result;
    }

    public async Task SaveAsync(OpcUaAlarmOptions options, CancellationToken token)
    {
        options.Validate();
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".alarms-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, options, new JsonSerializerOptions { WriteIndented = true }, token);
                await stream.FlushAsync(token);
                stream.Flush(true);
            }
            token.ThrowIfCancellationRequested();
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
