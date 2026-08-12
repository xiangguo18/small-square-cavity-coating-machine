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

public sealed partial class NewRecipeLayerViewModel : ObservableObject
{
    private readonly RecipeParameterInput[] _commonInputs;

    [ObservableProperty]
    private string sequenceText;

    [ObservableProperty]
    private RecipePressureControlMode pressureControlMode = RecipePressureControlMode.Pressure;

    public NewRecipeLayerViewModel(int sequence)
    {
        MaximumSequence = sequence;
        SequenceText = sequence.ToString(CultureInfo.InvariantCulture);

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

        PressureInputs =
        [
            new("启辉气压", "Pa"),
            new("工作气压", "Pa")
        ];

        ApcInputs =
        [
            new("启辉APC", "%", maximum: 100d),
            new("工作APC", "%", maximum: 100d)
        ];

        _commonInputs =
        [
            .. PowerAndTimeInputs,
            .. GasInputs
        ];
    }

    public int MaximumSequence { get; }

    public IReadOnlyList<RecipeParameterInput> PowerAndTimeInputs { get; }

    public IReadOnlyList<RecipeParameterInput> GasInputs { get; }

    public IReadOnlyList<RecipeParameterInput> PressureInputs { get; }

    public IReadOnlyList<RecipeParameterInput> ApcInputs { get; }

    public bool IsPressureMode
    {
        get => PressureControlMode == RecipePressureControlMode.Pressure;
        set
        {
            if (value)
            {
                PressureControlMode = RecipePressureControlMode.Pressure;
            }
        }
    }

    public bool IsApcMode
    {
        get => PressureControlMode == RecipePressureControlMode.ApcPosition;
        set
        {
            if (value)
            {
                PressureControlMode = RecipePressureControlMode.ApcPosition;
            }
        }
    }

    public bool TryBuildLayer(out RecipeLayer? layer, out string error)
    {
        layer = null;
        error = string.Empty;

        if (!int.TryParse(SequenceText?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var sequence)
            || sequence < 1
            || sequence > MaximumSequence)
        {
            error = $"“插入序号”必须是 1-{MaximumSequence} 之间的整数。";
            return false;
        }

        var commonValues = new double[_commonInputs.Length];

        if (!TryReadInputs(_commonInputs, commonValues, out error))
        {
            return false;
        }

        var modeInputs = IsPressureMode ? PressureInputs : ApcInputs;
        var modeValues = new double[2];
        if (!TryReadInputs(modeInputs, modeValues, out error))
        {
            return false;
        }

        layer = new RecipeLayer
        {
            Sequence = sequence,
            CathodeAPower = commonValues[0],
            CathodeBPower = commonValues[1],
            PowerSpan = commonValues[2],
            IntervalSeconds = commonValues[3],
            PreSputterSeconds = commonValues[4],
            StageSpeedRpm = commonValues[5],
            CoatingSeconds = commonValues[6],
            IgnitionArgonSccm = commonValues[7],
            WorkingArgonSccm = commonValues[8],
            IgnitionNitrogenSccm = commonValues[9],
            WorkingNitrogenSccm = commonValues[10],
            IgnitionOxygenSccm = commonValues[11],
            WorkingOxygenSccm = commonValues[12],
            GasStabilizationSeconds = commonValues[13],
            IgnitionPressurePa = IsPressureMode ? modeValues[0] : 0d,
            WorkingPressurePa = IsPressureMode ? modeValues[1] : 0d,
            IgnitionApcPercent = IsApcMode ? modeValues[0] : 0d,
            WorkingApcPercent = IsApcMode ? modeValues[1] : 0d,
            PressureControlMode = PressureControlMode
        };
        return true;
    }

    partial void OnPressureControlModeChanged(RecipePressureControlMode value)
    {
        OnPropertyChanged(nameof(IsPressureMode));
        OnPropertyChanged(nameof(IsApcMode));
    }

    private static bool TryReadInputs(
        IReadOnlyList<RecipeParameterInput> inputs,
        double[] values,
        out string error)
    {
        error = string.Empty;
        for (var index = 0; index < inputs.Count; index++)
        {
            var input = inputs[index];
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
