using System;
using System.ComponentModel;

namespace VisionPositionTool.Models
{
    /// <summary>
    /// 匹配参数（界面绑定用，角度单位为度，内部自动换算弧度）。
    /// 包含模板匹配 / 灰度匹配的完整参数集合。
    /// </summary>
    public class TemplateParams : INotifyPropertyChanged
    {
        // ---- 通用 ----
        private double _startAngle = -45;        // 起始角度（度）
        private double _angleExtent = 90;        // 角度范围（度）
        private double _minScore = 0.5;          // 最小分数
        private int _numMatches = 1;             // 匹配数量
        private double _maxOverlap = 0.5;        // 最大重叠度
        private string _numLevels = "auto";      // 金字塔层数（create：auto 或整数）
        private string _angleStep = "auto";      // 角度步长（create：auto 或整数）

        // ---- 模板匹配（形状匹配）专属 ----
        private string _shapeSubPixel = "least_squares";  // 亚像素精度
        private string _optimization = "auto";   // 优化方式
        private string _metric = "use_polarity"; // 度量方式
        private string _contrast = "auto";       // 对比度
        private string _minContrast = "auto";    // 最小对比度
        private double _greediness = 0.7;        // 贪心系数

        // ---- 相关性匹配（NCC）专属 ----
        private string _nccSubPixel = "true";    // 亚像素（true/false/interpolation）

        // ---- 灰度模板匹配（create_template / best_match）专属 ----
        private string _grayMetric = "original"; // 灰度值模式（original/normalized）
        private string _graySubPixel = "true";   // 亚像素（true/false）

        // ---- 几何拟合（edges_sub_pix + fit_*_contour_xld）专属 ----
        private string _fitType = "圆";              // 拟合类型（圆/直线/矩形/椭圆）
        private string _fitEdgeFilter = "canny";     // 边缘提取算子（canny/deriche2/lanser2/mshen/sobel_fast）
        private string _fitAlpha = "1.0";            // 平滑系数（canny 越小越平滑，其余算子越大越平滑）
        private string _fitLow = "20";               // 滞后低阈值（只填数字，>0）
        private string _fitHigh = "40";              // 滞后高阈值（只填数字，>= 低阈值）
        private string _fitMinContourLength = "30";  // 最小轮廓长度（短于此的轮廓丢弃）
        private string _fitGap = "0";                // 断边拼接间隙（像素，0=不拼接）
        private string _fitMaxError = "2.0";         // 最大拟合误差（只接受残差≤此值的轮廓，0=不限制）

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public double StartAngle { get => _startAngle; set { _startAngle = value; OnPropertyChanged(nameof(StartAngle)); } }
        public double AngleExtent { get => _angleExtent; set { _angleExtent = value; OnPropertyChanged(nameof(AngleExtent)); } }
        public double MinScore { get => _minScore; set { _minScore = value; OnPropertyChanged(nameof(MinScore)); } }
        public int NumMatches { get => _numMatches; set { _numMatches = value; OnPropertyChanged(nameof(NumMatches)); } }
        public double MaxOverlap { get => _maxOverlap; set { _maxOverlap = value; OnPropertyChanged(nameof(MaxOverlap)); } }
        public string NumLevels { get => _numLevels; set { _numLevels = value; OnPropertyChanged(nameof(NumLevels)); } }
        public string AngleStep { get => _angleStep; set { _angleStep = value; OnPropertyChanged(nameof(AngleStep)); } }

        public string ShapeSubPixel { get => _shapeSubPixel; set { _shapeSubPixel = value; OnPropertyChanged(nameof(ShapeSubPixel)); } }
        public string Optimization { get => _optimization; set { _optimization = value; OnPropertyChanged(nameof(Optimization)); } }
        public string Metric { get => _metric; set { _metric = value; OnPropertyChanged(nameof(Metric)); } }
        public string Contrast { get => _contrast; set { _contrast = value; OnPropertyChanged(nameof(Contrast)); } }
        public string MinContrast { get => _minContrast; set { _minContrast = value; OnPropertyChanged(nameof(MinContrast)); } }
        public double Greediness { get => _greediness; set { _greediness = value; OnPropertyChanged(nameof(Greediness)); } }

        public string NccSubPixel { get => _nccSubPixel; set { _nccSubPixel = value; OnPropertyChanged(nameof(NccSubPixel)); } }

        public string GrayMetric { get => _grayMetric; set { _grayMetric = value; OnPropertyChanged(nameof(GrayMetric)); } }
        public string GraySubPixel { get => _graySubPixel; set { _graySubPixel = value; OnPropertyChanged(nameof(GraySubPixel)); } }

        public string FitType { get => _fitType; set { _fitType = value; OnPropertyChanged(nameof(FitType)); } }
        public string FitEdgeFilter { get => _fitEdgeFilter; set { _fitEdgeFilter = value; OnPropertyChanged(nameof(FitEdgeFilter)); } }
        public string FitAlpha { get => _fitAlpha; set { _fitAlpha = value; OnPropertyChanged(nameof(FitAlpha)); } }
        public string FitLow { get => _fitLow; set { _fitLow = value; OnPropertyChanged(nameof(FitLow)); } }
        public string FitHigh { get => _fitHigh; set { _fitHigh = value; OnPropertyChanged(nameof(FitHigh)); } }
        public string FitMinContourLength { get => _fitMinContourLength; set { _fitMinContourLength = value; OnPropertyChanged(nameof(FitMinContourLength)); } }
        public string FitGap { get => _fitGap; set { _fitGap = value; OnPropertyChanged(nameof(FitGap)); } }
        public string FitMaxError { get => _fitMaxError; set { _fitMaxError = value; OnPropertyChanged(nameof(FitMaxError)); } }

        /// <summary>起始角度（弧度）——HALCON 角度参数单位是弧度</summary>
        public double StartAngleRad => StartAngle * Math.PI / 180.0;

        /// <summary>角度范围（弧度）</summary>
        public double AngleExtentRad => AngleExtent * Math.PI / 180.0;
    }
}
