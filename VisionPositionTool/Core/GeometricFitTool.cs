using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HalconDotNet;
using VisionPositionTool.Models;

namespace VisionPositionTool.Core
{
    /// <summary>
    /// 几何拟合定位：在拟合区域（绿框）内提取亚像素边缘（edges_sub_pix），
    /// 对每条候选轮廓拟合为圆/直线/矩形/椭圆（fit_*_contour_xld）并计算残差，
    /// 选"残差 ≤ 最大拟合误差（FitMaxError）的最长轮廓"输出几何参数
    /// （中心/角度/尺寸/残差）。免模板、亚像素精度，适合圆孔定位、直边找边、
    /// 矩形件对位等精密对位场景。
    /// 残差过滤是关键：只取最长轮廓会把目标的多边形外边界/折角闭合轮廓
    /// （如正方体闭合边界，直线拟合残差 100px+）选中，结果随位置角度漂移。
    /// "模型" = 拟合区域 + 拟合类型（无模板句柄，边缘每次现提），
    /// 本模式不使用搜索框（蓝框隐藏），拟合始终在绿框区域内进行。
    /// </summary>
    public class GeometricFitTool : IMatchTool
    {
        private HObject _modelRegion;   // 拟合区域（深拷贝自持）
        private string _fitType = "圆"; // 拟合类型（保存/加载用）

        public string DisplayName => "几何拟合";
        public bool HasModel => _modelRegion != null;

        /// <summary>拟合类型（保存/加载用；RunMatch 以传入的 p.FitType 为准）</summary>
        public string FitType
        {
            get => _fitType;
            set => _fitType = KnownFitType(value) ? value : "圆";
        }

        public bool CreateModelFromRegion(HObject image, HObject roi, TemplateParams p, out string message)
        {
            // 拟合的"模型"就是拟合区域（绿框）+ 拟合类型；边缘在每次拟合时现提，无模板句柄
            message = string.Empty;
            try
            {
                // 深拷贝自持：调用方可能在 finally 里 Dispose 传入的 roi（#4056 坑）
                HOperatorSet.CopyObj(roi, out HObject roiCopy, 1, -1);
                ClearModel();
                _modelRegion = roiCopy;
                _fitType = KnownFitType(p.FitType) ? p.FitType : "圆";
                message = "拟合区域已设置";
                return true;
            }
            catch (Exception ex)
            {
                message = "设置拟合区域失败：" + ex.Message;
                return false;
            }
        }

