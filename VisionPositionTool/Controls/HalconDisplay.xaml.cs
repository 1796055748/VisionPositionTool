using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using HalconDotNet;
using VisionPositionTool.Models;

namespace VisionPositionTool.Controls
{
    /// <summary>
    /// HALCON 图像显示控件（自绘，不依赖 HALCON 窗口控件）。
    /// 用 WriteableBitmap 渲染 HImage，支持缩放/平移/框选 ROI/叠加显示匹配结果。
    /// </summary>
    public partial class HalconDisplay : UserControl
    {
        private HImage _image;
        private int _imgW, _imgH;
        private double _scale = 1.0;
        private double _offX = 0, _offY = 0;      // 图像→屏幕：screenX = imgX*scale + offX

        private Point _dragStartScreen;           // 框选起点（屏幕坐标）
        private bool _drawing;
        private Point _panStartScreen;            // 平移起点
        private bool _panning;
        private Point _panStartOff;
        private RoiKind _roiKind = RoiKind.None;

        /// <summary>模板 ROI 框选完成（图像坐标，行=Y，列=X，r1&lt;r2, c1&lt;c2）</summary>
        public event Action<double, double, double, double> RoiDrawn;

        /// <summary>搜索框（搜索区域）框选完成</summary>
        public event Action<double, double, double, double> SearchRoiDrawn;

        /// <summary>模板 ROI 框被拖动/缩放后（图像坐标）</summary>
        public event Action<double, double, double, double> TemplateRoiChanged;

        /// <summary>搜索框被拖动/缩放后（图像坐标）</summary>
        public event Action<double, double, double, double> SearchRoiChanged;

        private enum RoiKind { None, Template, Search }

        // ---- 交互式 ROI 框 ----
        private sealed class RoiBox
        {
            public RoiKind Kind;
            public double R1, C1, R2, C2;   // 图像坐标
            public string Color;
        }

        private readonly List<RoiBox> _boxes = new List<RoiBox>();
        private readonly List<UIElement> _boxShapes = new List<UIElement>();
        private RoiBox _dragBox;          // 正在拖动/缩放的框
        private string _dragOp;           // move / TL / TR / BL / BR / T / B / L / R
        private Point _dragLastImg;

        public HalconDisplay()
        {
            InitializeComponent();
            SizeChanged += (s, e) => FitImage();
        }

        // ---------------- 对外 API ----------------

        public void ShowImage(HImage image)
        {
            _image = image;
            var bmp = ToBitmap(image);
            bool sizeChanged = bmp.PixelWidth != _imgW || bmp.PixelHeight != _imgH;
            _imgW = bmp.PixelWidth;
            _imgH = bmp.PixelHeight;
            Img.Source = bmp;
            Img.Width = _imgW;
            Img.Height = _imgH;
            ClearOverlay();
            ExitRoiDrawMode();
            HintText.Text = "";
            // 仅图像尺寸变化（换图）才重置视图为适应窗口；刷新同一张图时保留用户缩放/平移
            // （否则拖动 ROI 后 RefreshDisplay 会把放大状态重置回初始大小）
            if (sizeChanged) FitImage();
        }

        /// <summary>进入模板 ROI 绘制模式（绿色）</summary>
        public void EnterRoiDrawMode()
        {
            _roiKind = RoiKind.Template;
            DrawingRect.Stroke = new SolidColorBrush(Colors.Lime);
            HintText.Text = "请框选模板区域（拖出矩形，右键取消）";
            Cursor = Cursors.Cross;
        }

        /// <summary>进入搜索框绘制模式（蓝色）</summary>
        public void EnterSearchRoiDrawMode()
        {
            _roiKind = RoiKind.Search;
            DrawingRect.Stroke = new SolidColorBrush(Colors.DodgerBlue);
            HintText.Text = "请框选搜索区域（拖出矩形，右键取消）";
            Cursor = Cursors.Cross;
        }

        public void ExitRoiDrawMode()
        {
            _roiKind = RoiKind.None;
            DrawingRect.Visibility = Visibility.Collapsed;
            Cursor = Cursors.Arrow;
            HintText.Text = "";
        }

