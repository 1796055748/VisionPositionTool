using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HalconDotNet;
using VisionPositionTool.Core;
using VisionPositionTool.Models;

namespace VisionPositionTool
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm = new MainViewModel();
        private readonly ShapeMatchTool _shapeTool = new ShapeMatchTool();
        private readonly NccMatchTool _nccTool = new NccMatchTool();
        private readonly GrayMatchTool _grayTool = new GrayMatchTool();
        private readonly GeometricFitTool _fitTool = new GeometricFitTool();

        private HObject _searchRegion;                    // 搜索框区域（可空=全图搜索）
        private (double R1, double C1, double R2, double C2)? _searchRect;
        private ImageItem _templateSourceImage;            // 模板来源图（绿框只在此图显示）

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _vm;
            Display.RoiDrawn += Display_RoiDrawn;
            Display.SearchRoiDrawn += Display_SearchRoiDrawn;
            Display.TemplateRoiChanged += Display_TemplateRoiChanged;
            Display.SearchRoiChanged += Display_SearchRoiChanged;
            _vm.PropertyChanged += _vm_PropertyChanged;
            _vm.Params.PropertyChanged += Params_PropertyChanged;
            Closed += MainWindow_Closed;
            Loaded += MainWindow_Loaded;
        }

        /// <summary>当前选择的匹配工具</summary>
        private IMatchTool CurrentTool =>
            _vm.MatchMode == "ncc" ? (IMatchTool)_nccTool :
            _vm.MatchMode == "gray" ? (IMatchTool)_grayTool :
            _vm.MatchMode == "fit" ? (IMatchTool)_fitTool :
            (IMatchTool)_shapeTool;

        /// <summary>匹配方式切换：自动清除模板模型与结果（搜索框保留，两种方式通用）</summary>
        private void _vm_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(MainViewModel.MatchMode)) return;
            _shapeTool.ClearModel();
            _nccTool.ClearModel();
            _grayTool.ClearModel();
            _fitTool.ClearModel();
            _templateSourceImage = null;
            foreach (var item in _vm.Images)
            {
                item.Results.Clear();
                item.Outcome = null;
                item.MatchFailed = false;
                item.ResultSummary = "未匹配";
            }
            // 相关性匹配（NCC）的 Metric 只接受 use_polarity/ignore_global_polarity，
            // 模板匹配的其它选项（ignore_color_polarity 等）切过去会报 #1305，重置为默认
            if (_vm.MatchMode == "ncc")
            {
                string m = _vm.Params.Metric;
                if (m != "use_polarity" && m != "ignore_global_polarity")
                    _vm.Params.Metric = "use_polarity";
            }
            _vm.Status = _vm.MatchMode == "fit"
                ? "已切换到" + CurrentTool.DisplayName + "，拟合区域已清除，请重新框选拟合区域（绿色框）"
                : "已切换到" + CurrentTool.DisplayName + "，模板已清除，请重新框选模板";
            RefreshDisplay();
        }

        // ================= 命令行调试模式 =================

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 6; i++)
            {
                if (args[i] != "--demo") continue;
                RunDemo(args[i + 1], args[i + 2], args[i + 3], args[i + 4], args[i + 5], args[i + 6]);
                return;
            }
            for (int i = 0; i < args.Length - 2; i++)
            {
                if (args[i] != "--open") continue;
                string log = args[i + 2];
                bool ok = TryOpenImage(args[i + 1]);
                var t = new System.Threading.Timer(_ => Dispatcher.Invoke(() =>
                {
                    File.WriteAllText(log, ok ? "OPEN OK" : "OPEN FAILED");
                    Close();
                }), null, 3000, -1);
                return;
            }
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "--selftest") continue;
                RunSelfTest(args[i + 1]);
                Close();
                return;
            }
        }

        private void RunDemo(string imagePath, string r1s, string c1s, string r2s, string c2s, string logPath)
        {
            var sb = new StringBuilder();
            try
            {
                var inv = CultureInfo.InvariantCulture;
                HImage img = ReadImageRobust(imagePath);
                Display.ShowImage(img);
                HOperatorSet.GenRectangle1(out HObject roi, double.Parse(r1s, inv), double.Parse(c1s, inv),
                                                          double.Parse(r2s, inv), double.Parse(c2s, inv));

                var sp = new ShapeMatchTool();
                bool ok1 = sp.CreateModelFromRegion(img, roi, _vm.Params, out string m1);
                var o1 = sp.RunMatch(img, _vm.Params, null);
                sb.AppendLine($"[SHAPE] create={m1} matches={o1.Results.Count}");
                foreach (var x in o1.Results)
                    sb.AppendLine($"  [{x.Index}] row={x.Row}, col={x.Col}, angle={x.Angle}, score={x.Score}");

                // 搜索区域（搜索框）内匹配验证
                HOperatorSet.GenRectangle1(out HObject sRoi, 80, 120, 360, 440);
                var o1s = sp.RunMatch(img, _vm.Params, sRoi);
                sb.AppendLine($"[SHAPE+SEARCH] matches={o1s.Results.Count}" +
                    (o1s.Results.Count > 0 ? $", row={o1s.Results[0].Row}, col={o1s.Results[0].Col}, score={o1s.Results[0].Score}" : " (none)"));
                sRoi.Dispose();

                var nc = new NccMatchTool();
                bool ok2 = nc.CreateModelFromRegion(img, roi, _vm.Params, out string m2);
                var o2 = nc.RunMatch(img, _vm.Params, null);
                sb.AppendLine($"[NCC] create={m2} matches={o2.Results.Count}");
                foreach (var x in o2.Results)
                    sb.AppendLine($"  [{x.Index}] row={x.Row}, col={x.Col}, angle={x.Angle}, score={x.Score}");

                // [FIT] 几何拟合自检：合成圆图（中心 200,200 半径 100），拟合 ROI 必须完全包住圆（边距 20）
                HOperatorSet.GenImageConst(out HObject simObj, "byte", 400, 400);
                HOperatorSet.GenCircle(out HObject simReg, 200, 200, 100);
                HOperatorSet.PaintRegion(simReg, simObj, out HObject simPainted, 255, "fill");
                simObj.Dispose(); simReg.Dispose();
                var simImg = new HImage(simPainted);
                HOperatorSet.GenRectangle1(out HObject fitRoi, 80, 80, 320, 320);
                var ft = new GeometricFitTool();
                var fp = new TemplateParams { FitType = "圆" };
                bool fok = ft.CreateModelFromRegion(simImg, fitRoi, fp, out string fm);
                var fo = ft.RunMatch(simImg, fp, null);
                sb.AppendLine($"[FIT] create={fm} matches={fo.Results.Count}");
                foreach (var x in fo.Results)
                    sb.AppendLine($"  type={x.FitType} row={x.Row}, col={x.Col}, angle={x.Angle}, size1={x.Size1}, err={x.FitError}");
                bool fitPass = fok && fo.Results.Count > 0
                    && Math.Abs(fo.Results[0].Row - 200) <= 2 && Math.Abs(fo.Results[0].Col - 200) <= 2
                    && fo.Results[0].Size1.HasValue && Math.Abs(fo.Results[0].Size1.Value - 100) <= 2;
                ft.ClearModel(); simImg.Dispose(); fitRoi.Dispose();

                string tmpDir = Path.Combine(Path.GetTempPath(), "vpt_demo_" + System.Diagnostics.Process.GetCurrentProcess().Id);
                Directory.CreateDirectory(tmpDir);
                sp.SaveModel(tmpDir, out _);
                nc.SaveModel(tmpDir, out _);
                var sp2 = new ShapeMatchTool();
                var nc2 = new NccMatchTool();
                bool l1 = sp2.LoadModel(tmpDir, out string lm1);
                var o1b = sp2.RunMatch(img, _vm.Params, null);
                bool l2 = nc2.LoadModel(tmpDir, out string lm2);
                var o2b = nc2.RunMatch(img, _vm.Params, null);
                sb.AppendLine($"[LOAD] shape={l1}({lm1}) matches={o1b.Results.Count}; ncc={l2}({lm2}) matches={o2b.Results.Count}");
                Directory.Delete(tmpDir, true);

                sp.ClearModel(); nc.ClearModel(); sp2.ClearModel(); nc2.ClearModel();
                sb.AppendLine($"RESULT: {(ok1 && ok2 && o1.Results.Count > 0 && o2.Results.Count > 0 && l1 && l2 && fitPass ? "PASS" : "FAIL")}");
            }
            catch (Exception ex)
            {
                sb.AppendLine("DEMO FAILED: " + ex);
            }
            File.WriteAllText(logPath, sb.ToString());
            Close();
        }

        // ================= 图像导入 =================

        private void BtnImportImages_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "图像文件|*.png;*.bmp;*.jpg;*.jpeg;*.tif;*.tiff|所有文件|*.*",
                Title = "选择图像（可多选）",
                Multiselect = true
            };
            if (dlg.ShowDialog() != true) return;

            foreach (var path in dlg.FileNames)
            {
                try
                {
                    var img = ReadImageRobust(path);
                    _vm.Images.Add(new ImageItem
                    {
                        Name = Path.GetFileName(path),
                        FilePath = path,
                        Image = img
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导入 {Path.GetFileName(path)} 失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            _vm.Status = $"已导入 {_vm.Images.Count} 张图像";
            if (_vm.SelectedImage == null && _vm.Images.Count > 0)
                _vm.SelectedImage = _vm.Images[0];
        }

        private void ImageList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            RefreshDisplay();
        }

        // ================= 删除图像 =================

        private void BtnDeleteImages_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelectedImages();
        }

        /// <summary>Delete 键删除选中的图像</summary>
        private void ImageList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Delete) return;
            DeleteSelectedImages();
            e.Handled = true;
        }

        /// <summary>批量删除选中的图像（仅从列表移除并释放内存，不删除磁盘文件）</summary>
        private void DeleteSelectedImages()
        {
            List<ImageItem> selected = ImageList.SelectedItems.Cast<ImageItem>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("请先选择要删除的图像（Ctrl/Shift 可多选）", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show($"确定删除选中的 {selected.Count} 张图像吗？\n（仅从列表移除并释放内存，不会删除磁盘上的文件）",
                                          "删除图像", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK) return;

            foreach (var item in selected)
            {
                // 模板来源图被删除后，模板仍保留但视为无来源（绿框显示在所有图上）
                if (ReferenceEquals(item, _templateSourceImage)) _templateSourceImage = null;
                item.Image?.Dispose();
                _vm.Images.Remove(item);
            }

            // 修正选中项：删除后若没有选中图，自动选中列表第一张（或清空显示）
            if (_vm.Images.Count == 0)
                _vm.SelectedImage = null;
            else if (_vm.SelectedImage == null)
                _vm.SelectedImage = _vm.Images[0];

            _vm.Status = $"已删除 {selected.Count} 张图像，剩余 {_vm.Images.Count} 张";
            RefreshDisplay();
        }

        /// <summary>搜索框颜色：该图匹配失败 → 红色，否则蓝色</summary>
        private string SearchRoiColor(ImageItem item)
            => item != null && item.MatchFailed ? "Red" : "DodgerBlue";

        /// <summary>按当前选中图像刷新显示：图像 + ROI 框 + 结果叠加</summary>
        private void RefreshDisplay()
        {
            var item = _vm.SelectedImage;
            if (item == null) { Display.ClearOverlay(); return; }
            Display.ShowImage(item.Image);
            // 无条件清叠加层：匹配失败（Outcome=null）时也要清掉上一次的轮廓，避免两次结果重叠残留
            Display.ClearOverlay();
            if (item.Outcome != null && item.Results.Count > 0)
                Display.DrawOutcome(item.Outcome);
            // 渲染 ROI 框：搜索蓝框全局显示；模板绿框只在模板来源图显示（加载的模板无来源则都显示）
            var tool = CurrentTool;
            bool showTemplateBox = _templateSourceImage == null || ReferenceEquals(item, _templateSourceImage);
            bool hasRoi = tool.TryGetModelRoi(out double r1, out double c1, out double r2, out double c2);
            if (showTemplateBox && hasRoi)
                Display.SetTemplateRoi(r1, c1, r2, c2);
            else
                Display.RemoveTemplateRoi();

            // 模板轮廓预览（金色=模型边缘，绿色框=ROI）：仅模板匹配显示（灰度匹配的"轮廓"只是 ROI 矩形，与绿框重合，无意义）
            // 每段轮廓平移到模型参考点（ROI 中心），独立绘制折线
            if (_vm.MatchMode == "shape" && showTemplateBox && hasRoi && tool.TryGetBaseContour(out var brParts, out var bcParts))
            {
                double refR = (r1 + r2) / 2, refC = (c1 + c2) / 2;
                for (int part = 0; part < brParts.Count; part++)
                {
                    var pts = new List<Point2>(brParts[part].Length);
                    for (int k = 0; k < brParts[part].Length; k++) pts.Add(new Point2(brParts[part][k] + refR, bcParts[part][k] + refC));
                    Display.DrawModelContour(pts);
                }
            }

            // 几何拟合不使用搜索框：蓝框隐藏（区域保留，切回其它模式恢复）
            if (_searchRect != null && _vm.MatchMode != "fit")
                Display.SetSearchRoi(_searchRect.Value.R1, _searchRect.Value.C1, _searchRect.Value.R2, _searchRect.Value.C2, SearchRoiColor(item));
            else
                Display.RemoveSearchRoi();
        }

        // ================= 参数联动重建模板 =================

        /// <summary>匹配参数变化：仅影响模型创建的参数触发重建（匹配参数不改模型）；拟合参数触发重拟合</summary>
        private void Params_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(TemplateParams.NumLevels):
                case nameof(TemplateParams.StartAngle):
                case nameof(TemplateParams.AngleExtent):
                case nameof(TemplateParams.AngleStep):
                case nameof(TemplateParams.Optimization):
                case nameof(TemplateParams.Metric):
                case nameof(TemplateParams.Contrast):
                case nameof(TemplateParams.MinContrast):
                    RebuildTemplatePreview();
                    break;
                case nameof(TemplateParams.FitType):
                case nameof(TemplateParams.FitEdgeFilter):
                case nameof(TemplateParams.FitAlpha):
                case nameof(TemplateParams.FitLow):
                case nameof(TemplateParams.FitHigh):
                case nameof(TemplateParams.FitMinContourLength):
                case nameof(TemplateParams.FitGap):
                case nameof(TemplateParams.FitMaxError):
                    if (_vm.MatchMode == "fit")
                    {
                        // 拟合类型同步到工具（保存时写入 meta）；其余参数 RunMatch 时直接读 Params
                        if (e.PropertyName == nameof(TemplateParams.FitType))
                            _fitTool.FitType = _vm.Params.FitType;
                        RebuildFitPreview();
                    }
                    break;
            }
        }

        /// <summary>几何拟合：按当前参数对来源图立即重拟合（实时反馈，同模板模式重建惯例）</summary>
        private void RebuildFitPreview()
        {
            var src = _templateSourceImage;
            if (!_fitTool.HasModel || src == null) return;   // 加载的拟合区域无来源图，执行全部匹配时统一拟合
            try
            {
                var outcome = _fitTool.RunMatch(src.Image, _vm.Params, null);
                ApplyOutcomeToItem(src, outcome);
                _vm.Status = outcome.Results.Count > 0
                    ? $"拟合结果：{outcome.Results[0].FitType}，行 {outcome.Results[0].Row}，列 {outcome.Results[0].Col}"
                    : "未找到足够边缘，请调整拟合区域或参数";
            }
            catch
            {
                // 异常时清掉旧结果：否则上一种拟合类型的图形残留，看起来像"切换类型没有变化"
                ApplyOutcomeToItem(src, new MatchOutcome());
                _vm.Status = "拟合参数无效，请检查边缘算子/阈值等参数";
            }
            RefreshDisplay();
        }

        /// <summary>把一次匹配/拟合输出写回图像项（结果表与叠加），执行全部匹配与即时拟合共用</summary>
        private void ApplyOutcomeToItem(ImageItem item, MatchOutcome outcome)
        {
            item.Results.Clear();
            foreach (var r in outcome.Results) item.Results.Add(r);
            item.Outcome = outcome;
            if (outcome.Results.Count > 0)
            {
                item.MatchFailed = false;
                item.ResultSummary = $"找到 {outcome.Results.Count} 个";
            }
            else
            {
                item.MatchFailed = true;
                item.ResultSummary = "匹配失败";
            }
        }

        /// <summary>按当前参数重建模板（需模板来源图 + 模型 ROI），并刷新轮廓预览</summary>
        private void RebuildTemplatePreview()
        {
            var tool = CurrentTool;
            var src = _templateSourceImage;
            if (!tool.HasModel || src == null) return;   // 加载的模板无来源图，无法重建（轮廓仍显示）
            if (!tool.TryGetModelRoi(out double r1, out double c1, out double r2, out double c2)) return;

            HOperatorSet.GenRectangle1(out HObject roi, r1, c1, r2, c2);
            try
            {
                if (tool.CreateModelFromRegion(src.Image, roi, _vm.Params, out string msg))
                    _vm.Status = "已按新参数重建模板";
                else
                    _vm.Status = msg + "（模板未变）";
            }
            catch
            {
                _vm.Status = "参数无效，模板未变";
            }
            finally { roi.Dispose(); }
            RefreshDisplay();
        }

        // ================= 模板 ROI =================

        private void BtnTemplateRoi_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedImage == null) { MessageBox.Show("请先导入并选择一张图像"); return; }
            Display.EnterRoiDrawMode();
            _vm.Status = _vm.MatchMode == "fit"
                ? "请在图像上框选拟合区域（绿色框，拖出矩形，右键取消）"
                : "请在图像上框选模板区域（绿色框，拖出矩形，右键取消）";
        }

        private void Display_RoiDrawn(double r1, double c1, double r2, double c2)
        {
            if (_vm.SelectedImage == null) return;
            try
            {
                HOperatorSet.GenRectangle1(out HObject roi, r1, c1, r2, c2);
                var tool = CurrentTool;
                if (tool.CreateModelFromRegion(_vm.SelectedImage.Image, roi, _vm.Params, out string msg))
                {
                    _templateSourceImage = _vm.SelectedImage;
                    if (_vm.MatchMode == "fit")
                        RebuildFitPreview();   // 几何拟合：框完立即对当前图拟合
                    else
                    {
                        _vm.Status = msg;
                        RefreshDisplay();
                    }
                }
                else
                {
                    MessageBox.Show(msg, "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("创建模板失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ================= 搜索框 =================

        private void BtnSearchRoi_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.MatchMode == "fit")
            {
                _vm.Status = "几何拟合在绿色拟合区域内提边拟合，不使用搜索框";
                return;
            }
            if (_vm.SelectedImage == null) { MessageBox.Show("请先导入并选择一张图像"); return; }
            Display.EnterSearchRoiDrawMode();
            _vm.Status = "请在图像上框选搜索区域（蓝色框，拖出矩形，右键取消）";
        }

        private void Display_SearchRoiDrawn(double r1, double c1, double r2, double c2)
        {
            SetSearchRegion(r1, c1, r2, c2, "搜索区域已设置（蓝色框，可拖动）");
        }

        /// <summary>搜索框被拖动/缩放后：更新搜索区域</summary>
        private void Display_SearchRoiChanged(double r1, double c1, double r2, double c2)
        {
            SetSearchRegion(r1, c1, r2, c2, "搜索区域已调整");
        }

        private void SetSearchRegion(double r1, double c1, double r2, double c2, string status)
        {
            try
            {
                HOperatorSet.GenRectangle1(out HObject roi, r1, c1, r2, c2);
                _searchRegion?.Dispose();
                _searchRegion = roi;
                _searchRect = (r1, c1, r2, c2);
                _vm.Status = status;
                RefreshDisplay();
            }
            catch (Exception ex)
            {
                MessageBox.Show("设置搜索区域失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>模板框被拖动/缩放后：按新位置重建模板</summary>
        private void Display_TemplateRoiChanged(double r1, double c1, double r2, double c2)
        {
            if (_vm.SelectedImage == null) return;
            try
            {
                HOperatorSet.GenRectangle1(out HObject roi, r1, c1, r2, c2);
                var tool = CurrentTool;
                if (tool.CreateModelFromRegion(_vm.SelectedImage.Image, roi, _vm.Params, out string msg))
                {
                    _templateSourceImage = _vm.SelectedImage;
                    if (_vm.MatchMode == "fit")
                        RebuildFitPreview();   // 几何拟合：拖动/缩放后立即重拟合
                    else
                    {
                        _vm.Status = "模板已调整：" + msg;
                        RefreshDisplay();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("调整模板失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ================= 清除 ROI =================

        private void BtnClearRoi_Click(object sender, RoutedEventArgs e)
        {
            _searchRegion?.Dispose();
            _searchRegion = null;
            _searchRect = null;
            _templateSourceImage = null;
            CurrentTool.ClearModel();
            Display.ClearRoiBoxes();
            foreach (var item in _vm.Images)
            {
                item.Results.Clear();
                item.Outcome = null;
                item.MatchFailed = false;
                item.ResultSummary = "未匹配";
            }
            _vm.Status = "已清除模板/搜索区域与匹配结果";
            RefreshDisplay();
        }

        // ================= 执行全部匹配 =================

        private async void BtnRunAllMatch_Click(object sender, RoutedEventArgs e)
        {
            var tool = CurrentTool;
            if (!tool.HasModel)
            {
                MessageBox.Show(_vm.MatchMode == "fit" ? "请先框选拟合区域（绿色框）" : "请先框选模板区域创建模板");
                return;
            }
            if (_vm.Images.Count == 0) { MessageBox.Show("请先导入图像"); return; }

            // 后台线程执行匹配（灰度模板的 best_match 在大图上很慢，主线程同步跑会卡死 UI），
            // 结果写回在 Dispatcher（UI 线程）完成
            _vm.IsBusy = true;
            _vm.Status = "正在匹配所有图像...";
            int total = _vm.Images.Count;
            try
            {
                int failCount = await Task.Run(() =>
                {
                    int fails = 0;
                    for (int i = 0; i < _vm.Images.Count; i++)
                    {
                        var item = _vm.Images[i];
                        try
                        {
                            var outcome = tool.RunMatch(item.Image, _vm.Params, _searchRegion);
                            Dispatcher.Invoke(() =>
                            {
                                ApplyOutcomeToItem(item, outcome);
                                _vm.Status = $"正在匹配 {i + 1}/{total}...";
                            });
                            if (outcome.Results.Count == 0) fails++;
                        }
                        catch (Exception)
                        {
                            // 单图匹配出错：标记失败，不打断其余图像，不弹错误框
                            Dispatcher.Invoke(() =>
                            {
                                item.Results.Clear();
                                item.Outcome = null;
                                item.MatchFailed = true;
                                item.ResultSummary = "匹配失败";
                            });
                            fails++;
                        }
                    }
                    return fails;
                });
                RefreshDisplay();
                _vm.Status = $"匹配完成：{total} 张，失败 {failCount} 张 (当前: {tool.DisplayName})";
            }
            finally
            {
                _vm.IsBusy = false;
            }
        }

        // ================= 保存 / 加载模板 =================

        private void BtnSaveModel_Click(object sender, RoutedEventArgs e)
        {
            var tool = CurrentTool;
            if (!tool.HasModel) { MessageBox.Show("请先创建模板"); return; }

            using (var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "选择模板保存文件夹" })
            {
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                if (tool.SaveModel(dlg.SelectedPath, out string msg))
                    _vm.Status = msg;
                else
                    MessageBox.Show(msg, "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnLoadModel_Click(object sender, RoutedEventArgs e)
        {
            var tool = CurrentTool;
            string folder;
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "选择模板文件夹" })
            {
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                folder = dlg.SelectedPath;
            }

            if (tool.LoadModel(folder, out string msg))
            {
                _templateSourceImage = null;   // 加载的模板无来源图，绿框在所有图上显示
                if (_vm.MatchMode == "fit")
                    _vm.Params.FitType = _fitTool.FitType;   // 回填保存的拟合类型（触发参数面板刷新）
                _vm.Status = msg;
                RefreshDisplay();
            }
            else
                MessageBox.Show(msg, "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // ================= 自检（--selftest 命令行模式） =================

        private static void RunSelfTest(string logPath)
        {
            void W(string m)
            {
                try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {m}\r\n"); } catch { }
            }
            try
            {
                W("selftest begin");
                W("t1 SetSystem...");
                HOperatorSet.SetSystem("thread_num", 4);
                W("t1 OK");
                W("t2 GenImageConst...");
                HOperatorSet.GenImageConst(out HObject img, "byte", 128, 128);
                W("t2 OK");
                W("t3 GenRectangle1+RegionToBin+ReduceDomain...");
                HOperatorSet.GenRectangle1(out HObject rect, 16, 16, 96, 96);
                HOperatorSet.RegionToBin(rect, out HObject binImg, (HTuple)255, (HTuple)0, (HTuple)128, (HTuple)128);
                HOperatorSet.ReduceDomain(binImg, rect, out HObject reduced);
                W("t3 OK");
                W("t4 CreateShapeModel...");
                HOperatorSet.CreateShapeModel(reduced, "auto", -0.3, 0.6, "auto", "auto", "use_polarity", "auto", "auto", out HTuple modelId);
                W("t4 OK");
                HOperatorSet.ClearShapeModel(modelId);
                img.Dispose(); rect.Dispose(); binImg.Dispose(); reduced.Dispose();
                W("SELFTEST PASS");
            }
            catch (Exception ex)
            {
                W("selftest EXCEPTION: " + ex);
            }
        }

        // ================= 工具方法 =================

        /// <summary>稳健读图：WPF 流式解码 + GenImageInterleaved 构造 HALCON 图像</summary>
        private static HImage ReadImageRobust(string path)
        {
            using (var fs = File.OpenRead(path))
            {
                var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                BitmapSource src = decoder.Frames[0];
                int w = src.PixelWidth, h = src.PixelHeight;
                if (w < 1 || h < 1) throw new InvalidOperationException("图像尺寸无效");

                BitmapSource conv = src.Format == PixelFormats.Bgr24
                    ? src
                    : new FormatConvertedBitmap(src, PixelFormats.Bgr24, null, 0);

                int stride = w * 3;
                byte[] buf = new byte[stride * h];
                conv.CopyPixels(buf, stride, 0);

                var gch = System.Runtime.InteropServices.GCHandle.Alloc(buf, System.Runtime.InteropServices.GCHandleType.Pinned);
                try
                {
                    HOperatorSet.GenImageInterleaved(out HObject img, gch.AddrOfPinnedObject(),
                                                     "bgr", w, h, -1, "byte", w, h, 0, 0, 8, 0);
                    return new HImage(img);
                }
                finally { gch.Free(); }
            }
        }

        private bool TryOpenImage(string path)
        {
            try
            {
                var img = ReadImageRobust(path);
                _vm.Images.Add(new ImageItem { Name = Path.GetFileName(path), FilePath = path, Image = img });
                _vm.SelectedImage = _vm.Images[_vm.Images.Count - 1];
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开图像失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            _shapeTool.ClearModel();
            _nccTool.ClearModel();
            _grayTool.ClearModel();
            _fitTool.ClearModel();
            _searchRegion?.Dispose();
            foreach (var item in _vm.Images) item.Image?.Dispose();
        }
    }
}
