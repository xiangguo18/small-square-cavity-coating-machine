using System.Globalization;
using System.Windows.Data;

namespace Small_square_cavity_coating_machine.Controls;

/// <summary>
/// Converts a percentage value to a clamped ratio between 0 and 1.
/// </summary>
public sealed class PercentageToRatioConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (value is not double percentage || !double.IsFinite(percentage))
        {
            return 0d;
        }

        return Math.Clamp(percentage, 0d, 100d) / 100d;
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
