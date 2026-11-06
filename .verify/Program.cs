using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HalconDotNet;
using VisionPositionTool.Core;
using VisionPositionTool.Models;

/// <summary>
/// 灰度模板匹配（GrayMatchTool, create_template/best_match）验证：
/// 1) text.bmp 中心 ROI 建模板 → 自匹配位置=ROI 中心、分数≈1
/// 2) 平移验证：把 ROI 区域贴到图像右下角 → 匹配到新位置
/// 3) 保存/加载往返后仍能匹配
/// </summary>
internal static class Program
{
    private static int _fitFailed;

    /// <summary>可判定断言：打印 [PASS]/[FAIL]，失败计数</summary>
    private static void Check(bool cond, string name)
    {
        Console.WriteLine((cond ? "[PASS] " : "[FAIL] ") + name);
        if (!cond) _fitFailed++;
    }

    private static void Main(string[] args)
    {
        // 诊断模式：--fitdiag <图像目录> <输出文件>：列出每张图最长边缘轮廓的包围盒与直线拟合结果
        if (args.Length >= 2 && args[0] == "--fitdiag")
        {
            FitDiag(args[1], args[2]);
            return;
        }

        string path = @"D:\cSharp\资料\项目\tool\VisionPositionTool\testdata\text.bmp";
        HImage img = ReadImageRobust(path);
        HOperatorSet.GetImageSize(img, out HTuple iw, out HTuple ih);
        Console.WriteLine("image: " + iw.I + " x " + ih.I);

        // ROI：图中心 400x400（r[824,1224] c[1024,1424]，中心 (1024,1224)）
        double r1 = 824, c1 = 1024, r2 = 1224, c2 = 1424;
        HOperatorSet.GenRectangle1(out HObject roi, r1, c1, r2, c2);
        double refR = (r1 + r2) / 2, refC = (c1 + c2) / 2;
        Console.WriteLine("ROI center: (" + refR + "," + refC + ")");

        var tool = new GrayMatchTool();
        var p = new TemplateParams();
        p.MinScore = 0.5; p.NumLevels = "auto"; p.GrayMetric = "original"; p.GraySubPixel = "true";

        if (!tool.CreateModelFromRegion(img, roi, p, out string msg)) { Console.WriteLine("create FAIL: " + msg); return; }
        Console.WriteLine("create OK: " + msg);

        // 1) 自匹配
        var o1 = tool.RunMatch(img, p, null);
        if (o1.Results.Count > 0)
            Console.WriteLine("self-match: row=" + o1.Results[0].Row + " col=" + o1.Results[0].Col +
                " angle=" + o1.Results[0].Angle + " score=" + o1.Results[0].Score +
                "  (expect ~1024, ~1224, score~1)");
        else Console.WriteLine("self-match: 0 results (FAIL)");

        // 2) 负例验证：把原图 ROI 区域涂灰（模板内容消失），应匹配不到或分数大降
        HOperatorSet.Rgb1ToGray(img, out HObject grayFullObj);
        HImage grayFull = new HImage(grayFullObj); grayFullObj.Dispose();
        HOperatorSet.PaintRegion(roi, grayFull, out HObject paintedObj, 128, "fill");
        HImage painted = new HImage(paintedObj); paintedObj.Dispose();
        var o2 = tool.RunMatch(painted, p, null);
        if (o2.Results.Count > 0)
            Console.WriteLine("grayed-out match: " + o2.Results.Count + " result score=" + o2.Results[0].Score + "  (expect 0 or low score)");
        else Console.WriteLine("grayed-out match: 0 results (OK, template content gone)");
        grayFull.Dispose(); painted.Dispose();

        // 3) 保存/加载往返
        string folder = @"D:\cSharp\资料\项目\tool\.testout\gray_model_test";
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        Directory.CreateDirectory(folder);
        Console.WriteLine("save: " + tool.SaveModel(folder, out string sm));
        var tool2 = new GrayMatchTool();
        Console.WriteLine("load: " + tool2.LoadModel(folder, out string lm));
        var o3 = tool2.RunMatch(img, p, null);
        if (o3.Results.Count > 0)
            Console.WriteLine("after-load match: row=" + o3.Results[0].Row + " col=" + o3.Results[0].Col +
                " score=" + o3.Results[0].Score + "  (expect ~1024, ~1224)");
        else Console.WriteLine("after-load match: 0 results (FAIL)");
        tool2.ClearModel();

        // 4) 搜索框限制验证（可选）：搜索域限制在右上角，应匹配不到（ROI 中心不在域内）
        HOperatorSet.GenRectangle1(out HObject searchRoi, 0, 1600, 600, 2440);
        var o4 = tool.RunMatch(img, p, searchRoi);
        Console.WriteLine("search-region restricted (ROI outside): " + o4.Results.Count + " results (expect 0)");
        searchRoi.Dispose();

        tool.ClearModel();

        // ==================== 几何拟合验证（GeometricFitTool, edges_sub_pix + fit_*_contour_xld） ====================
        Console.WriteLine();
        Console.WriteLine("==== 几何拟合验证 ====");

        // [FIT-1] 圆：理想台阶边缘（中心 256,256 半径 120），ROI 留 10px 边距。
        // 注意：离散圆的真实亚像素中心在 255.5（半像素），容差放宽到 ±1
        HImage circleImg = MakeShape("circle");
        HOperatorSet.GenRectangle1(out HObject cr, 130, 130, 382, 382);
        var fTool = new GeometricFitTool();
        var fp1 = new TemplateParams { FitType = "圆" };
        Console.WriteLine("fit create: " + fTool.CreateModelFromRegion(circleImg, cr, fp1, out string fmsg) + " " + fmsg);
        var f1 = fTool.RunMatch(circleImg, fp1, null);
        if (f1.Results.Count > 0)
        {
            var r = f1.Results[0];
            Console.WriteLine("[FIT-1] circle: row=" + r.Row + " col=" + r.Col + " size1=" + r.Size1 +
                              " err=" + r.FitError + "  (expect row/col 256±1, r 120±0.5)");
            Check(Math.Abs(r.Row - 256) <= 1 && Math.Abs(r.Col - 256) <= 1 &&
                  r.Size1.HasValue && Math.Abs(r.Size1.Value - 120) <= 0.5, "FIT-1 circle");
        }
        else { Console.WriteLine("[FIT-1] circle: 0 results"); Check(false, "FIT-1 circle"); }
        cr.Dispose();

        // [FIT-2] 矩形：GenRectangle2(256,256,0.4rad≈22.92°,130,70)，ROI 留 10px 边距
        HImage rectImg = MakeShape("rect");
        HOperatorSet.GenRectangle1(out HObject rr, 106, 106, 406, 406);
        var fp2 = new TemplateParams { FitType = "矩形" };
        fTool.CreateModelFromRegion(rectImg, rr, fp2, out _);
        var f2 = fTool.RunMatch(rectImg, fp2, null);
        if (f2.Results.Count > 0)
        {
            var r = f2.Results[0];
            Console.WriteLine("[FIT-2] rect: row=" + r.Row + " col=" + r.Col + " angle=" + r.Angle +
                              " size1=" + r.Size1 + " size2=" + r.Size2 + " err=" + r.FitError +
                              "  (expect center 256±1, 130x70±1, angle 22.92°±1 或等价表示±90°)");
            double a = r.Angle, s1 = r.Size1 ?? 0, s2 = r.Size2 ?? 0;
            bool centerOk = Math.Abs(r.Row - 256) <= 1 && Math.Abs(r.Col - 256) <= 1;
            bool sizeOk = (Math.Abs(s1 - 130) <= 1 && Math.Abs(s2 - 70) <= 1) ||
                          (Math.Abs(s1 - 70) <= 1 && Math.Abs(s2 - 130) <= 1);
            bool angleOk = Math.Abs(a - 22.92) <= 1 || Math.Abs(a - 22.92 + 90) <= 1 || Math.Abs(a - 22.92 - 90) <= 1;
            Check(centerOk && sizeOk && angleOk, "FIT-2 rectangle");
        }
        else { Console.WriteLine("[FIT-2] rect: 0 results"); Check(false, "FIT-2 rectangle"); }
        rr.Dispose();

        // [FIT-3] 椭圆：GenEllipse(256,256,0.4rad,140,90)，ROI 留 10px 边距
        HImage ellImg = MakeShape("ellipse");
        HOperatorSet.GenRectangle1(out HObject er, 96, 96, 416, 416);
        var fp3 = new TemplateParams { FitType = "椭圆" };
        fTool.CreateModelFromRegion(ellImg, er, fp3, out _);
        var f3 = fTool.RunMatch(ellImg, fp3, null);
        if (f3.Results.Count > 0)
        {
            var r = f3.Results[0];
            Console.WriteLine("[FIT-3] ellipse: row=" + r.Row + " col=" + r.Col + " angle=" + r.Angle +
                              " size1=" + r.Size1 + " size2=" + r.Size2 + " err=" + r.FitError +
                              "  (expect center 256±0.5, 140x90±1, angle 22.92°±1 或等价表示±90°)");
            double a = r.Angle, s1 = r.Size1 ?? 0, s2 = r.Size2 ?? 0;
            bool centerOk = Math.Abs(r.Row - 256) <= 0.5 && Math.Abs(r.Col - 256) <= 0.5;
            bool sizeOk = (Math.Abs(s1 - 140) <= 1 && Math.Abs(s2 - 90) <= 1) ||
                          (Math.Abs(s1 - 90) <= 1 && Math.Abs(s2 - 140) <= 1);
            bool angleOk = Math.Abs(a - 22.92) <= 1 || Math.Abs(a - 22.92 + 90) <= 1 || Math.Abs(a - 22.92 - 90) <= 1;
            Check(centerOk && sizeOk && angleOk, "FIT-3 ellipse");
        }
        else { Console.WriteLine("[FIT-3] ellipse: 0 results"); Check(false, "FIT-3 ellipse"); }
        er.Dispose();

        // [FIT-4] 直线（开轮廓）：细横条(200x3)裁剪掉两端短边，域内剩上下两条开放水平边缘
        // 注意：拟合的是上/下其中一条边缘线（行≈253 或 ≈259），不是条带中心 256
        HImage lineImg = MakeShape("line");
        HOperatorSet.GenRectangle1(out HObject lr, 240, 66, 272, 446);
        var fp4 = new TemplateParams { FitType = "直线" };
        fTool.CreateModelFromRegion(lineImg, lr, fp4, out _);
        var f4 = fTool.RunMatch(lineImg, fp4, null);
        if (f4.Results.Count > 0)
        {
            var r = f4.Results[0];
            Console.WriteLine("[FIT-4] line: row=" + r.Row + " col=" + r.Col + " angle=" + r.Angle +
                              " size1=" + r.Size1 + " err=" + r.FitError +
                              "  (expect angle 0±0.5°(或±180), row 253 或 259±1.5, col 256±3)");
            double a = Math.Abs(r.Angle);
            bool angleOk = Math.Min(a, Math.Min(Math.Abs(a - 180), Math.Abs(a + 180))) <= 0.5;
            bool rowOk = Math.Abs(r.Row - 253) <= 1.5 || Math.Abs(r.Row - 259) <= 1.5;
            bool colOk = Math.Abs(r.Col - 256) <= 3;
            Check(angleOk && rowOk && colOk, "FIT-4 line");
        }
        else { Console.WriteLine("[FIT-4] line: 0 results"); Check(false, "FIT-4 line"); }
        lr.Dispose();

        // [FIT-5] 空结果：ROI 在纯背景上，无边缘 → 0 结果、不抛异常
        HOperatorSet.GenRectangle1(out HObject emptyRoi, 20, 20, 60, 60);
        fTool.CreateModelFromRegion(circleImg, emptyRoi, fp1, out _);
        var f5 = fTool.RunMatch(circleImg, fp1, null);
        Console.WriteLine("[FIT-5] empty-ROI: " + f5.Results.Count + " results (expect 0)");
        Check(f5.Results.Count == 0, "FIT-5 empty ROI");
        emptyRoi.Dispose();

        // [FIT-6] 保存/加载往返：新工具加载后重拟合结果与 [FIT-1] 相同
        string fitFolder = @"D:\cSharp\资料\项目\tool\.testout\fit_model_test";
        if (Directory.Exists(fitFolder)) Directory.Delete(fitFolder, true);
        Directory.CreateDirectory(fitFolder);
        HOperatorSet.GenRectangle1(out HObject sr, 130, 130, 382, 382);
        fTool.CreateModelFromRegion(circleImg, sr, fp1, out _);
        sr.Dispose();
        Console.WriteLine("fit save: " + fTool.SaveModel(fitFolder, out string fsm));
        var fTool2 = new GeometricFitTool();
        Console.WriteLine("fit load: " + fTool2.LoadModel(fitFolder, out string flm));
        var f6 = fTool2.RunMatch(circleImg, fp1, null);
        if (f6.Results.Count > 0)
        {
            var r = f6.Results[0];
            Console.WriteLine("[FIT-6] after-load: type=" + r.FitType + " row=" + r.Row + " col=" + r.Col +
                              " size1=" + r.Size1 + "  (expect type=圆, same as [FIT-1]: 256±1, r 120±1)");
            Check(fTool2.FitType == "圆" && Math.Abs(r.Row - 256) <= 1 && Math.Abs(r.Col - 256) <= 1 &&
                  r.Size1.HasValue && Math.Abs(r.Size1.Value - 120) <= 1 &&
                  File.Exists(fitFolder + "\\model_fit_region.hobj") && File.Exists(fitFolder + "\\model_fit_meta.txt"),
                  "FIT-6 save/load roundtrip");
        }
        else { Console.WriteLine("[FIT-6] after-load: 0 results"); Check(false, "FIT-6 save/load roundtrip"); }
        fTool2.ClearModel();

        // [FIT-7] 真实相机图 smoke（3 通道 text.bmp → 灰度路径）：不抛异常即可
        HOperatorSet.GenRectangle1(out HObject tRoi, 824, 1024, 1224, 1424);
        fTool.CreateModelFromRegion(img, tRoi, fp1, out _);
        try
        {
            var f7 = fTool.RunMatch(img, fp1, null);
            Console.WriteLine("[FIT-7] text.bmp smoke: " + f7.Results.Count + " results (no exception)");
            Check(true, "FIT-7 text.bmp smoke");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FIT-7] text.bmp smoke EXCEPTION: " + ex);
            Check(false, "FIT-7 text.bmp smoke");
        }
        tRoi.Dispose();

