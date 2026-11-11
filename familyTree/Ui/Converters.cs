using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Mycelia.Model;

namespace Mycelia.Ui;

/// <summary>Sex to its strong or soft colour (ConverterParameter "soft").</summary>
public sealed class SexBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var sex = value is Sex s ? s : Sex.Unknown;
        return parameter as string == "soft" ? UiKit.SexSoftBrush(sex) : UiKit.SexBrush(sex);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class EmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
