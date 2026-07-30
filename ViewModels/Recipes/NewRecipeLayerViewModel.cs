using CommunityToolkit.Mvvm.ComponentModel;
using Small_square_cavity_coating_machine.Models.Recipes;
using System.Globalization;

namespace Small_square_cavity_coating_machine.ViewModels.Recipes;

public sealed partial class RecipeParameterInput : ObservableObject
{
    public RecipeParameterInput(
        string label,
        string unit,
        double defaultValue = 0d,
        bool allowsNegative = false,
        double? maximum = null,
        string hint = "")
    {
        Label = label;
        Unit = unit;
        AllowsNegative = allowsNegative;
        Maximum = maximum;
        Hint = hint;
        ValueText = defaultValue.ToString("0.###", CultureInfo.InvariantCulture);
    }

    public string Label { get; }

    public string Unit { get; }

    public bool AllowsNegative { get; }

    public double? Maximum { get; }

    public string Hint { get; }

    [ObservableProperty]
    private string valueText;
}

public sealed class NewRecipeLayerViewModel
{
    private readonly RecipeParameterInput[] _orderedInputs;

    public NewRecipeLayerViewModel(int sequence)
    {
        Sequence = sequence;

        PowerAndTimeInputs =
        [
            new("阴极A功率", "W"),
            new("阴极B功率", "W"),
            new("功率跨度", "W"),
            new("间隔时间", "s"),
            new("预溅射时间", "s"),
            new(
                "样品台转速",
                "rpm",
                allowsNegative: true,
                hint: "正数正转，负数反转，0停止"),
            new("镀膜时间", "s")
        ];

        GasInputs =
        [
            new("启辉Ar", "Sccm"),
            new("工作Ar", "Sccm"),
            new("启辉N₂", "Sccm"),
            new("工作N₂", "Sccm"),
            new("启辉O₂", "Sccm"),
            new("工作O₂", "Sccm"),
            new("气流稳定时间", "s", 2d)
        ];

        PressureAndApcInputs =
        [
            new("启辉气压", "Pa"),
            new("工作气压", "Pa"),
            new("启辉APC", "%", maximum: 100d),
            new("工作APC", "%", maximum: 100d)
        ];

        _orderedInputs =
        [
            .. PowerAndTimeInputs,
            .. GasInputs,
            .. PressureAndApcInputs
        ];
    }

    public int Sequence { get; }

    public IReadOnlyList<RecipeParameterInput> PowerAndTimeInputs { get; }

    public IReadOnlyList<RecipeParameterInput> GasInputs { get; }

    public IReadOnlyList<RecipeParameterInput> PressureAndApcInputs { get; }

    public bool TryBuildLayer(out RecipeLayer? layer, out string error)
    {
        layer = null;
        error = string.Empty;
        var values = new double[_orderedInputs.Length];

        for (var index = 0; index < _orderedInputs.Length; index++)
        {
            var input = _orderedInputs[index];
            if (!TryParseNumber(input.ValueText, out var value))
            {
                error = $"“{input.Label}”必须是有效数字。";
                return false;
            }

            if (!input.AllowsNegative && value < 0d)
            {
                error = $"“{input.Label}”不能为负数。";
                return false;
            }

            if (input.Maximum is { } maximum && value > maximum)
            {
                error = $"“{input.Label}”不能大于 {maximum:0.###}{input.Unit}。";
                return false;
            }

            values[index] = value;
        }

        layer = new RecipeLayer
        {
            Sequence = Sequence,
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
        return true;
    }

    private static bool TryParseNumber(string text, out double value)
    {
        text = text?.Trim() ?? string.Empty;
        return (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
               && double.IsFinite(value);
    }
}
