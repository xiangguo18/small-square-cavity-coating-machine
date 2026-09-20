using Small_square_cavity_coating_machine.Models.Recipes;
using System.Globalization;
using System.IO;
using System.Text;

namespace Small_square_cavity_coating_machine.Services.Recipes;

public sealed class RecipeCsvService : IRecipeCsvService
{
    private const int ColumnCount = 19;
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);
    private static readonly string[] Headers =
    [
        "序号", "阴极A功率(W)", "阴极B功率(W)", "功率跨度(W)", "间隔时间(s)", "预溅射时间(s)",
        "样品台转速(rpm)", "镀膜时间(s)", "启辉Ar(Sccm)", "工作Ar(Sccm)", "启辉N₂(Sccm)",
        "工作N₂(Sccm)", "启辉O₂(Sccm)", "工作O₂(Sccm)", "气流稳定时间(s)", "启辉气压(Pa)",
        "工作气压(Pa)", "启辉APC(%)", "工作APC(%)"
    ];

    private readonly IReadOnlyList<RecipeDefinition> _definitions;

    public RecipeCsvService(IReadOnlyList<RecipeDefinition>? definitions = null) =>
        _definitions = definitions ?? RecipeDefinitions.Default;

    public void Export(string filePath, IReadOnlyList<RecipeLayer> layers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(layers);

        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("无法确定配方导出目录。");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, Utf8WithBom))
            {
                writer.WriteLine(string.Join(',', Headers.Select(Escape)));
                foreach (var layer in layers)
                {
                    var fields = new[] { layer.Sequence.ToString(CultureInfo.InvariantCulture) }
                        .Concat(RecipeLayerValues.Values(layer)
                            .Select(value => value.ToString("R", CultureInfo.InvariantCulture)));
                    writer.WriteLine(string.Join(',', fields.Select(Escape)));
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

    public RecipeImportResult Import(string filePath)
    {
        try
        {
            using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var header = reader.ReadLine() ?? throw new InvalidDataException("CSV文件为空，未找到配方表头。");
            if (!ParseLine(header).SequenceEqual(Headers, StringComparer.Ordinal))
            {
                return Failed($"CSV列不兼容。需要列：{string.Join(", ", Headers)}");
            }

            var layers = new List<RecipeLayer>();
            var errors = new List<RecipeImportError>();
            var sequences = new HashSet<int>();
            var lastSequence = 0;
            var rowNumber = 1;
            while (reader.ReadLine() is { } line)
            {
                rowNumber++;
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var fields = ParseLine(line);
                if (fields.Count != ColumnCount)
                {
                    errors.Add(new RecipeImportError(rowNumber, 0, $"CSV列数错误，应为{ColumnCount}列。"));
                    continue;
                }

                if (!TryReadSequence(fields[0], out var sequence))
                {
                    errors.Add(new RecipeImportError(rowNumber, 1, "序号必须是正整数。"));
                    continue;
                }

                var rowHasError = false;
                if (!sequences.Add(sequence))
                {
                    errors.Add(new RecipeImportError(rowNumber, 1, $"序号 {sequence} 重复。"));
                    rowHasError = true;
                }
                else if (sequence <= lastSequence)
                {
                    errors.Add(new RecipeImportError(rowNumber, 1, "序号必须严格递增。"));
                    rowHasError = true;
                }
                lastSequence = sequence;

                var values = new double[ColumnCount - 1];
                for (var columnIndex = 1; columnIndex < ColumnCount; columnIndex++)
                {
                    var blankDefault = columnIndex == 14 ? 2d : 0d;
                    if (!TryReadNumber(fields[columnIndex], blankDefault, out var value))
                    {
                        errors.Add(new RecipeImportError(rowNumber, columnIndex + 1, "参数必须是有效数字。"));
                        rowHasError = true;
                        continue;
                    }

                    try { RecipeDefinitions.Validate(value, _definitions[columnIndex - 1]); }
                    catch (InvalidOperationException exception)
                    {
                        errors.Add(new RecipeImportError(rowNumber, columnIndex + 1, exception.Message));
                        rowHasError = true;
                    }
                    values[columnIndex - 1] = value;
                }

                if (!rowHasError)
                {
                    layers.Add(RecipeLayerValues.Create(sequence, values));
                }
            }

            if (errors.Count > 0)
            {
                return new RecipeImportResult([], errors);
            }

            return layers.Count == 0
                ? Failed("CSV中没有有效的配方层。")
                : new RecipeImportResult(layers, []);
        }
        catch (Exception exception)
        {
            return Failed($"无法读取 CSV：{exception.Message}");
        }
    }

    private static bool TryReadSequence(string value, out int sequence)
    {
        sequence = 0;
        return TryReadNumber(value, 0d, out var numeric)
               && numeric > 0d
               && numeric <= int.MaxValue
               && Math.Abs(numeric - Math.Round(numeric)) <= 0.0000001d
               && (sequence = checked((int)Math.Round(numeric))) > 0;
    }

    private static bool TryReadNumber(string value, double blankDefault, out double number)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            number = blankDefault;
            return true;
        }

        return (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                || double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out number))
               && double.IsFinite(number);
    }

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\r') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    private static IReadOnlyList<string> ParseLine(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == ',' && !quoted)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(character);
            }
        }

        if (quoted)
        {
            throw new InvalidDataException("CSV包含未闭合的引号。");
        }

        fields.Add(field.ToString());
        return fields;
    }

    private static RecipeImportResult Failed(string message) =>
        new([], [new RecipeImportError(0, 0, message)]);
}

internal static class RecipeLayerValues
{
    public static double[] Values(RecipeLayer layer) =>
    [
        layer.CathodeAPower, layer.CathodeBPower, layer.PowerSpan, layer.IntervalSeconds, layer.PreSputterSeconds,
        layer.StageSpeedRpm, layer.CoatingSeconds, layer.IgnitionArgonSccm, layer.WorkingArgonSccm,
        layer.IgnitionNitrogenSccm, layer.WorkingNitrogenSccm, layer.IgnitionOxygenSccm, layer.WorkingOxygenSccm,
        layer.GasStabilizationSeconds, layer.IgnitionPressurePa, layer.WorkingPressurePa,
        layer.IgnitionApcPercent, layer.WorkingApcPercent
    ];

    public static RecipeLayer Create(int sequence, IReadOnlyList<double> values)
    {
        if (values.Count != 18)
        {
            throw new ArgumentException("配方参数必须包含18项。", nameof(values));
        }

        return new RecipeLayer
        {
            Sequence = sequence,
            CathodeAPower = values[0],
            CathodeBPower = values[1],
            PowerSpan = values[2],
            IntervalSeconds = values[3],
            PreSputterSeconds = values[4],
            StageSpeedRpm = values[5],
            CoatingSeconds = values[6],
            IgnitionArgonSccm = values[7],
            WorkingArgonSccm = values[8],
            IgnitionNitrogenSccm = values[9],
            WorkingNitrogenSccm = values[10],
            IgnitionOxygenSccm = values[11],
            WorkingOxygenSccm = values[12],
            GasStabilizationSeconds = values[13],
            IgnitionPressurePa = values[14],
            WorkingPressurePa = values[15],
            IgnitionApcPercent = values[16],
            WorkingApcPercent = values[17]
        };
    }
}