        public void ClearOverlay()
        {
            Overlay.Children.Clear();
            _boxShapes.Clear();
        }

        // ---- 交互式 ROI 框 ----

        /// <summary>设置模板 ROI 框（绿色，可拖动/缩放）</summary>
        public void SetTemplateRoi(double r1, double c1, double r2, double c2)
            => SetRoiBox(RoiKind.Template, r1, c1, r2, c2, "Lime");

        /// <summary>设置搜索框（可拖动/缩放），默认蓝色；可传其他颜色（如匹配失败显示红色）</summary>
        public void SetSearchRoi(double r1, double c1, double r2, double c2, string color = "DodgerBlue")
            => SetRoiBox(RoiKind.Search, r1, c1, r2, c2, color);

        private void SetRoiBox(RoiKind kind, double r1, double c1, double r2, double c2, string color)
        {
            var box = _boxes.Find(b => b.Kind == kind);
            if (box == null)
            {
                box = new RoiBox { Kind = kind };
                _boxes.Add(box);
            }
            box.Color = color;      // 始终更新颜色（如匹配失败变红）
            box.R1 = r1; box.C1 = c1; box.R2 = r2; box.C2 = c2;
            RenderRoiBoxes();
        }

        /// <summary>清除所有 ROI 框</summary>
        public void ClearRoiBoxes()
        {
            _boxes.Clear();
            RenderRoiBoxes();
        }

        /// <summary>移除模板 ROI 框</summary>
        public void RemoveTemplateRoi() => RemoveRoiBox(RoiKind.Template);

        /// <summary>移除搜索框</summary>
        public void RemoveSearchRoi() => RemoveRoiBox(RoiKind.Search);

        private void RemoveRoiBox(RoiKind kind)
        {
            if (_boxes.RemoveAll(b => b.Kind == kind) > 0)
                RenderRoiBoxes();
        }

        /// <summary>重新渲染 ROI 框（在 ClearOverlay/DrawOutcome 之后调用）</summary>
        public void RenderRoiBoxes()
        {
            foreach (var s in _boxShapes) Overlay.Children.Remove(s);
            _boxShapes.Clear();
            if (_boxes.Count == 0) return;

            double hs = Math.Max(4, 6.0 / _scale);   // 手柄大小（约 6 屏幕像素）
            foreach (var b in _boxes)
            {
                var stroke = (Color)ColorConverter.ConvertFromString(b.Color);
                double x1 = Math.Min(b.C1, b.C2), y1 = Math.Min(b.R1, b.R2);
                double w = Math.Abs(b.C2 - b.C1), h = Math.Abs(b.R2 - b.R1);

                var rect = new Rectangle
                {
                    Stroke = new SolidColorBrush(stroke),
                    StrokeThickness = 3,
                    Fill = null
                };
                rect.Width = w; rect.Height = h;
                Canvas.SetLeft(rect, x1); Canvas.SetTop(rect, y1);
                Overlay.Children.Add(rect); _boxShapes.Add(rect);

                // 四角手柄
                var corners = new[] { new Point(x1, y1), new Point(x1 + w, y1), new Point(x1, y1 + h), new Point(x1 + w, y1 + h) };
                foreach (var cp in corners)
                {
                    var hd = new Rectangle { Width = hs * 2, Height = hs * 2, Fill = new SolidColorBrush(stroke), Stroke = Brushes.White, StrokeThickness = 1 };
                    Canvas.SetLeft(hd, cp.X - hs); Canvas.SetTop(hd, cp.Y - hs);
                    Overlay.Children.Add(hd); _boxShapes.Add(hd);
                }
            }
        }

        /// <summary>绘制匹配结果叠加层（轮廓 + 十字，图像坐标）</summary>
        public void DrawOutcome(MatchOutcome outcome)
        {
            ClearOverlay();
            var red = new SolidColorBrush(Colors.Red);
            foreach (var contour in outcome.Contours)
                AddPolyline(contour, red, 2);
            foreach (var c in outcome.Crosses)
                AddCross(c.Row, c.Col, 30, red);
        }

