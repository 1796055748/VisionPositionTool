# 视觉定位工具 VisionPositionTool

基于 HALCON 24.11 的 WPF 视觉定位工具（C#，x64）。用户是中文 C# 上位机开发者。

## 构建与环境（关键约束）

- **TargetFramework: net48（.NET Framework 4.8）** — HALCON 24.11 的 .NET 包装在 net9 下用户桌面 fail-fast，必须在 .NET Framework 下运行。**不要改成 net9**。
- PlatformTarget x64，WPF + WinForms（WinForms 仅用于 FolderBrowserDialog）。
- HALCON 引用：`E:\halcon\MVTec\HALCON-24.11-Progress-Steady\bin\dotnet35\halcondotnet.dll`；halcon.dll / hcanvas.dll 复制到输出目录。
- 构建命令：`dotnet build VisionPositionTool/VisionPositionTool.csproj`。注意：应用运行时 exe 被锁定会导致 MSB3021（文件拷贝失败），需先关闭运行中的应用。
- 图像读取用 WPF 流式解码 + `GenImageInterleaved`（`ReadImageRobust`），绕开 HALCON 文件解码器。

## 架构

- `Core/IMatchTool`（RunMatch(image, params, searchRegion)）接口，实现：`ShapeMatchTool`（模板匹配）、`NccMatchTool`（相关性匹配）、`GrayMatchTool`（灰度模板，create_template/best_match）、`GeometricFitTool`（几何拟合，edges_sub_pix + fit_*_contour_xld）。
- 视图瘦（code-behind）+ 纯逻辑服务类，不重 MVVM；`MainViewModel` 仅做属性绑定。
- `Models/`：ImageItem（一图一结果）、MatchResult、MatchOutcome、TemplateParams、Point2。
- `Controls/HalconDisplay`：自绘 HALCON 图像控件，支持 ROI 框选/拖动（模板绿框、搜索蓝框），事件回传 code-behind。
- 命令行调试模式：`--demo <img> <r1> <c1> <r2> <c2> <log>`、`--open <img> <log>`、`--selftest <log>`。
- 无界面回归验证：`tool\.verify\Verify.csproj`（net48 控制台，引用主项目，`Verify.exe`）——用 testdata\text.bmp（WPF 流式解码绕开 HALCON BMP fail-fast）建模板 + 匹配 20 个：验证轮廓变换（native 算子对比逐点一致）、多匹配轮廓围绕各自匹配点、对比度参数影响轮廓点数。改匹配/参数逻辑后可跑：`dotnet build .verify && .verify\bin\Debug\net48\Verify.exe`。注意：.ps1 反射脚本必须存 **UTF-8 带 BOM**（PowerShell 5.1 按 GBK 读无 BOM 脚本，中文路径乱码）。

## 功能清单（随改动保持更新）

- 四种匹配方式（顶部 RadioButton 四态，`MainViewModel.MatchMode` = shape/ncc/gray/fit，转换器 `MatchModeConverter` 供 IsChecked 与面板 Visibility）：
  - 模板匹配（ShapeMatchTool）
  - 相关性匹配（NccMatchTool，NCC）
  - 灰度模板（GrayMatchTool，create_template/best_match，只平移、单最佳匹配、不支持旋转），x64。
  - 几何拟合（GeometricFitTool，edges_sub_pix + fit_circle/line/rectangle2/ellipse_contour_xld）：
    - **免模板**："模型"=拟合区域（绿框）+拟合类型，无模板句柄；框选/拖动绿框**立即对当前图拟合**（RebuildFitPreview），改拟合参数实时重拟合；执行全部匹配对所有图在同一区域拟合
    - 管线：EdgesSubPix（reduced-domain 单通道）→ SelectContoursXld("contour_length", min, **99999**（Max1 默认 200 会滤掉长轮廓！）, ...) → **逐条拟合+残差筛选**：选"残差 ≤ FitMaxError（默认 2.0px）的最长轮廓"（<5 点不拟合）→ fit（圆/椭圆 geotukey，直线/矩形 tukey；矩形 6 控参含 maxClosureDist、椭圆 7 控参含 maxClosureDist+vossTabSize）
    - **按残差选而非只取最长**（关键修复 2026-09-10）：只取最长会把目标的多边形外边界/折角闭合轮廓选中（正方体闭合边界直线拟合残差 160px+，拟合角度随位姿漂移 67°/-75° 等垃圾值）；残差过滤后真直线（<1px）被选中。真实图回归 [FIT-8]（testdata\cube\ 3 张，含上边缘被小产品遮挡成两段 + 全正方体对抗用例）
    - **断边拼接 FitGap 是 C# 自实现**（端点间隙 ≤ gap 且合并后拟合残差仍达标才合并），**不用 union_adjacent_contours_xld**（只按端点距离合并会把直线段与无关轮廓拼成弯折轮廓，拼错后整条被残差过滤拒掉 → 0 结果）
    - **单条候选拟合失败按跳过处理**（TryFit 内 catch → null）：直线轮廓拟合矩形会抛 #3266（矩形某边无点），曾导致异常中断整个 RunMatch 且 UI 显示旧结果（"切换类型没变化"）；椭圆退化（短轴≈0）也按失败处理。RebuildFitPreview 异常时清掉旧结果防残留。回归见 .verify [FIT-9]
    - 残差 = distance_pc 点到拟合几何最小距离 RMS（勿手写旋转坐标变换——曾产生 34px 级错误残差）；显示折线用 Gen*ContourXld 生成再采样
    - **本模式蓝搜索框隐藏且忽略**（RunMatch 始终 ReduceDomain 到 _modelRegion；切回其它模式蓝框恢复）；搜索框按钮点击给提示不进入绘制
    - 结果专用 DataGrid（类型/行/列/角度/尺寸1/尺寸2/误差，Visibility=fit；通用表 DataTrigger 隐藏）；MatchResult 加性字段 FitType/Size1/Size2/FitError
    - 保存 model_fit_region.hobj + model_fit_meta.txt（单行 FitType）；LoadModel 后 code-behind 回填 `_vm.Params.FitType`
    - 验证：.verify Program.cs 有 [FIT-1..7] 合成图断言（圆/矩形/椭圆/直线/空结果/保存加载往返/text.bmp smoke，`Check()` 计数 + 退出码）；--demo 有 [FIT] 自检块（合成圆图，并入 RESULT: PASS/FAIL）
