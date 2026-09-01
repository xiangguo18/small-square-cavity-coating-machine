using System.Globalization;
using Small_square_cavity_coating_machine.Models.Equipment;

namespace Small_square_cavity_coating_machine.Services.Equipment;

public static class ParameterValueCodec
{
    public static bool IsNumeric(Type? type) => type == typeof(float) || type == typeof(double)
        || type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
        || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);
    public static bool IsValid(object? value) => value is not null && IsNumeric(value.GetType())
        && (value is not float f || float.IsFinite(f)) && (value is not double d || double.IsFinite(d));
    public static string Format(object? value) => value switch {
        null => "—", bool b => b ? "true" : "false", float f => f.ToString("R", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        IFormattable n => n.ToString(null, CultureInfo.InvariantCulture), _ => "—" };
    public static bool Same(object? a, object? b) => a is not null && b is not null && a.GetType() == b.GetType() && a.Equals(b);

    public static object Parse(string text, Type type, ParameterDefinition definition)
    {
        if (!IsNumeric(type)) throw new InvalidOperationException("PLC参数不是受支持的数值类型");
        object value;
        if (type == typeof(float) || type == typeof(double))
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                || !double.IsFinite(number)) throw new InvalidOperationException("请输入有限数值，不允许NaN或无穷大");
            if (number < definition.MinValue || number > definition.MaxValue) throw RangeError(definition);
            value = type == typeof(float) ? (object)(float)number : number;
        }
        else
        {
            if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                || number != decimal.Truncate(number)) throw new InvalidOperationException("此PLC参数为整数，不允许小数");
            if (number < (decimal)definition.MinValue || number > (decimal)definition.MaxValue) throw RangeError(definition);
            try { value = Convert.ChangeType(number, type, CultureInfo.InvariantCulture); }
            catch (OverflowException) { throw new InvalidOperationException("数值超出PLC数据类型范围"); }
        }
        var converted = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        if (!double.IsFinite(converted) || converted < definition.MinValue || converted > definition.MaxValue)
            throw RangeError(definition);
        return value;
    }

    private static InvalidOperationException RangeError(ParameterDefinition d) =>
        new($"允许范围：{d.MinValue}～{d.MaxValue} {d.Unit}");
}