        public MatchOutcome RunMatch(HObject image, TemplateParams p, HObject searchRegion)
        {
            var outcome = new MatchOutcome();
            if (_modelRegion == null) return outcome;

            // 几何拟合始终在拟合区域（绿框）内提边拟合；本模式蓝搜索框隐藏，传入的 searchRegion 忽略
            HObject gray = ImageUtil.ToSingleChannel(image);
            try
            {
                HOperatorSet.ReduceDomain(gray, _modelRegion, out HObject reduced);
                try
                {
                    HOperatorSet.EdgesSubPix(reduced, out HObject edges, p.FitEdgeFilter,
                                             ParamConv.Num(p.FitAlpha), ParamConv.Num(p.FitLow), ParamConv.Num(p.FitHigh));
                    try
                    {
                        HOperatorSet.CountObj(edges, out HTuple en);
                        if (en.Length == 0 || en.I == 0) return outcome;   // 区域内没有边缘

                        // 按长度过滤短轮廓（噪声）。陷阱：Max1 默认 200，长轮廓会被过滤掉，必须显式传大值
                        HOperatorSet.SelectContoursXld(edges, out HObject selected, "contour_length",
                                                      ParamConv.Num(p.FitMinContourLength), 99999.0, -0.5, 0.5);
                        try
                        {
                            HOperatorSet.CountObj(selected, out HTuple sn);
                            int cnt = sn.Length > 0 ? (int)sn.I : 0;
                            if (cnt == 0) return outcome;

                            // 收集候选轮廓点列（get_contour_xld 只接受单轮廓，需 select_obj 逐条取）
                            var parts = new List<double[][]>();   // 每项 [0]=rows [1]=cols
                            for (int i = 1; i <= cnt; i++)
                            {
                                HOperatorSet.SelectObj(selected, out HObject part, i);
                                try
                                {
                                    HOperatorSet.GetContourXld(part, out HTuple pr, out HTuple pc);
                                    if (pr.Length < 5) continue;   // 少于 5 点不拟合
                                    parts.Add(new[] { pr.DArr, pc.DArr });
                                }
                                finally { part.Dispose(); }
                            }
                            if (parts.Count == 0) return outcome;

                            double maxErr = ParseDouble(p.FitMaxError, 2.0);
                            double gap = ParseDouble(p.FitGap, 0);

                            // 断边拼接（FitGap>0）：合并端点间隙 ≤ gap、且合并后拟合残差仍 ≤ 最大拟合误差的
                            // 轮廓对（如被遮挡物截断的直线/圆弧断口）。不用 union_adjacent_contours_xld——
                            // 它只按端点距离合并、不看几何形状，会把直线段与邻近无关轮廓拼成弯折轮廓，
                            // 拼错后整条被残差过滤拒掉（表现为 0 结果）。
                            if (gap > 0 && parts.Count > 1)
                            {
                                bool mergedAny = true;
                                while (mergedAny)
                                {
                                    mergedAny = false;
                                    for (int i = 0; i < parts.Count && !mergedAny; i++)
                                    {
                                        for (int j = i + 1; j < parts.Count && !mergedAny; j++)
                                        {
                                            if (MinEndpointDistance(parts[i], parts[j]) > gap) continue;
                                            double[][] m = ConcatPoints(parts[i], parts[j]);
                                            FitProbe probe = TryFit(p.FitType, m[0], m[1]);
                                            if (probe == null) continue;
                                            double rms = probe.Result.FitError ?? double.MaxValue;
                                            if (maxErr > 0 && rms > maxErr) continue;
                                            parts[i] = m;
                                            parts.RemoveAt(j);
                                            mergedAny = true;
                                        }
                                    }
                                }
                            }

                            // 对每条候选轮廓拟合并计算残差，选"残差达标（≤ 最大拟合误差）的最长轮廓"。
                            // 不能只取最长轮廓：ROI 里最长的往往是目标的多边形外边界/折角闭合轮廓
                            // （如正方体闭合边界，直线拟合残差 100px+），拟合结果随目标位置角度漂移；
                            // 残差过滤自动排除非目标几何，只留真正接近所选形状的轮廓。
                            FitProbe best = null;
                            double bestLen = -1;
                            foreach (var part in parts)
                            {
                                FitProbe probe = TryFit(p.FitType, part[0], part[1]);
                                if (probe == null) continue;
                                double rms = probe.Result.FitError ?? double.MaxValue;
                                if (maxErr > 0 && rms > maxErr) continue;
                                double len = PolylineLength(part[0], part[1]);
                                if (len > bestLen) { bestLen = len; best = probe; }
                            }
                            if (best == null) return outcome;
                            outcome.Crosses.Add(best.Center);
                            if (best.Polyline.Count > 0) outcome.Contours.Add(best.Polyline);
                            outcome.Results.Add(best.Result);
                        }
                        finally { selected.Dispose(); }
                    }
                    finally { edges.Dispose(); }
                }
                finally { reduced.Dispose(); }
            }
            finally { if (!ReferenceEquals(gray, image)) gray.Dispose(); }
            return outcome;
        }

        /// <summary>单条轮廓的拟合结果（供候选选择与最终输出）</summary>
        private sealed class FitProbe
        {
            public MatchResult Result;
            public List<Point2> Polyline;
            public Point2 Center;
        }

        /// <summary>对单条轮廓（点列）按类型拟合；拟合失败/输出为空返回 null</summary>
        private FitProbe TryFit(string fitType, double[] rows, double[] cols)
        {
            if (rows.Length < 5) return null;   // 少于 5 点不拟合
            HOperatorSet.GenContourPolygonXld(out HObject contour, new HTuple(rows), new HTuple(cols));
            try
            {
                return TryFitCore(fitType, contour, rows, cols);
            }
            catch
            {
                // 拟合算子对退化轮廓会抛异常（如直线拟合矩形：#3266 矩形某边无点），
                // 按"该轮廓拟合失败"跳过——不能让单条候选的异常中断整个 RunMatch
                return null;
            }
            finally { contour.Dispose(); }
        }

