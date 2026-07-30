using CommunityToolkit.Mvvm.ComponentModel;

namespace Small_square_cavity_coating_machine.Models.Recipes;

/// <summary>
/// 单层工艺参数。属性顺序与 Excel A-S 列以及后续 PLC 参数块保持一致。
/// </summary>
public sealed partial class RecipeLayer : ObservableObject
{
    public int Sequence { get; init; }

    public double CathodeAPower { get; init; }

    public double CathodeBPower { get; init; }

    public double PowerSpan { get; init; }

    public double IntervalSeconds { get; init; }

    public double PreSputterSeconds { get; init; }

    /// <summary>
    /// 正数表示正转，负数表示反转，0 表示停止。
    /// </summary>
    public double StageSpeedRpm { get; init; }

    public double CoatingSeconds { get; init; }

    public double IgnitionArgonSccm { get; init; }

    public double WorkingArgonSccm { get; init; }

    public double IgnitionNitrogenSccm { get; init; }

    public double WorkingNitrogenSccm { get; init; }

    public double IgnitionOxygenSccm { get; init; }

    public double WorkingOxygenSccm { get; init; }

    public double GasStabilizationSeconds { get; init; } = 2d;

    public double IgnitionPressurePa { get; init; }

    public double WorkingPressurePa { get; init; }

    public double IgnitionApcPercent { get; init; }

    public double WorkingApcPercent { get; init; }

    [ObservableProperty]
    private bool isCurrent;

    public RecipeLayer Snapshot()
    {
        return new RecipeLayer
        {
            Sequence = Sequence,
            CathodeAPower = CathodeAPower,
            CathodeBPower = CathodeBPower,
            PowerSpan = PowerSpan,
            IntervalSeconds = IntervalSeconds,
            PreSputterSeconds = PreSputterSeconds,
            StageSpeedRpm = StageSpeedRpm,
            CoatingSeconds = CoatingSeconds,
            IgnitionArgonSccm = IgnitionArgonSccm,
            WorkingArgonSccm = WorkingArgonSccm,
            IgnitionNitrogenSccm = IgnitionNitrogenSccm,
            WorkingNitrogenSccm = WorkingNitrogenSccm,
            IgnitionOxygenSccm = IgnitionOxygenSccm,
            WorkingOxygenSccm = WorkingOxygenSccm,
            GasStabilizationSeconds = GasStabilizationSeconds,
            IgnitionPressurePa = IgnitionPressurePa,
            WorkingPressurePa = WorkingPressurePa,
            IgnitionApcPercent = IgnitionApcPercent,
            WorkingApcPercent = WorkingApcPercent
        };
    }
}

public sealed record RecipeImportError(int RowNumber, int ColumnNumber, string Message)
{
    public override string ToString()
    {
        var location = RowNumber > 0
            ? $"第 {RowNumber} 行，第 {ColumnNumber} 列："
            : string.Empty;
        return $"{location}{Message}";
    }
}

public sealed record RecipeImportResult(
    IReadOnlyList<RecipeLayer> Layers,
    IReadOnlyList<RecipeImportError> Errors)
{
    public bool IsSuccessful => Errors.Count == 0;
}
