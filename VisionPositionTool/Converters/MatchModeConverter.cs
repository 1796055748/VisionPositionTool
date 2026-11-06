using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VisionPositionTool.Converters
{
    /// <summary>
    /// 匹配方式选择转换器：MatchMode("shape"/"ncc"/"gray"/"fit") 与 ConverterParameter 相等 →
    /// 目标为 bool 时返回 true（RadioButton IsChecked），为 Visibility 时返回 Visible（参数面板/结果表）。
    /// ConvertBack：RadioButton 选中(true)时把 parameter 写回 MatchMode；取消选中不覆盖（避免互斥清除）。
    /// </summary>
    public class MatchModeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool eq = parameter != null &&
                      string.Equals(value as string, parameter.ToString(), StringComparison.Ordinal);
            if (targetType == typeof(Visibility))
                return eq ? Visibility.Visible : Visibility.Collapsed;
            return eq;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b && parameter != null)
                return parameter.ToString();
            return Binding.DoNothing;
        }
    }
}