        /// <summary>对给定轮廓对象按类型拟合；拟合失败/输出为空返回 null</summary>
        private FitProbe TryFitCore(string fitType, HObject contour, double[] rows, double[] cols)
        {
            string type = KnownFitType(fitType) ? fitType : "圆";
            double row, col, angleDeg, size1 = 0, size2 = 0, error = -1;
            List<Point2> polyline;

            switch (type)
            {
                case "直线":
                    {
                        HOperatorSet.FitLineContourXld(contour, "tukey", -1, 0, 5, 2,
                            out HTuple rB, out HTuple cB, out HTuple rE, out HTuple cE, out _, out _, out _);
                        if (rB.Length == 0) return null;
                        double rb = rB.D, cb = cB.D, re = rE.D, ce = cE.D;
                        double dr = re - rb, dc = ce - cb;
                        double len = Math.Sqrt(dr * dr + dc * dc);
                        if (len < 1e-9) return null;
                        row = (rb + re) / 2; col = (cb + ce) / 2;    // 中心 = 拟合线段中点
                        angleDeg = NormDeg(Math.Atan2(dr, dc) * 180.0 / Math.PI);   // 方向角
                        size1 = len;
                        // 拟合模型 = 线段（2 点折线），残差用 distance_pc 统一计算
                        HOperatorSet.GenContourPolygonXld(out HObject model,
                            new HTuple(new[] { rb, re }), new HTuple(new[] { cb, ce }));
                        error = RmsToContour(model, rows, cols);
                        polyline = SampleXld(model);
                        break;
                    }
                case "矩形":
                    {
                        HOperatorSet.FitRectangle2ContourXld(contour, "tukey", -1, 0, 0, 5, 2,
                            out HTuple r, out HTuple c, out HTuple phi, out HTuple l1, out HTuple l2, out _);
                        if (r.Length == 0) return null;
                        row = r.D; col = c.D; size1 = l1.D; size2 = l2.D;
                        angleDeg = NormDeg(phi.D * 180.0 / Math.PI);
                        HObject model = GenRectangle2ContourXld(row, col, phi.D, l1.D, l2.D);
                        error = RmsToContour(model, rows, cols);
                        polyline = SampleXld(model);
                        break;
                    }
                case "椭圆":
                    {
                        HOperatorSet.FitEllipseContourXld(contour, "geotukey", -1, 0, 0, 200, 3, 2,
                            out HTuple r, out HTuple c, out HTuple phi, out HTuple r1, out HTuple r2, out _, out _, out _);
                        if (r.Length == 0) return null;
                        if (r1.D <= 0 || r2.D <= 0.001) return null;   // 短轴≈0 = 退化成直线的椭圆，无效
                        row = r.D; col = c.D; size1 = r1.D; size2 = r2.D;
                        angleDeg = NormDeg(phi.D * 180.0 / Math.PI);
                        // StartPhi/EndPhi（弧段角）忽略，始终画完整椭圆展示拟合模型
                        HObject model = GenEllipseContourXld(row, col, phi.D, r1.D, r2.D,
                            0, Math.PI * 2, "positive", Math.Max(1.0, Math.Min(r1.D, r2.D) / 180.0));
                        error = RmsToContour(model, rows, cols);
                        polyline = SampleXld(model);
                        break;
                    }
                default: // 圆
                    {
                        HOperatorSet.FitCircleContourXld(contour, "geotukey", -1, 0, 0, 5, 2,
                            out HTuple r, out HTuple c, out HTuple rad, out _, out _, out _);
                        if (r.Length == 0) return null;
                        row = r.D; col = c.D; size1 = rad.D;
                        angleDeg = 0;   // 单一圆没有角度信息
                        HObject model = GenCircleContourXld(row, col, rad.D,
                            0, Math.PI * 2, "positive", Math.Max(1.0, rad.D / 180.0));
                        error = RmsToContour(model, rows, cols);
                        polyline = SampleXld(model);
                        break;
                    }
            }

            return new FitProbe
            {
                Center = new Point2(row, col),
                Polyline = polyline,
                Result = new MatchResult
                {
                    Index = 0,
                    Row = Math.Round(row, 3),
                    Col = Math.Round(col, 3),
                    Angle = Math.Round(angleDeg, 3),
                    Score = 0,
                    FitType = type,
                    Size1 = Math.Round(size1, 3),
                    Size2 = (type == "圆" || type == "直线") ? (double?)null : Math.Round(size2, 3),
                    FitError = error < 0 ? (double?)null : Math.Round(error, 3)
                }
            };
        }

