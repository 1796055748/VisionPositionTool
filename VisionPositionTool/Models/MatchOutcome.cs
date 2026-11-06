using System.Collections.Generic;

namespace VisionPositionTool.Models
{
    /// <summary>一次匹配的输出：结果列表 + 供显示的叠加图形（图像坐标）</summary>
    public class MatchOutcome
    {
        public List<MatchResult> Results { get; } = new List<MatchResult>();

        /// <summary>每个匹配的轮廓（折线，图像坐标）</summary>
        public List<List<Point2>> Contours { get; } = new List<List<Point2>>();

        /// <summary>匹配中心十字（图像坐标）</summary>
        public List<Point2> Crosses { get; } = new List<Point2>();
    }
}
