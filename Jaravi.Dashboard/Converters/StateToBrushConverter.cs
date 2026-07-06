using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Jaravi.Core.Models;

namespace Jaravi.Dashboard.Converters;

public sealed class StateToBrushConverter : IValueConverter
{
    private static readonly Brush Running = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly Brush Waiting = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00));
    private static readonly Brush Completed = new SolidColorBrush(Color.FromRgb(0x60, 0x7D, 0x8B));
    private static readonly Brush Failed = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
    private static readonly Brush Killed = new SolidColorBrush(Color.FromRgb(0x8E, 0x24, 0xAA));
    private static readonly Brush Other = new SolidColorBrush(Color.FromRgb(0x90, 0xA4, 0xAE));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value switch
        {
            SessionState.Running => Running,
            SessionState.WaitingInput => Waiting,
            SessionState.Completed => Completed,
            SessionState.Failed => Failed,
            SessionState.Killed => Killed,
            _ => Other,
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
