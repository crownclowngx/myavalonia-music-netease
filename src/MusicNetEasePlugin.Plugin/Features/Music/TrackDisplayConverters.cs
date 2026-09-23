using System.Globalization;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Data.Converters;

namespace MusicNetEasePlugin.Features.Music;

/// <summary>只格式化已有歌曲元数据；不查询网络，也不将整曲时长当作试听可播放时长。</summary>
public sealed class TrackDurationConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is long milliseconds && milliseconds > 0
        ? $"{milliseconds / 60000:00}:{milliseconds / 1000 % 60:00}" : "—";
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// 表头与可见行共享列宽描述，但不能共享可变的 ColumnDefinitions 实例。
/// 每个 Grid 从同一字符串创建自己的定义，仅在宽度模式变化时重新转换。
/// </summary>
public sealed class TrackRowLayout : AvaloniaObject
{
    public static readonly AttachedProperty<string> ColumnsProperty =
        AvaloniaProperty.RegisterAttached<TrackRowLayout, Grid, string>("Columns", "*");
    static TrackRowLayout() => ColumnsProperty.Changed.AddClassHandler<Grid>((grid, args) =>
        grid.ColumnDefinitions = new ColumnDefinitions(args.NewValue as string ?? "*"));
    public static string GetColumns(Grid grid) => grid.GetValue(ColumnsProperty);
    public static void SetColumns(Grid grid, string value) => grid.SetValue(ColumnsProperty, value);
}