- 左侧多图像列表：**多选导入**（＋ 导入图像）、**批量删除**（「删除选中」按钮或 Delete 键，Ctrl/Shift 多选；仅从列表移除并释放内存，不删除磁盘文件；删除模板来源图后绿框显示在所有图上）。
- 右侧上=图像显示（自绘控件）、右下=结果表格（随选中图像联动）。
- 模板 ROI（绿框）+ 搜索框（蓝框，可选，reduce_domain 限制搜索区域，坐标保持绝对）。
- **模板轮廓预览**（金色=模型边缘，**仅模板匹配显示**）：创建/加载模板后叠加显示；相关性匹配的模型"轮廓"只是 ROI 矩形、与绿框重合，故不显示；灰度模板无轮廓概念也不显示。调整影响模型创建的参数（NumLevels/StartAngle/AngleExtent/AngleStep/Optimization/Metric/Contrast/MinContrast）时自动重建模型并实时刷新，匹配参数不触发。
- **视图缩放保持**：HalconDisplay.ShowImage 仅图像尺寸变化时 FitImage，刷新同一张图保留用户缩放/平移（修复"放大后动 ROI 恢复初始大小"）。
- **灰度模板要点**：best_match 返回位置=模板中心（勿再加偏移）；遵守 reduce_domain 域；MaxError = 255*宽*高*(1-MinScore)；保存 model_tpl.tif + model_meta.txt（numLevels/grayValues）+ region，加载用元信息重建 create_template（句柄不可持久化）。
- **数字参数类型转换（Core/ParamConv）**：界面参数是 string（"auto"/"50"），HALCON 数字参数只收数字，数字字符串直传报 #1307/#1301/#1201（表现为改参数后创建失败、轮廓不变）。`ParamConv.Num`（浮点，contrast/min_contrast）/`ParamConv.Int`（整数，numlevels）/`ParamConv.Rad`（角度步长，**UI 度→弧度**，允许小数）统一转换，`auto` 等字符串原样传。
- **角度显示归一化**：搜索范围含 ±360° 时 HALCON 返回等价角（0° 报成 ±360°）。两个工具的 RunMatch 把结果角度归一化到 [-180,180) 再显示（轮廓变换仍用原始弧度值）。
- **HObject 所有权（#4056 坑）**：工具保存的 `_modelRegion` 必须 `copy_obj` 深拷贝自持——调用方（RebuildTemplatePreview/Display_RoiDrawn）在 finally Dispose 传入的 roi，浅引用会导致 TryGetModelRoi 抛 #4056（现象：第一次匹配正常、改参数后再匹配报错）。ClearModel 需 Dispose 自有的 _modelRegion。
- **匹配轮廓残留**：RefreshDisplay 无条件 ClearOverlay 后再绘制（DrawOutcome 只在结果非空时调用，若匹配失败不清屏会残留上一次的红色轮廓 → "两次匹配轮廓重叠"假象）。
- **空结果保护**：find_shape_model/find_ncc_model 无匹配返回空元组，访问 .DArr 抛 HTupleAccessException。RunMatch 先查 row.Length==0 再访问。
- **NCC 高阈值陷阱**：NCC 金字塔顶层分数远低于全分辨率——MinScore≥0.8 时真目标（全分辨率 score=1.0）在顶层就被过滤，NumLevels=auto 下 0.8 匹配 0 个。NCC MinScore 建议 ≤0.7，或 NumLevels 设 1~3 支持高阈值。
- **NCC Metric 白名单（#1305）**：create_ncc_model 的 Metric 只收 use_polarity/ignore_global_polarity，形状匹配选项报 #1305（HALCON 控制参数 5=Metric，编号不含对象参数）。防护：NccMatchTool 白名单回退 use_polarity + XAML 灰度面板用 NccMetricOptions（2 项）+ 切模式时 _vm_PropertyChanged 重置非法 Metric。
- **NumLevels 不影响匹配结果**：金字塔层数只是搜索加速策略，get_shape_model_params 读回确认参数传入（上限 8 层），匹配分数/位置不变是正常的。
- 「执行全部匹配」一键匹配所有图像，每图独立结果；匹配失败标红。**后台线程执行**（Task.Run + Dispatcher 写回结果），期间 `_vm.IsBusy` 禁用工具栏/参数面板（防止切模式/改参数与后台匹配竞争模型句柄）；状态栏实时进度"正在匹配 i/N"。
- 保存/加载模板（.shm/.ncm + .hobj）。

## 交互偏好

用户是中文上位机开发者，交互直观：框选 ROI、点图看结果、多选批处理。UI 文案用中文。右侧匹配参数面板每个参数下方有灰色小字说明。

## 工作约定

- **每次添加/修改功能后，更新本文件的「功能清单」部分**。
