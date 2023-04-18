﻿using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FSPreview.Controls.WPF
{
    public class TicksToTimeSpanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) { return new TimeSpan((long)value); }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { return ((TimeSpan)value).Ticks; }
    }

    public class TicksToTimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) { return new TimeSpan((long)value).ToString(@"hh\:mm\:ss"); }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { throw new NotImplementedException(); }
    }

    public class TicksToSecondsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) { return (long)value / 10000000.0; }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { return (long)((double)value * 10000000); }
    }

    public class TicksToMilliSecondsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) { return (int)((long)value / 10000); }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { return long.Parse(value.ToString()) * 10000; }
    }

    public class StringToRationalConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) { return value.ToString(); }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { return new AspectRatio(value.ToString()); }
    }

    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) { return (bool)value ? Visibility.Visible : Visibility.Collapsed; }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { throw new NotImplementedException(); }
    }
}