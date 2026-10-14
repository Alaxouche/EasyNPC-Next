using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace Focus.Apps.EasyNpc.Profiles
{
    // True when the row's NPC (values[0]) is in the excluded set (values[1]); drives the grid's "excluded" row style.
    public class NpcExclusionConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            return values.Length >= 2 && values[0] is IRecordKey key && values[1] is IReadOnlySet<IRecordKey> set &&
                set.Contains(key);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
