using System.Globalization;
using System.Windows.Data;

namespace Small_square_cavity_coating_machine.Controls;

/// <summary>
/// 将数值、格式字符串和单位组合成实时显示文本。
/// </summary>
public sealed class NumericValueWithUnitConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] is not IFormattable value)
        {
            return "--";
        }

        var format = values.Length > 1 && values[1] is string requestedFormat
            ? requestedFormat
            : "G";
        var unit = values.Length > 2 ? values[2]?.ToString() : null;
        var text = value.ToString(format, culture);

        return string.IsNullOrWhiteSpace(unit) ? text : $"{text} {unit}";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
