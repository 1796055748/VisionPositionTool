namespace VisionPositionTool.Models
{
    /// <summary>图像坐标点（行=Y，列=X）</summary>
    public struct Point2
    {
        public double Row;
        public double Col;

        public Point2(double row, double col) { Row = row; Col = col; }
    }
}
