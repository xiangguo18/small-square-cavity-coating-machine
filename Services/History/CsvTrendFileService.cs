using System.Globalization;
using System.IO;
using System.Text;
using Small_square_cavity_coating_machine.Models.History;

namespace Small_square_cavity_coating_machine.Services.History;

public sealed class CsvTrendFileService : ITrendFileService
{
    private static readonly string[] Headers =
    [
        "Timestamp",
        "SessionId",
        "RecipeRunId",
        "RecipeName",
        "HighVacuumPa",
        "FilmHighVacuumPa",
        "Power1VoltageV",
        "Power1CurrentA",
        "Power2VoltageV",
        "Power2CurrentA",
        "TemperatureC",
        "DataQuality"
    ];

    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public async Task SaveAsync(
        string path,
        IReadOnlyList<TelemetrySample> samples,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("无法确定保存目录。");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             useAsync: true))
            await using (var writer = new StreamWriter(stream, Utf8WithBom))
            {
                await writer.WriteLineAsync(string.Join(',', Headers)).ConfigureAwait(false);

                foreach (var sample in samples)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fields = new[]
                    {
                        sample.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                        sample.SessionId,
                        sample.RecipeRunId,
                        sample.RecipeName,
                        sample.HighVacuumPa.ToString("R", CultureInfo.InvariantCulture),
                        sample.FilmHighVacuumPa.ToString("R", CultureInfo.InvariantCulture),
                        sample.Power1VoltageV.ToString("R", CultureInfo.InvariantCulture),
                        sample.Power1CurrentA.ToString("R", CultureInfo.InvariantCulture),
                        sample.Power2VoltageV.ToString("R", CultureInfo.InvariantCulture),
                        sample.Power2CurrentA.ToString("R", CultureInfo.InvariantCulture),
                        sample.TemperatureC.ToString("R", CultureInfo.InvariantCulture),
                        sample.DataQuality
                    };

                    await writer.WriteLineAsync(string.Join(',', fields.Select(Escape))).ConfigureAwait(false);
                }
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<IReadOnlyList<TelemetrySample>> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var samples = new List<TelemetrySample>();
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var headerLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("文件为空，未找到CSV表头。");
        var actualHeaders = ParseLine(headerLine);
        if (!actualHeaders.SequenceEqual(Headers, StringComparer.Ordinal))
        {
            throw new InvalidDataException($"CSV列不兼容。需要列：{string.Join(", ", Headers)}");
        }

        var lineNumber = 1;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = ParseLine(line);
            if (fields.Count != Headers.Length)
            {
                throw new InvalidDataException($"第{lineNumber}行列数错误，应为{Headers.Length}列。");
            }

            try
            {
                samples.Add(new TelemetrySample(
                    DateTimeOffset.ParseExact(fields[0], "O", CultureInfo.InvariantCulture),
                    fields[1],
                    fields[2],
                    fields[3],
                    ParseDouble(fields[4]),
                    ParseDouble(fields[5]),
                    ParseDouble(fields[6]),
                    ParseDouble(fields[7]),
                    ParseDouble(fields[8]),
                    ParseDouble(fields[9]),
                    ParseDouble(fields[10]),
                    fields[11]));
            }
            catch (Exception exception) when (exception is FormatException or OverflowException)
            {
                throw new InvalidDataException($"第{lineNumber}行包含无效的时间或数值。", exception);
            }
        }

        return samples;
    }

    private static double ParseDouble(string value) =>
        double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static string Escape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\r') && !value.Contains('\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static IReadOnlyList<string> ParseLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var insideQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (insideQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    insideQuotes = !insideQuotes;
                }
            }
            else if (character == ',' && !insideQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        if (insideQuotes)
        {
            throw new InvalidDataException("CSV包含未闭合的引号。");
        }

        fields.Add(current.ToString());
        return fields;
    }
}
