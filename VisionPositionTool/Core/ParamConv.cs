using System;
using System.Globalization;
using HalconDotNet;

namespace VisionPositionTool.Core
{
    /// <summary>
    /// 界面参数字符串 → HALCON 参数转换。
    /// 界面参数是字符串（"auto" / "50"），而 create_shape_model 等算子的数字参数
    /// 只接受数字，传数字字符串会报 #1202/#1307/#1301（Wrong type/value of control parameter）。
    /// 可解析为数字则转数字，否则（"auto" 等）原样传字符串。
    /// </summary>
    internal static class ParamConv
    {
        /// <summary>浮点参数（contrast / min_contrast 等）</summary>
        public static HTuple Num(string s)
        {
            s = (s ?? string.Empty).Trim();
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return v;
            return s;
        }

        /// <summary>整数参数（num_levels 等）：HALCON 要求 integer，传 double 报 #1201</summary>
        public static HTuple Int(string s)
        {
            s = (s ?? string.Empty).Trim();
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                return v;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                return (int)Math.Round(d);
            return s;
        }

        /// <summary>
        /// 角度参数（angle_step，UI 为度）：数字转弧度（HALCON 角度一律弧度），
        /// 字符串（"auto"）原样传。注意不能用 Int——步长允许小数（如 0.5°）。
        /// </summary>
        public static HTuple Rad(string s)
        {
            s = (s ?? string.Empty).Trim();
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return v * Math.PI / 180.0;
            return s;
        }
    }
}
