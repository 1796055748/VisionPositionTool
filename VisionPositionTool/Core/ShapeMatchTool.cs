using System;
using System.Collections.Generic;
using HalconDotNet;
using VisionPositionTool.Models;

namespace VisionPositionTool.Core
{
    /// <summary>
    /// 模板匹配（形状匹配）：create_shape_model / find_shape_model
    /// 通过边缘/轮廓特征进行定位，鲁棒性好，是工业视觉中最常用的定位方式。
    /// </summary>
    public class ShapeMatchTool : IMatchTool
    {
        private HTuple _modelId;
        private HObject _modelRegion;
        private readonly List<double[]> _baseRowsParts = new List<double[]>();   // 模板基础轮廓（分段，模型坐标系，参考点 0,0）
        private readonly List<double[]> _baseColsParts = new List<double[]>();

        public string DisplayName => "模板匹配";
        public bool HasModel => _modelId != null;

        public bool CreateModelFromRegion(HObject image, HObject roi, TemplateParams p, out string message)
        {
            message = string.Empty;
            try
            {
                HOperatorSet.ReduceDomain(image, roi, out HObject reduced);
                try
                {
                    // 先创建成功再替换旧模型，参数非法时不丢失已有模板
                    // 注意：create_shape_model 角度参数单位是弧度
                    HOperatorSet.CreateShapeModel(reduced, ParamConv.Int(p.NumLevels), p.StartAngleRad, p.AngleExtentRad,
                                                  ParamConv.Rad(p.AngleStep), p.Optimization, p.Metric,
                                                  ParamConv.Num(p.Contrast), ParamConv.Num(p.MinContrast), out HTuple newId);
                    ClearModel();
                    _modelId = newId;
                    LoadBaseContour();
                    // 深拷贝 ROI：调用方可能在 finally 里 Dispose 传入的 roi，不能直接持有引用
                    HOperatorSet.CopyObj(roi, out HObject roiCopy, 1, -1);
                    _modelRegion = roiCopy;
                    message = "创建模板成功";
                    return true;
                }
                finally { reduced.Dispose(); }
            }
            catch (Exception ex)
            {
                message = "创建模板失败：" + ex.Message;
                return false;
            }
        }

        public MatchOutcome RunMatch(HObject image, TemplateParams p, HObject searchRegion)
        {
            var outcome = new MatchOutcome();
            if (_modelId == null) return outcome;

            // 有搜索框时限制搜索区域（reduce_domain 保持原图像坐标，结果仍是绝对坐标）
            HObject searchImg = image;
            bool disposeReduced = false;
            if (searchRegion != null)
            {
                HOperatorSet.ReduceDomain(image, searchRegion, out searchImg);
                disposeReduced = true;
            }
            try
            {
                HOperatorSet.FindShapeModel(searchImg, _modelId, p.StartAngleRad, p.AngleExtentRad, p.MinScore,
                                            p.NumMatches, p.MaxOverlap, p.ShapeSubPixel, 0, p.Greediness,
                                            out HTuple row, out HTuple col, out HTuple angle, out HTuple score);

            // 无匹配时 find_shape_model 返回空元组，直接访问 .DArr 会抛 HTupleAccessException
            if (row.Length == 0) return outcome;

            double[] rows = row.DArr, cols = col.DArr, angles = angle.DArr, scores = score.DArr;
            bool hasBase = _baseRowsParts.Count > 0;

            for (int i = 0; i < rows.Length; i++)
            {
                if (hasBase)
                {
                    HOperatorSet.VectorAngleToRigid(0, 0, 0, rows[i], cols[i], angles[i], out HTuple hom);
                    // 分段轮廓：每段独立变换、独立折线，避免不连续子轮廓被连成一条线
                    for (int part = 0; part < _baseRowsParts.Count; part++)
                    {
                        HOperatorSet.AffineTransPoint2d(hom, new HTuple(_baseRowsParts[part]), new HTuple(_baseColsParts[part]), out HTuple tr, out HTuple tc);

                        var contour = new List<Point2>();
                        double[] trr = tr.DArr, tcc = tc.DArr;
                        for (int k = 0; k < trr.Length; k++) contour.Add(new Point2(trr[k], tcc[k]));
                        outcome.Contours.Add(contour);
                    }
                }
                outcome.Crosses.Add(new Point2(rows[i], cols[i]));

                // 角度归一化到 [-180,180)：搜索范围含 ±360 时 HALCON 返回等价角（0° 可能报成 ±360°），统一显示
                double deg = angles[i] * 180.0 / Math.PI;
                deg = ((deg + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;

                outcome.Results.Add(new MatchResult
                {
                    Index = i,
                    Row = Math.Round(rows[i], 3),
                    Col = Math.Round(cols[i], 3),
                    Angle = Math.Round(deg, 3),
                    Score = Math.Round(scores[i], 4)
                });
            }
            return outcome;
            }
            finally { if (disposeReduced) searchImg.Dispose(); }
        }

        public bool SaveModel(string folder, out string message)
        {
            message = string.Empty;
            try
            {
                if (_modelId == null || _modelRegion == null) { message = "没有可保存的模板"; return false; }
                HOperatorSet.WriteShapeModel(_modelId, folder + "\\model.shm");
                HOperatorSet.WriteRegion(_modelRegion, folder + "\\model_region.hobj");
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
                string modelPath = folder + "\\model.shm";
                if (!System.IO.File.Exists(modelPath))
                {
                    message = "该文件夹里没有模板匹配文件（model.shm）。\n请选择「模板匹配」模式下保存过模板的文件夹。";
                    return false;
                }
                HOperatorSet.ReadShapeModel(modelPath, out _modelId);
                HOperatorSet.ReadRegion(out _modelRegion, folder + "\\model_region.hobj");
                LoadBaseContour();
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

        public bool TryGetBaseContour(out List<double[]> rowsParts, out List<double[]> colsParts)
        {
            rowsParts = _baseRowsParts;
            colsParts = _baseColsParts;
            return rowsParts.Count > 0 && colsParts.Count == rowsParts.Count;
        }

        public void ClearModel()
        {
            if (_modelId != null)
            {
                HOperatorSet.ClearShapeModel(_modelId);
                _modelId = null;
            }
            if (_modelRegion != null)
            {
                _modelRegion.Dispose();
                _modelRegion = null;
            }
            _baseRowsParts.Clear();
            _baseColsParts.Clear();
        }

        private void LoadBaseContour()
        {
            // 轮廓提取失败不应影响创建/加载主流程
            _baseRowsParts.Clear();
            _baseColsParts.Clear();
            try
            {
                HOperatorSet.GetShapeModelContours(out HObject contour, _modelId, 1);
                try
                {
                    // 模型轮廓可能由多段子轮廓组成（count_obj>1），get_contour_xld 只接受单段，需逐段提取
                    HOperatorSet.CountObj(contour, out HTuple n);
                    int cnt = n.Length > 0 ? (int)n.I : 0;
                    for (int i = 1; i <= cnt; i++)
                    {
                        HOperatorSet.SelectObj(contour, out HObject part, i);
                        try
                        {
                            HOperatorSet.GetContourXld(part, out HTuple cr, out HTuple cc);
                            if (cr.Length > 0)
                            {
                                _baseRowsParts.Add(cr.DArr);
                                _baseColsParts.Add(cc.DArr);
                            }
                        }
                        finally { part.Dispose(); }
                    }
                }
                finally { contour.Dispose(); }
            }
            catch { _baseRowsParts.Clear(); _baseColsParts.Clear(); }
        }
    }
}
