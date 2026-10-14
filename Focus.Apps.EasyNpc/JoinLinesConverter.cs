using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace Focus.Apps.EasyNpc
{
    // Joins a sequence into one string, one item per line (tooltips listing example paths).
    public class JoinLinesConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is IEnumerable items and not string ?
                string.Join(System.Environment.NewLine, items.Cast<object?>().Select(x => x?.ToString())) : value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
