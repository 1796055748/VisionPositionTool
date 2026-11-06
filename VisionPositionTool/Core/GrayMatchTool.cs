using System;
using System.Collections.Generic;
using HalconDotNet;
using VisionPositionTool.Models;

namespace VisionPositionTool.Core
{
    /// <summary>
    /// 灰度模板匹配：create_template / best_match（基于灰度值的经典模板匹配）。
    /// 直接在灰度图上滑窗找最佳匹配，只支持平移定位（不支持旋转/缩放），
    /// 适合目标姿态固定、纹理灰度特征明显的场景。单次返回最佳匹配。
    /// </summary>
    public class GrayMatchTool : IMatchTool
    {
        private HTuple _templateId;          // create_template 的模板句柄（内存对象，不能持久化）
        private HImage _templateImg;         // 模板灰度图（ROI 裁剪，自持）
        private HObject _modelRegion;        // 模型 ROI（深拷贝自持）
        private int _tplW, _tplH;            // 模板尺寸
        private int _numLevels = 4;          // 创建模板时的金字塔层数（保存/加载还原用）
        private string _grayValues = "original";   // 创建模板时的灰度值模式

        public string DisplayName => "灰度模板";
        public bool HasModel => _templateId != null;

        public bool CreateModelFromRegion(HObject image, HObject roi, TemplateParams p, out string message)
        {
            message = string.Empty;
            try
            {
                HOperatorSet.ReduceDomain(image, roi, out HObject reduced);
                try
                {
                    HOperatorSet.CropDomain(reduced, out HObject cropped);
                    try
                    {
                        // create_template 要求单通道灰度图
                        HObject tplGray = ImageUtil.ToSingleChannel(cropped);
                        try
                        {
                            HOperatorSet.GetImageSize(tplGray, out HTuple tw, out HTuple th);
                            _tplW = tw.I; _tplH = th.I;
                            int nl = ParseInt(p.NumLevels, 4);
                            _grayValues = p.GrayMetric == "normalized" ? "normalized" : "original";

                            // 先创建成功再替换旧模型，参数非法时不丢失已有模板
                            HOperatorSet.CreateTemplate(tplGray, 255, nl, "sort", _grayValues, out HTuple newId);
                            ClearModel();
                            _templateId = newId;
                            _numLevels = nl;
                            // 深拷贝自持：调用方可能在 finally 里 Dispose 传入的 roi / 中间对象
                            HOperatorSet.CopyObj(roi, out HObject roiCopy, 1, -1);
                            _modelRegion = roiCopy;
                            _templateImg = new HImage(tplGray);
                            message = "创建灰度模板成功";
                            return true;
                        }
                        finally { if (!ReferenceEquals(tplGray, cropped)) tplGray.Dispose(); }
                    }
                    finally { cropped.Dispose(); }
                }
                finally { reduced.Dispose(); }
            }
            catch (Exception ex)
            {
                message = "创建灰度模板失败：" + ex.Message;
                return false;
            }
        }

        public MatchOutcome RunMatch(HObject image, TemplateParams p, HObject searchRegion)
        {
            var outcome = new MatchOutcome();
            if (_templateId == null || _templateImg == null) return outcome;

            // create_template 要求单通道图像
            HObject gray = ImageUtil.ToSingleChannel(image);
            try
            {
                // 有搜索框时限制搜索区域（reduce_domain 保持原图像坐标，结果仍是绝对坐标）
                HObject searchImg = gray;
                bool disposeReduced = false;
                if (searchRegion != null)
                {
                    HOperatorSet.ReduceDomain(gray, searchRegion, out searchImg);
                    disposeReduced = true;
                }
                try
                {
                    // MinScore(0~1) 换算成 best_match 的绝对误差上限：
                    // 允许平均每像素误差 = 255 * (1 - MinScore)，总误差 = 平均误差 * 模板像素数
                    double maxError = 255.0 * _tplW * _tplH * (1.0 - Math.Max(0.0, Math.Min(1.0, p.MinScore)));
                    string subPixel = p.GraySubPixel == "false" ? "false" : "true";
                    HOperatorSet.BestMatch(searchImg, _templateId, maxError, subPixel,
                                           out HTuple row, out HTuple col, out HTuple error);

                    // 无匹配时 best_match 返回空元组，直接访问 .DArr 会抛 HTupleAccessException
                    if (row.Length == 0) return outcome;

                    // best_match 返回的位置就是模板中心（实测自匹配返回 ROI 中心），无需再偏移
                    double centerR = row.D, centerC = col.D;
                    double err = error.Length > 0 ? error.D : double.MaxValue;
                    // 分数：0 误差 → 1.0，误差达到 maxError → 0（clamp 到 [0,1]）
                    double score = Math.Max(0.0, Math.Min(1.0, 1.0 - err / (255.0 * _tplW * _tplH)));

                    outcome.Crosses.Add(new Point2(centerR, centerC));
                    // 结果轮廓 = 模板矩形框（以中心 (centerR,centerC) 展开），5 点闭合折线
                    double r0 = centerR - (_tplH - 1) / 2.0, c0 = centerC - (_tplW - 1) / 2.0;
                    var rect = new List<Point2>
                    {
                        new Point2(r0, c0), new Point2(r0, c0 + _tplW - 1),
                        new Point2(r0 + _tplH - 1, c0 + _tplW - 1), new Point2(r0 + _tplH - 1, c0),
                        new Point2(r0, c0)
                    };
                    outcome.Contours.Add(rect);
                    outcome.Results.Add(new MatchResult
                    {
                        Index = 0,
                        Row = Math.Round(centerR, 3),
                        Col = Math.Round(centerC, 3),
                        Angle = 0,   // create_template 不支持旋转
                        Score = Math.Round(score, 4)
                    });
                    return outcome;
                }
                finally { if (disposeReduced) searchImg.Dispose(); }
            }
            finally { if (!ReferenceEquals(gray, image)) gray.Dispose(); }
        }