        fTool.ClearModel();
        circleImg.Dispose(); rectImg.Dispose(); ellImg.Dispose(); lineImg.Dispose();

        // ==================== [FIT-8] 真实相机图回归：正方体上边缘直线（角度/位置变化） ====================
        // 背景：本工具修复前"取最长轮廓拟合"，在这组图上会选中正方体的闭合边界轮廓
        // （直线拟合残差 160px+），拟合角度随位姿漂移（67°/-75°等垃圾值）。
        // 修复后按残差过滤（FitMaxError=2.0），选中最长直线段（残差 <1px）。
        // 期望角度来自 --fitdiag 实测的直线段角度。
        Console.WriteLine();
        Console.WriteLine("==== 真实相机图回归（正方体上边缘直线） ====");
        string cubeDir = @"D:\cSharp\资料\项目\tool\VisionPositionTool\testdata\cube";
        string[] cubeNames = { "Image_20220424161805735.bmp", "Image_20220424161855055.bmp", "Image_20220424162053608.bmp" };

        void FitCase(string name, string file, double fr1, double fc1, double fr2, double fc2,
                     double gap, double expAngle, double angleTol, double expRowMin, double expRowMax, double minSize)
        {
            HImage cimg = ReadImageRobust(Path.Combine(cubeDir, file));
            try
            {
                HOperatorSet.GenRectangle1(out HObject croi, fr1, fc1, fr2, fc2);
                var toolX = new GeometricFitTool();
                var px = new TemplateParams { FitType = "直线", FitGap = gap.ToString() };
                toolX.CreateModelFromRegion(cimg, croi, px, out _);
                var ox = toolX.RunMatch(cimg, px, null);
                if (ox.Results.Count > 0)
                {
                    var r = ox.Results[0];
                    Console.WriteLine("[FIT-8" + name + "] row=" + r.Row + " col=" + r.Col + " angle=" + r.Angle +
                        " size1=" + r.Size1 + " err=" + r.FitError + "  (expect angle " + expAngle + "±" + angleTol +
                        ", row " + expRowMin + ".." + expRowMax + (minSize > 0 ? ", size1≥" + minSize : "") + ")");
                    double a = Math.Min(Math.Abs(r.Angle - expAngle),
                        Math.Min(Math.Abs(r.Angle - expAngle + 180), Math.Abs(r.Angle - expAngle - 180)));
                    bool ok = a <= angleTol && r.Row >= expRowMin && r.Row <= expRowMax;
                    if (minSize > 0) ok = ok && r.Size1.HasValue && r.Size1.Value >= minSize;
                    Check(ok, "FIT-8" + name);
                }
                else { Console.WriteLine("[FIT-8" + name + "] 0 results"); Check(false, "FIT-8" + name); }
                toolX.ClearModel();
                croi.Dispose();
            }
            finally { cimg.Dispose(); }
        }