        public bool SaveModel(string folder, out string message)
        {
            message = string.Empty;
            try
            {
                if (_modelRegion == null) { message = "没有可保存的拟合区域"; return false; }
                HOperatorSet.WriteRegion(_modelRegion, folder + "\\model_fit_region.hobj");
                // 拟合参数由界面在 RunMatch 时传入，无需持久化；只存拟合类型（加载后回填界面）
                System.IO.File.WriteAllLines(folder + "\\model_fit_meta.txt", new[] { _fitType });
                message = "保存拟合区域成功：" + folder;
                return true;
            }
            catch (Exception ex) { message = "保存拟合区域失败：" + ex.Message; return false; }
        }

        public bool LoadModel(string folder, out string message)
        {
            message = string.Empty;
            try
            {
                string regionPath = folder + "\\model_fit_region.hobj";
                if (!System.IO.File.Exists(regionPath))
                {
                    message = "该文件夹里没有几何拟合文件（model_fit_region.hobj）。\n请选择「几何拟合」模式下保存过模板的文件夹。";
                    return false;
                }
                // 先读临时再替换，加载中途失败不丢旧模型
                HOperatorSet.ReadRegion(out HObject loaded, regionPath);
                ClearModel();
                _modelRegion = loaded;
                _fitType = "圆";
                string meta = folder + "\\model_fit_meta.txt";
                if (System.IO.File.Exists(meta))
                {
                    var lines = System.IO.File.ReadAllLines(meta);
                    if (lines.Length > 0) FitType = lines[0].Trim();
                }
                message = "加载拟合区域成功：" + folder;
                return true;
            }
            catch (Exception ex) { message = "加载拟合区域失败：" + ex.Message; return false; }
        }

        public bool TryGetModelRoi(out double r1, out double c1, out double r2, out double c2)
        {
            r1 = c1 = r2 = c2 = 0;
            if (_modelRegion == null) return false;
            HOperatorSet.SmallestRectangle1(_modelRegion, out HTuple hr1, out HTuple hc1, out HTuple hr2, out HTuple hc2);
            r1 = hr1.D; c1 = hc1.D; r2 = hr2.D; c2 = hc2.D;
            return true;
        }

        /// <summary>拟合无模板轮廓概念（结果几何每次现算），不提供基础轮廓</summary>
        public bool TryGetBaseContour(out List<double[]> rowsParts, out List<double[]> colsParts)
        {
            rowsParts = new List<double[]>();
            colsParts = new List<double[]>();
            return false;
        }

        public void ClearModel()
        {
            if (_modelRegion != null)
            {
                _modelRegion.Dispose();
                _modelRegion = null;
            }
        }

        // ================= 辅助 =================

        private static bool KnownFitType(string t) =>
            t == "圆" || t == "直线" || t == "矩形" || t == "椭圆";

