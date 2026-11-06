namespace VisionPositionTool.Models
{
    /// <summary>单次匹配结果（一行）</summary>
    public class MatchResult
    {
        /// <summary>序号</summary>
        public int Index { get; set; }

        /// <summary>行坐标（像素）</summary>
        public double Row { get; set; }

        /// <summary>列坐标（像素）</summary>
        public double Col { get; set; }

        /// <summary>角度（度）</summary>
        public double Angle { get; set; }

        /// <summary>匹配分数</summary>
        public double Score { get; set; }

        // ---- 几何拟合专属（其余模式为 null，不绑定即不显示） ----

        /// <summary>拟合类型（"圆"/"直线"/"矩形"/"椭圆"）</summary>
        public string FitType { get; set; }

        /// <summary>尺寸1：圆/椭圆=半径1，矩形=半长，直线=长度（像素）</summary>
        public double? Size1 { get; set; }

        /// <summary>尺寸2：椭圆=半径2，矩形=半宽；圆/直线无意义</summary>
        public double? Size2 { get; set; }

        /// <summary>拟合残差 RMS（像素；椭圆为归一化近似）</summary>
        public double? FitError { get; set; }
    }
}