        // 8a: img1 上边缘条带 ROI（上边缘被小产品遮挡成两段 240+178px，角度 -0.31°/0°）
        FitCase("a", cubeNames[0], 240, 350, 300, 900, 0, -0.3, 1.5, 255, 275, 0);
        // 8b: 同 ROI 开断边间隙=45 → 两段应合并成一条长直线（size1≈457）
        FitCase("b", cubeNames[0], 240, 350, 300, 900, 45, -0.3, 1.5, 255, 275, 300);
        // 8c: img1 全正方体 ROI（对抗用例：旧算法会选中闭合边界拟合出 67°；新算法应选出上边缘 -0.31°）
        FitCase("c", cubeNames[0], 240, 240, 760, 930, 0, -0.3, 1.5, 255, 275, 0);
        // 8d: img2 全正方体 ROI（立方体位置/角度不同，上边缘段 -3.85° 在 r≈679）
        FitCase("d", cubeNames[1], 230, 360, 750, 880, 0, -3.85, 1.5, 660, 700, 0);
        // 8e: img7 全正方体 ROI（上边缘双线 r≈260-265，选最长段 -0.57°）
        FitCase("e", cubeNames[2], 230, 250, 750, 950, 0, -0.57, 1.5, 250, 275, 0);

        // [FIT-9] 同一条直线轮廓上切换拟合类型的退化行为：
        // 拟合类型必须匹配目标真实几何。修复前：拟合矩形对直线轮廓抛 #3266（矩形某边无点），
        // 异常中断整个 RunMatch 并在 UI 上表现为"切换类型没有变化"（旧结果残留）；
        // 修复后：单条候选拟合失败按跳过处理，直线轮廓拟合矩形 → 0 结果且不崩
        Console.WriteLine();
        Console.WriteLine("==== 直线轮廓上切换拟合类型（退化行为） ====");
        {
            HImage lineImg2 = MakeShape("line");
            HOperatorSet.GenRectangle1(out HObject lr2, 240, 66, 272, 446);
            var t9 = new GeometricFitTool();
            var p9 = new TemplateParams();
            t9.CreateModelFromRegion(lineImg2, lr2, p9, out _);
            string[] types9 = { "直线", "矩形", "椭圆", "圆" };
            bool t9ok = true;
            foreach (string tp in types9)
            {
                p9.FitType = tp;
                var o9 = t9.RunMatch(lineImg2, p9, null);
                if (o9.Results.Count > 0)
                {
                    var r = o9.Results[0];
                    Console.WriteLine("[FIT-9] type=" + tp + " row=" + r.Row + " col=" + r.Col + " angle=" + r.Angle +
                        " size1=" + r.Size1 + " size2=" + r.Size2 + " err=" + r.FitError +
                        (tp == "直线" ? "  (expect 1 result)" : "  (degenerate fit, informational)"));
                    if (tp != "直线") t9ok = false;   // 非直线类型对直线轮廓应无合格结果
                }
                else
                {
                    Console.WriteLine("[FIT-9] type=" + tp + " 0 results" + (tp == "直线" ? " (FAIL)" : " (OK: 不匹配的几何被拒)"));
                    if (tp == "直线") t9ok = false;
                }
            }
            Check(t9ok, "FIT-9 degenerate type switch (no crash, 直线 only)");
            t9.ClearModel();
            lineImg2.Dispose();
            lr2.Dispose();
        }

