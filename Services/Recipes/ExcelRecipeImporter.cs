using ExcelDataReader;
using Small_square_cavity_coating_machine.Models.Recipes;
using System.Globalization;
using System.IO;
using System.Text;

namespace Small_square_cavity_coating_machine.Services.Recipes;

public sealed class ExcelRecipeImporter : IRecipeExcelImporter
{
    private const int RequiredColumnCount = 19;
    private const int StageSpeedColumnIndex = 6;
    private const int GasStabilizationColumnIndex = 14;
    private static readonly HashSet<int> ApcColumnIndexes = [17, 18];

    static ExcelRecipeImporter()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public RecipeImportResult Import(string filePath)
    {
        try
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = ExcelReaderFactory.CreateReader(stream);

            do
            {
                var rows = ReadWorksheet(reader);
                if (rows.Any(row => row.Cells.Any(cell => !IsBlank(cell))))
                {
                    return ParseWorksheet(rows, reader.FieldCount);
                }
            }
            while (reader.NextResult());

            return Failed("工作簿中没有可读取的工作表。");
        }
        catch (Exception exception)
        {
            return Failed($"无法读取 Excel：{exception.Message}");
        }
    }

    private static List<WorksheetRow> ReadWorksheet(IExcelDataReader reader)
    {
        var rows = new List<WorksheetRow>();
        var rowNumber = 0;

        while (reader.Read())
        {
            rowNumber++;
            var values = new object?[RequiredColumnCount];
            var readableCount = Math.Min(reader.FieldCount, RequiredColumnCount);
            for (var column = 0; column < readableCount; column++)
            {
                values[column] = reader.GetValue(column);
            }

            rows.Add(new WorksheetRow(rowNumber, values));
        }

        return rows;
    }

    private static RecipeImportResult ParseWorksheet(
        IReadOnlyList<WorksheetRow> rows,
        int worksheetColumnCount)
    {
        if (worksheetColumnCount < RequiredColumnCount)
        {
            return Failed($"配方表至少需要 A-S 共 {RequiredColumnCount} 列，当前仅检测到 {worksheetColumnCount} 列。");
        }

        var layers = new List<RecipeLayer>();
        var errors = new List<RecipeImportError>();
        var usedSequences = new HashSet<int>();
        var lastSequence = 0;

        // 第一行只作为人员查看的说明表头，内容不参与字段映射或校验。
        foreach (var row in rows.Skip(1))
        {
            if (row.Cells.All(IsBlank))
            {
                continue;
            }

            var parametersAreBlank = row.Cells.Skip(1).All(IsBlank);
            if (!IsBlank(row.Cells[0]) && parametersAreBlank)
            {
                // 模板末尾常预填下一序号，这种行不属于有效配方层。
                continue;
            }

            if (!TryReadSequence(row.Cells[0], out var sequence))
            {
                errors.Add(new RecipeImportError(row.RowNumber, 1, "序号必须是正整数。"));
                continue;
            }

            if (!usedSequences.Add(sequence))
            {
                errors.Add(new RecipeImportError(row.RowNumber, 1, $"序号 {sequence} 重复。"));
            }
            else if (sequence <= lastSequence)
            {
                errors.Add(new RecipeImportError(row.RowNumber, 1, "序号必须严格递增。"));
            }

            lastSequence = sequence;

            var values = new double[RequiredColumnCount - 1];
            var rowHasParameterError = false;
            for (var columnIndex = 1; columnIndex < RequiredColumnCount; columnIndex++)
            {
                var defaultValue = columnIndex == GasStabilizationColumnIndex ? 2d : 0d;
                if (!TryReadNumber(row.Cells[columnIndex], defaultValue, out var value))
                {
                    errors.Add(new RecipeImportError(
                        row.RowNumber,
                        columnIndex + 1,
                        "参数必须是有效数字。"));
                    rowHasParameterError = true;
                    continue;
                }

                if (columnIndex != StageSpeedColumnIndex && value < 0d)
                {
                    errors.Add(new RecipeImportError(
                        row.RowNumber,
                        columnIndex + 1,
                        "除样品台转速外，工艺参数不能为负数。"));
                    rowHasParameterError = true;
                }

                if (ApcColumnIndexes.Contains(columnIndex) && value > 100d)
                {
                    errors.Add(new RecipeImportError(
                        row.RowNumber,
                        columnIndex + 1,
                        "APC 开度必须在 0-100% 之间。"));
                    rowHasParameterError = true;
                }

                values[columnIndex - 1] = value;
            }

            if (!rowHasParameterError)
            {
                layers.Add(CreateLayer(sequence, values));
            }
        }

        if (errors.Count > 0)
        {
            return new RecipeImportResult([], errors);
        }

        if (layers.Count == 0)
        {
            return Failed("工作表中没有有效的配方层。");
        }

        return new RecipeImportResult(layers, []);
    }

    private static RecipeLayer CreateLayer(int sequence, IReadOnlyList<double> values)
    {
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

    private static bool TryReadSequence(object? rawValue, out int sequence)
    {
        sequence = 0;
        if (!TryReadNumber(rawValue, 0d, out var numeric)
            || numeric <= 0d
            || numeric > int.MaxValue
            || Math.Abs(numeric - Math.Round(numeric)) > 0.0000001d)
        {
            return false;
        }

        sequence = checked((int)Math.Round(numeric));
        return true;
    }

    private static bool TryReadNumber(object? rawValue, double blankDefault, out double value)
    {
        if (IsBlank(rawValue))
        {
            value = blankDefault;
            return true;
        }

        if (rawValue is string text)
        {
            text = text.Trim();
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return false;
            }
        }
        else
        {
            try
            {
                value = Convert.ToDouble(rawValue, CultureInfo.InvariantCulture);
            }
            catch
            {
                value = 0d;
                return false;
            }
        }

        return double.IsFinite(value);
    }

    private static bool IsBlank(object? value)
        => value is null || value is DBNull || value is string text && string.IsNullOrWhiteSpace(text);

    private static RecipeImportResult Failed(string message)
        => new([], [new RecipeImportError(0, 0, message)]);

    private sealed record WorksheetRow(int RowNumber, object?[] Cells);
}
