using System.Globalization;
using System.Windows.Data;
using Domain.Objects;
using Domain.Versions;

namespace Shell.Converters;

public sealed class EnumDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ObjectType.Assembly => "Сборка",
        ObjectType.Part => "Деталь",
        ObjectType.StandardPart => "Стандартное изделие",
        ObjectVersionType.InWork => "В работе",
        ObjectVersionType.Approved => "Утверждена",
        ObjectVersionType.Annulled => "Аннулирована",
        _ => "—"
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException("Обратное преобразование названий типов и состояний не поддерживается.");
}