        Console.WriteLine("FIT-TOTAL: failed=" + _fitFailed);
        Console.WriteLine("DONE");
        Environment.Exit(_fitFailed > 0 ? 1 : 0);
    }

    /// <summary>诊断：列出目录内每张 BMP 的最长边缘轮廓（包围盒 + 直线拟合角度/RMS）</summary>
    private static void FitDiag(string dir, string outPath)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var f in Directory.GetFiles(dir, "*.bmp"))
        {
            sb.AppendLine("=== " + Path.GetFileName(f) + " ===");
            HImage img = ReadImageRobust(f);
            try
            {
                HObject gray = ImageUtil.ToSingleChannel(img);
                try
                {
                    HOperatorSet.EdgesSubPix(gray, out HObject edges, "canny", 1.0, 20, 40);
                    try
                    {
                        HOperatorSet.CountObj(edges, out HTuple n);
                        int cnt = n.Length > 0 ? (int)n.I : 0;
                        sb.AppendLine("edge contours: " + cnt);
                        var list = new System.Collections.Generic.List<Tuple<double, double[], double[]>>();
                        for (int i = 1; i <= cnt; i++)
                        {
                            HOperatorSet.SelectObj(edges, out HObject part, i);
                            try
                            {
                                HOperatorSet.GetContourXld(part, out HTuple pr, out HTuple pc);
                                if (pr.Length == 0) continue;
                                double[] rr = pr.DArr, cc = pc.DArr;
                                double len = 0;
                                for (int k = 1; k < rr.Length; k++)
                                {
                                    double dr = rr[k] - rr[k - 1], dc = cc[k] - cc[k - 1];
                                    len += Math.Sqrt(dr * dr + dc * dc);
                                }
                                list.Add(Tuple.Create(len, rr, cc));
                            }
                            finally { part.Dispose(); }
                        }
                        list.Sort((a, b) => b.Item1.CompareTo(a.Item1));
                        for (int i = 0; i < Math.Min(6, list.Count); i++)
                        {
                            var t = list[i];
                            double[] rr = t.Item2, cc = t.Item3;
                            double r1 = double.MaxValue, r2 = double.MinValue, c1 = double.MaxValue, c2 = double.MinValue;
                            foreach (double v in rr) { if (v < r1) r1 = v; if (v > r2) r2 = v; }
                            foreach (double v in cc) { if (v < c1) c1 = v; if (v > c2) c2 = v; }
                            HOperatorSet.GenContourPolygonXld(out HObject cont, new HTuple(rr), new HTuple(cc));
                            string lineInfo;
                            try
                            {
                                HOperatorSet.FitLineContourXld(cont, "tukey", -1, 0, 5, 2,
                                    out HTuple rB, out HTuple cB, out HTuple rE, out HTuple cE, out _, out _, out _);
                                if (rB.Length == 0) { lineInfo = "line fit: no result"; }
                                else
                                {
                                    double dr = rE.D - rB.D, dc = cE.D - cB.D;
                                    double ang = Math.Atan2(dr, dc) * 180.0 / Math.PI;
                                    // 真实残差：原轮廓点到拟合线段的距离
                                    HOperatorSet.GenContourPolygonXld(out HObject lineSeg,
                                        new HTuple(new[] { rB.D, rE.D }), new HTuple(new[] { cB.D, cE.D }));
                                    try
                                    {
                                        HOperatorSet.DistancePc(lineSeg, new HTuple(rr), new HTuple(cc), out HTuple mind, out _);
                                        double[] dd = mind.DArr;
                                        double sum = 0;
                                        foreach (double d in dd) sum += d * d;
                                        double rms = Math.Sqrt(sum / dd.Length);
                                        lineInfo = "line ang=" + ang.ToString("F2") + "deg rms=" + rms.ToString("F2") + "px";
                                    }
                                    finally { lineSeg.Dispose(); }
                                }
                            }
                            catch { lineInfo = "line fit failed"; }
                            finally { cont.Dispose(); }
                            sb.AppendLine($"  #{i} len={t.Item1:F0} bbox r[{r1:F0},{r2:F0}] c[{c1:F0},{c2:F0}] pts={rr.Length} {lineInfo}");
                        }
                    }
                    finally { edges.Dispose(); }
                }
                finally { if (!ReferenceEquals(gray, img)) gray.Dispose(); }
            }
            finally { img.Dispose(); }
        }
        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine("diag written to " + outPath);
    }

    /// <summary>合成测试图：512x512 背景 0，前景 255 的理想几何形状</summary>
    private static HImage MakeShape(string kind)
    {
        HOperatorSet.GenImageConst(out HObject imgObj, "byte", 512, 512);
        HObject reg;
        switch (kind)
        {
            case "rect":
                HOperatorSet.GenRectangle2(out reg, 256, 256, 0.4, 130, 70);
                break;
            case "ellipse":
                HOperatorSet.GenEllipse(out reg, 256, 256, 0.4, 140, 90);
                break;
            case "line":
                HOperatorSet.GenRectangle2(out reg, 256, 256, 0, 200, 3);
                break;
            default:
                HOperatorSet.GenCircle(out reg, 256, 256, 120);
                break;
        }
        HOperatorSet.PaintRegion(reg, imgObj, out HObject painted, 255, "fill");
        imgObj.Dispose(); reg.Dispose();
        return new HImage(painted);
    }

    private static HImage ReadImageRobust(string path)
    {
        using (var fs = File.OpenRead(path))
        {
            var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            BitmapSource src = decoder.Frames[0];
            int w = src.PixelWidth, h = src.PixelHeight;
            BitmapSource conv = src.Format == PixelFormats.Bgr24 ? src : new FormatConvertedBitmap(src, PixelFormats.Bgr24, null, 0);
            int stride = w * 3;
            byte[] buf = new byte[stride * h];
            conv.CopyPixels(buf, stride, 0);
            var gch = System.Runtime.InteropServices.GCHandle.Alloc(buf, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                HOperatorSet.GenImageInterleaved(out HObject im, gch.AddrOfPinnedObject(), "bgr", w, h, -1, "byte", w, h, 0, 0, 8, 0);
                return new HImage(im);
            }
            finally { gch.Free(); }
        }
    }
}