        /// <summary>绘制模板轮廓预览（金色，图像坐标）。由 RefreshDisplay 在 ClearOverlay 之后调用，无需自清理</summary>
        public void DrawModelContour(List<Point2> pts)
        {
            if (pts.Count < 2) return;
            AddPolyline(pts, new SolidColorBrush(Colors.Gold), 2);
        }

        // ---------------- 绘制辅助 ----------------

        private void AddPolyline(List<Point2> pts, Brush brush, double thickness)
        {
            if (pts.Count < 2) return;
            var poly = new Polyline { Stroke = brush, StrokeThickness = thickness, Fill = null };
            var pc = new PointCollection();
            foreach (var p in pts) pc.Add(new Point(p.Col, p.Row));
            poly.Points = pc;
            Overlay.Children.Add(poly);
        }

        private void AddCross(double row, double col, double size, Brush brush)
        {
            var h = new Line { X1 = col - size, X2 = col + size, Y1 = row, Y2 = row, Stroke = brush, StrokeThickness = 2 };
            var v = new Line { X1 = col, X2 = col, Y1 = row - size, Y2 = row + size, Stroke = brush, StrokeThickness = 2 };
            Overlay.Children.Add(h);
            Overlay.Children.Add(v);
        }

        // ---------------- HImage → 位图 ----------------

        private static WriteableBitmap ToBitmap(HImage image)
        {
            int channels = (int)image.CountChannels().D;

            if (channels == 1)
            {
                IntPtr ptr = image.GetImagePointer1(out string type, out int w, out int h);
                byte[] px = new byte[w * h];
                if (type != "byte")
                {
                    // 非 byte 单通道：转 byte 后取指针
                    HImage conv = image.ConvertImageType("byte");
                    ptr = conv.GetImagePointer1(out type, out w, out h);
                    Marshal.Copy(ptr, px, 0, w * h);
                    conv.Dispose();
                }
                else
                {
                    Marshal.Copy(ptr, px, 0, w * h);
                }
                var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Gray8, null);
                bmp.WritePixels(new Int32Rect(0, 0, w, h), px, w, 0);
                return bmp;
            }

            // 多通道：整理为 3 通道 RGB（byte），杜绝 GetImagePointer3 遇非 3 通道崩溃
            HImage rgb = channels == 3 ? image : null;
            if (rgb == null)
            {
                HOperatorSet.AccessChannel(image, out HObject c1, 1);
                HOperatorSet.AccessChannel(image, out HObject c2, 2);
                HOperatorSet.AccessChannel(image, out HObject c3, 3);
                HOperatorSet.Compose3(c1, c2, c3, out HObject rgbObj);
                c1.Dispose(); c2.Dispose(); c3.Dispose();
                rgb = (HImage)rgbObj;
            }

            HImage byteRgb = rgb;
            bool disposeByte = false;
            if (rgb.GetImageType().S != "byte")
            {
                byteRgb = rgb.ConvertImageType("byte");
                disposeByte = true;
            }
            try
            {
                byteRgb.GetImagePointer3(out IntPtr rp, out IntPtr gp, out IntPtr bp, out string t3, out int w3, out int h3);
                byte[] r = new byte[w3 * h3], g = new byte[w3 * h3], b = new byte[w3 * h3];
                Marshal.Copy(rp, r, 0, w3 * h3);
                Marshal.Copy(gp, g, 0, w3 * h3);
                Marshal.Copy(bp, b, 0, w3 * h3);
                byte[] rgbData = new byte[w3 * h3 * 3];
                for (int i = 0; i < w3 * h3; i++) { rgbData[i * 3] = r[i]; rgbData[i * 3 + 1] = g[i]; rgbData[i * 3 + 2] = b[i]; }
                var bmp3 = new WriteableBitmap(w3, h3, 96, 96, PixelFormats.Rgb24, null);
                bmp3.WritePixels(new Int32Rect(0, 0, w3, h3), rgbData, w3 * 3, 0);
                return bmp3;
            }
            finally
            {
                if (disposeByte && byteRgb != null) byteRgb.Dispose();
                if (rgb != image) rgb.Dispose();
            }
        }