        public bool SaveModel(string folder, out string message)
        {
            message = string.Empty;
            try
            {
                if (_templateImg == null || _modelRegion == null) { message = "没有可保存的模板"; return false; }
                HOperatorSet.WriteImage(_templateImg, "tiff", 0, folder + "\\model_tpl.tif");
                HOperatorSet.WriteRegion(_modelRegion, folder + "\\model_region.hobj");
                // 重建参数元信息（金字塔层数/灰度值模式），加载时还原
                System.IO.File.WriteAllLines(folder + "\\model_meta.txt",
                    new[] { _numLevels.ToString(), _grayValues });
                message = "保存模板成功：" + folder;
                return true;
            }
            catch (Exception ex) { message = "保存模板失败：" + ex.Message; return false; }
        }

        public bool LoadModel(string folder, out string message)
        {
            message = string.Empty;
            try
            {
                string tplPath = folder + "\\model_tpl.tif";
                if (!System.IO.File.Exists(tplPath))
                {
                    message = "该文件夹里没有灰度模板文件（model_tpl.tif）。\n请选择「灰度模板」模式下保存过模板的文件夹。";
                    return false;
                }
                HOperatorSet.ReadImage(out HObject tpl, tplPath);
                try
                {
                    HObject tplGray = ImageUtil.ToSingleChannel(tpl);
                    try
                    {
                        // 从元信息还原创建参数（缺省时用默认）
                        int nl = 4; string gv = "original";
                        string meta = folder + "\\model_meta.txt";
                        if (System.IO.File.Exists(meta))
                        {
                            var lines = System.IO.File.ReadAllLines(meta);
                            if (lines.Length > 0) int.TryParse(lines[0], out nl);
                            if (lines.Length > 1 && lines[1] == "normalized") gv = "normalized";
                        }
                        HOperatorSet.CreateTemplate(tplGray, 255, nl, "sort", gv, out HTuple newId);
                        ClearModel();
                        _templateId = newId;
                        _numLevels = nl;
                        _grayValues = gv;
                        _templateImg = new HImage(tplGray);
                        HOperatorSet.GetImageSize(tplGray, out HTuple tw, out HTuple th);
                        _tplW = tw.I; _tplH = th.I;
                    }
                    finally { if (!ReferenceEquals(tplGray, tpl)) tplGray.Dispose(); }
                }
                finally { tpl.Dispose(); }
                HOperatorSet.ReadRegion(out _modelRegion, folder + "\\model_region.hobj");
                message = "加载模板成功：" + folder;
                return true;
            }
            catch (Exception ex) { message = "加载模板失败：" + ex.Message; return false; }
        }

        public bool TryGetModelRoi(out double r1, out double c1, out double r2, out double c2)
        {
            r1 = c1 = r2 = c2 = 0;
            if (_modelRegion == null) return false;
            HOperatorSet.SmallestRectangle1(_modelRegion, out HTuple hr1, out HTuple hc1, out HTuple hr2, out HTuple hc2);
            r1 = hr1.D; c1 = hc1.D; r2 = hr2.D; c2 = hc2.D;
            return true;
        }

        /// <summary>灰度模板无边缘轮廓概念，不提供基础轮廓（模板轮廓预览不显示）</summary>
        public bool TryGetBaseContour(out List<double[]> rowsParts, out List<double[]> colsParts)
        {
            rowsParts = new List<double[]>();
            colsParts = new List<double[]>();
            return false;
        }

        public void ClearModel()
        {
            if (_templateId != null)
            {
                HOperatorSet.ClearTemplate(_templateId);
                _templateId = null;
            }
            if (_templateImg != null)
            {
                _templateImg.Dispose();
                _templateImg = null;
            }
            if (_modelRegion != null)
            {
                _modelRegion.Dispose();
                _modelRegion = null;
            }
        }

        /// <summary>解析金字塔层数："auto" 或非法值用默认 4</summary>
        private static int ParseInt(string s, int fallback)
        {
            if (int.TryParse((s ?? string.Empty).Trim(), out int v) && v >= 1) return v;
            return fallback;
        }
    }
}
