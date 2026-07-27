using System.Globalization;
using System.Windows.Data;

namespace Small_square_cavity_coating_machine.Controls;

/// <summary>
/// 将设备名称按字符从上到下排列。
/// </summary>
public sealed class VerticalTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value?.ToString() ?? string.Empty;
        return string.Join(Environment.NewLine, text.ToCharArray());
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