        // ---------------- 坐标变换 ----------------

        private void ApplyTransform()
        {
            var tg = new TransformGroup();
            tg.Children.Add(new ScaleTransform(_scale, _scale));
            tg.Children.Add(new TranslateTransform(_offX, _offY));
            ImgHost.RenderTransform = tg;
        }

        private void FitImage()
        {
            double vw = Viewport.ActualWidth, vh = Viewport.ActualHeight;
            if (vw < 1 || vh < 1 || _imgW < 1) return;
            _scale = Math.Min(vw / _imgW, vh / _imgH);
            _offX = (vw - _imgW * _scale) / 2;
            _offY = (vh - _imgH * _scale) / 2;
            ApplyTransform();
        }

        private Point ScreenToImage(Point s) => new Point((s.X - _offX) / _scale, (s.Y - _offY) / _scale);

        // ---------------- 鼠标交互 ----------------

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_imgW < 1) return;
            double factor = e.Delta > 0 ? 1.2 : 1 / 1.2;
            var imgPt = ScreenToImage(e.GetPosition(Viewport));
            _scale = Math.Max(0.05, Math.Min(50.0, _scale * factor));
            _offX = e.GetPosition(Viewport).X - imgPt.X * _scale;
            _offY = e.GetPosition(Viewport).Y - imgPt.Y * _scale;
            ApplyTransform();
        }

        private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_imgW < 1) return;
            if (_roiKind != RoiKind.None)
            {
                _dragStartScreen = e.GetPosition(Viewport);
                _drawing = true;
                DrawingRect.Visibility = Visibility.Visible;
            }
            else
            {
                // 命中 ROI 框 → 拖动/缩放；否则平移
                var imgPt = ScreenToImage(e.GetPosition(Viewport));
                var hit = HitTestBoxes(imgPt);
                if (hit.Item1 != null)
                {
                    _dragBox = hit.Item1;
                    _dragOp = hit.Item2;
                    _dragLastImg = imgPt;
                    Cursor = _dragOp == "move" ? Cursors.SizeAll : Cursors.Cross;
                }
                else
                {
                    _panStartScreen = e.GetPosition(Viewport);
                    _panStartOff = new Point(_offX, _offY);
                    _panning = true;
                    Cursor = Cursors.SizeAll;
                }
            }
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (_drawing)
            {
                var cur = e.GetPosition(Viewport);
                DrawingRect.Width = Math.Abs(cur.X - _dragStartScreen.X);
                DrawingRect.Height = Math.Abs(cur.Y - _dragStartScreen.Y);
                Canvas.SetLeft(DrawingRect, Math.Min(_dragStartScreen.X, cur.X));
                Canvas.SetTop(DrawingRect, Math.Min(_dragStartScreen.Y, cur.Y));
            }
            else if (_dragBox != null)
            {
                var img = ScreenToImage(e.GetPosition(Viewport));
                double dx = img.X - _dragLastImg.X, dy = img.Y - _dragLastImg.Y;
                ApplyBoxDrag(dx, dy);
                _dragLastImg = img;
                RenderRoiBoxes();
            }
            else if (_panning)
            {
                var cur = e.GetPosition(Viewport);
                _offX = _panStartOff.X + (cur.X - _panStartScreen.X);
                _offY = _panStartOff.Y + (cur.Y - _panStartScreen.Y);
                ApplyTransform();
            }
            else if (_roiKind == RoiKind.None)
            {
                // 悬停反馈
                var hit = HitTestBoxes(ScreenToImage(e.GetPosition(Viewport)));
                Cursor = hit.Item1 != null ? Cursors.SizeAll : Cursors.Arrow;
            }
        }

        private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_drawing)
            {
                _drawing = false;
                DrawingRect.Visibility = Visibility.Collapsed;
                var start = ScreenToImage(_dragStartScreen);
                var end = ScreenToImage(e.GetPosition(Viewport));
                double r1 = Math.Min(start.Y, end.Y), r2 = Math.Max(start.Y, end.Y);
                double c1 = Math.Min(start.X, end.X), c2 = Math.Max(start.X, end.X);
                if (r2 - r1 >= 1 && c2 - c1 >= 1)
                {
                    RoiKind kind = _roiKind;
                    ExitRoiDrawMode();
                    if (kind == RoiKind.Template)
                        RoiDrawn?.Invoke(r1, c1, r2, c2);
                    else if (kind == RoiKind.Search)
                        SearchRoiDrawn?.Invoke(r1, c1, r2, c2);
                }
            }
            else if (_dragBox != null)
            {
                var b = _dragBox;
                _dragBox = null;
                _dragOp = null;
                NormalizeBox(b);
                RenderRoiBoxes();
                if (b.Kind == RoiKind.Template)
                    TemplateRoiChanged?.Invoke(b.R1, b.C1, b.R2, b.C2);
                else if (b.Kind == RoiKind.Search)
                    SearchRoiChanged?.Invoke(b.R1, b.C1, b.R2, b.C2);
            }
            else if (_panning)
            {
                _panning = false;
                Cursor = Cursors.Arrow;
            }
        }

        private void Viewport_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 绘制模式下右键取消
            if (_roiKind != RoiKind.None) ExitRoiDrawMode();
        }

        private void Viewport_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_drawing) { _drawing = false; DrawingRect.Visibility = Visibility.Collapsed; }
            if (_dragBox != null) { _dragBox = null; _dragOp = null; }
            if (_panning) { _panning = false; Cursor = Cursors.Arrow; }
        }

        // ---- ROI 框拖动/缩放 ----

        private (RoiBox, string) HitTestBoxes(Point img)
        {
            double tol = Math.Max(3, 8.0 / _scale);
            for (int i = _boxes.Count - 1; i >= 0; i--)
            {
                var b = _boxes[i];
                double x1 = Math.Min(b.C1, b.C2), y1 = Math.Min(b.R1, b.R2);
                double x2 = Math.Max(b.C1, b.C2), y2 = Math.Max(b.R1, b.R2);
                if (Dist(img.X, img.Y, x1, y1) < tol) return (b, "TL");
                if (Dist(img.X, img.Y, x2, y1) < tol) return (b, "TR");
                if (Dist(img.X, img.Y, x1, y2) < tol) return (b, "BL");
                if (Dist(img.X, img.Y, x2, y2) < tol) return (b, "BR");
                if (Math.Abs(img.Y - y1) < tol && img.X >= x1 - tol && img.X <= x2 + tol) return (b, "T");
                if (Math.Abs(img.Y - y2) < tol && img.X >= x1 - tol && img.X <= x2 + tol) return (b, "B");
                if (Math.Abs(img.X - x1) < tol && img.Y >= y1 - tol && img.Y <= y2 + tol) return (b, "L");
                if (Math.Abs(img.X - x2) < tol && img.Y >= y1 - tol && img.Y <= y2 + tol) return (b, "R");
                if (img.X > x1 && img.X < x2 && img.Y > y1 && img.Y < y2) return (b, "move");
            }
            return (null, null);
        }

        private static double Dist(double x, double y, double x2, double y2)
            => Math.Sqrt((x - x2) * (x - x2) + (y - y2) * (y - y2));

        private void ApplyBoxDrag(double dx, double dy)
        {
            var b = _dragBox;
            switch (_dragOp)
            {
                case "move": b.C1 += dx; b.C2 += dx; b.R1 += dy; b.R2 += dy; break;
                case "TL": b.C1 += dx; b.R1 += dy; break;
                case "TR": b.C2 += dx; b.R1 += dy; break;
                case "BL": b.C1 += dx; b.R2 += dy; break;
                case "BR": b.C2 += dx; b.R2 += dy; break;
                case "T": b.R1 += dy; break;
                case "B": b.R2 += dy; break;
                case "L": b.C1 += dx; break;
                case "R": b.C2 += dx; break;
            }
        }

        private static void NormalizeBox(RoiBox b)
        {
            if (b.R1 > b.R2) { double t = b.R1; b.R1 = b.R2; b.R2 = t; }
            if (b.C1 > b.C2) { double t = b.C1; b.C1 = b.C2; b.C2 = t; }
        }
    }
}
