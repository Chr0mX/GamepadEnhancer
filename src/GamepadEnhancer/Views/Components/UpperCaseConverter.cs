using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace GamepadEnhancer
{
    /// <summary>Card headings are shown in capitals (12 px, bold, wide tracking), whatever case the title was written in.</summary>
    internal sealed class UpperCaseConverter : IValueConverter
    {
        public static readonly UpperCaseConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value as string)?.ToUpperInvariant();
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