        /// <summary>数字参数解析（断边间隙等，非法值用默认）</summary>
        private static double ParseDouble(string s, double fallback)
        {
            if (double.TryParse((s ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return v;
            return fallback;
        }

        /// <summary>角度归一化到 [-180,180)（与 ShapeMatchTool 显示惯例一致）</summary>
        private static double NormDeg(double deg) =>
            ((deg + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;

        /// <summary>折线长度（相邻点欧氏距离累加）</summary>
        private static double PolylineLength(double[] rows, double[] cols)
        {
            double len = 0;
            for (int i = 1; i < rows.Length; i++)
            {
                double dr = rows[i] - rows[i - 1], dc = cols[i] - cols[i - 1];
                len += Math.Sqrt(dr * dr + dc * dc);
            }
            return len;
        }

        /// <summary>两条折线端点间的最小距离（4 种端点组合）</summary>
        private static double MinEndpointDistance(double[][] a, double[][] b)
        {
            double[] ar = a[0], ac = a[1], br = b[0], bc = b[1];
            double d0 = Hypot(ar[0] - br[0], ac[0] - bc[0]);
            double d1 = Hypot(ar[0] - br[br.Length - 1], ac[0] - bc[bc.Length - 1]);
            double d2 = Hypot(ar[ar.Length - 1] - br[0], ac[ac.Length - 1] - bc[0]);
            double d3 = Hypot(ar[ar.Length - 1] - br[br.Length - 1], ac[ac.Length - 1] - bc[bc.Length - 1]);
            return Math.Min(Math.Min(d0, d1), Math.Min(d2, d3));
        }

        /// <summary>按最近端点对接拼接两条折线的点列（保持点序连续，a 在前 b 在后）</summary>
        private static double[][] ConcatPoints(double[][] a, double[][] b)
        {
            double[] ar = a[0], ac = a[1], br = b[0], bc = b[1];
            double d00 = Hypot(ar[0] - br[0], ac[0] - bc[0]);
            double d01 = Hypot(ar[0] - br[br.Length - 1], ac[0] - bc[bc.Length - 1]);
            double d10 = Hypot(ar[ar.Length - 1] - br[0], ac[ac.Length - 1] - bc[0]);
            double d11 = Hypot(ar[ar.Length - 1] - br[br.Length - 1], ac[ac.Length - 1] - bc[bc.Length - 1]);
            double[] rr, cc;
            if (d10 <= d00 && d10 <= d01 && d10 <= d11)
            {
                rr = ar.Concat(br).ToArray(); cc = ac.Concat(bc).ToArray();            // a→b
            }
            else if (d11 <= d00 && d11 <= d01 && d11 <= d10)
            {
                rr = ar.Concat(br.Reverse()).ToArray(); cc = ac.Concat(bc.Reverse()).ToArray();   // a→b反
            }
            else if (d00 <= d01 && d00 <= d10 && d00 <= d11)
            {
                rr = ar.Reverse().Concat(br).ToArray(); cc = ac.Reverse().Concat(bc).ToArray();   // a反→b
            }
            else
            {
                rr = ar.Reverse().Concat(br.Reverse()).ToArray(); cc = ac.Reverse().Concat(bc.Reverse()).ToArray();
            }
            return new[] { rr, cc };
        }

        private static double Hypot(double dr, double dc) => Math.Sqrt(dr * dr + dc * dc);

        /// <summary>生成 XLD（单个轮廓对象）→ 采样为 Point2 折线（显示用，图像坐标）</summary>
        private static List<Point2> SampleXld(HObject cont)
        {
            var pts = new List<Point2>();
            try
            {
                HOperatorSet.GetContourXld(cont, out HTuple r, out HTuple c);
                if (r.Length > 0)
                {
                    double[] rr = r.DArr, cc = c.DArr;
                    for (int i = 0; i < rr.Length; i++) pts.Add(new Point2(rr[i], cc[i]));
                }
            }
            finally { cont.Dispose(); }
            return pts;
        }

        private static HObject GenCircleContourXld(double row, double col, double radius,
            double startPhi, double endPhi, string pointOrder, double resolution)
        {
            HOperatorSet.GenCircleContourXld(out HObject cont, row, col, radius,
                                             startPhi, endPhi, pointOrder, resolution);
            return cont;
        }

        private static HObject GenRectangle2ContourXld(double row, double col, double phi,
            double length1, double length2)
        {
            HOperatorSet.GenRectangle2ContourXld(out HObject cont, row, col, phi, length1, length2);
            return cont;
        }

        private static HObject GenEllipseContourXld(double row, double col, double phi,
            double radius1, double radius2, double startPhi, double endPhi, string pointOrder, double resolution)
        {
            HOperatorSet.GenEllipseContourXld(out HObject cont, row, col, phi, radius1, radius2,
                                              startPhi, endPhi, pointOrder, resolution);
            return cont;
        }

        // ---- RMS 残差（fit 算子不回传误差，用 distance_pc 算轮廓点到拟合几何的最小距离，单位像素） ----

        /// <summary>轮廓点到拟合模型 XLD 的 RMS 距离（distance_pc 返回每个点的最小距离）</summary>
        private static double RmsToContour(HObject modelCont, double[] rows, double[] cols)
        {
            try
            {
                HOperatorSet.DistancePc(modelCont, new HTuple(rows), new HTuple(cols), out HTuple mind, out HTuple maxd);
                if (mind.Length == 0) return -1;
                double[] d = mind.DArr;
                double sum = 0;
                for (int i = 0; i < d.Length; i++) sum += d[i] * d[i];
                return Math.Sqrt(sum / d.Length);
            }
            catch
            {
                return -1;
            }
        }
    }
}
