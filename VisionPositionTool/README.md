# 视觉定位工具（VisionPositionTool）

基于 **MVTec HALCON 24.11** 的 WPF 视觉定位工具，目前包含四种匹配方式：

- **模板匹配**（形状匹配）：`create_shape_model` / `find_shape_model`，通过边缘/轮廓特征定位，工业视觉中最常用。
- **相关性匹配**（NCC 归一化互相关）：`create_ncc_model` / `find_ncc_model`，通过灰度相关性定位，适合纹理丰富的对象，支持旋转。
- **灰度模板**（基于灰度值）：`create_template` / `best_match`，经典灰度滑窗匹配，只支持平移定位（不支持旋转/缩放），仅返回一个最佳匹配，适合目标姿态固定的场景。
- **几何拟合**（亚像素边缘拟合）：`edges_sub_pix` + `fit_circle/line/rectangle2/ellipse_contour_xld`，在拟合区域内提取亚像素边缘并拟合为圆/直线/矩形/椭圆，输出几何参数（中心/角度/尺寸/残差）。免模板、亚像素精度（可达 0.1px 级），适合圆孔定位、直边找边、矩形件对位等精密对位场景；**不需要模板图**，框选 ROI 即拟合。

## 环境要求

| 项目 | 要求 |
|---|---|
| HALCON | 24.11，安装于 `E:\halcon\MVTec\HALCON-24.11-Progress-Steady` |
| .NET | **.NET Framework 4.8**（`net48`，WPF）——HALCON 24.11 的 .NET 包装与 .NET Core/9 在交互桌面不兼容（首次调用 fail-fast），必须在 .NET Framework 下运行 |
| 平台 | x64（HALCON 原生库仅支持 x64） |

> 项目通过 `csproj` 的 `HintPath` 引用 `E:\halcon\...\bin\dotnet35\halcondotnet.dll`，并在构建时把 `halcon.dll` / `hcanvas.dll` 复制到输出目录。若 HALCON 路径不同，请修改 `VisionPositionTool.csproj`。

## 构建与运行

```bash
# 构建（在 tool 目录）
dotnet build VisionPositionTool.sln -c Debug

# 运行（net48 输出为 exe，直接运行）
VisionPositionTool\bin\Debug\net48\VisionPositionTool.exe
# 或用 VS 打开 VisionPositionTool.sln，设置启动项目后 F5
```

## 使用流程

1. **导入图像**：左侧"导入图像"按钮可**多选**多张图片。
2. **删除图像（批量）**：Ctrl/Shift 多选多张后点"删除选中"按钮（或直接按 Delete 键）一次性移除；仅从列表移除并释放内存，**不会删除磁盘文件**。
3. **模板ROI**：在右侧显示区框选模板区域（绿色框，拖出矩形，右键取消），自动创建模板；**模板匹配**下框内同步显示**金色模型轮廓**（模型边缘特征）。调整模型参数（对比度/金字塔层数/角度范围等）离开输入框后模型自动重建、轮廓实时更新（相关性匹配的"轮廓"只是 ROI 矩形、与绿框重合，不显示预览；灰度模板无轮廓概念，也不显示）。**几何拟合**模式下绿框即拟合区域：框选/拖动后**立即对当前图拟合并显示结果**（红色拟合几何），调整拟合参数实时重拟合。
4. **搜索框（可选）**：框选搜索区域（蓝色框），匹配只在框内搜索；不画则全图搜索。**几何拟合不使用搜索框**（蓝框隐藏且忽略，拟合始终在绿框区域内进行）。
5. **执行全部匹配**：一键对左侧**所有图像**匹配，每张图结果独立保存。匹配在**后台线程**执行（界面不卡死，状态栏显示进度"正在匹配 i/N"），期间工具栏/参数面板自动禁用防止误操作；灰度模板在大图上较慢（best_match 滑窗特性），耐心等待完成。几何拟合模式下对所有图像**在同一拟合区域内**拟合。
6. **查看结果**：右下表格显示当前选中图像的结果；**点击左侧任意图像**，右侧自动切换显示该图及其匹配叠加与结果。几何拟合的结果列不同（类型/行/列/角度/尺寸1/尺寸2/误差）。
7. **保存 / 加载模板**：保存为 `model.shm`（模板匹配）或 `model.ncm`（相关性匹配）或 `model_tpl.tif + model_meta.txt`（灰度模板）或 `model_fit_region.hobj + model_fit_meta.txt`（几何拟合）+ `model_region.hobj`。
8. **切换匹配方式**：顶部工具栏切换（模板匹配/相关性匹配/灰度模板/几何拟合），切换后模板与结果自动清除。

右侧"匹配参数"面板随匹配类型切换，参数齐全：

**模板匹配**：起始角度、角度范围、最小分数、匹配数量、最大重叠、金字塔层数、角度步长、亚像素精度、优化方式、度量方式、对比度、最小对比度、贪心系数

**相关性匹配**：起始角度、角度范围、最小分数、匹配数量、最大重叠、金字塔层数、角度步长、亚像素精度、度量方式

**灰度模板**：最小分数、金字塔层数、灰度值模式（original/normalized）、亚像素精度（仅一个最佳匹配，不支持旋转/缩放）

**几何拟合**：拟合类型（圆/直线/矩形/椭圆）、边缘算子（canny/deriche2/lanser2/mshen/sobel_fast）、平滑系数（Alpha）、低阈值、高阈值、最小轮廓长度、断边间隙（被遮挡的边/圆弧断口拼接，0=不合并）、最大拟合误差（只接受残差≤此值的轮廓，0=不限制）。**注意**：低/高阈值只填数字（无 auto）；ROI 建议比目标略大一圈；直线拟合建议 ROI 只含目标边线段（拟合的是边缘线本身，中心=拟合线段中点）

> 参数取值与 HALCON 算子一致：金字塔层数/角度步长/优化/度量/对比度/最小对比度 填 `auto` 或具体值；亚像素精度填 `least_squares`/`interpolation`/`none`（形状）或 `true`/`false`/`interpolation`（相关性）。
>
> 每个参数下方有**灰色小字说明**其含义与调整建议，方便理解参数对模型/匹配的影响。

## 界面布局

```
┌──────────────┬──────────────────────────────┐
│ 工具栏：模板ROI 搜索框 清除ROI 执行全部匹配 │
│   保存模板 加载模板 模板/相关性/灰度模板    │
├──────────────┼──────────────────────────────┤
│ 左：图像列表  │ 右上：图像显示 + ROI绘制      │
│   - 导入图像 │                              │
│   - 删除选中 │                              │
│   - 图1 找到N│                              │
│   - 图2 找到N│ 右下：匹配结果表格             │
│   - ...      │   （随选中图像联动）           │
└──────────────┴──────────────────────────────┘
```

## 无界面验证（demo 模式）

```bash
dotnet VisionPositionTool.dll --demo <图像> <r1> <c1> <r2> <c2> <日志文件>
```

加载图像 → 用给定 ROI 建模板 → 分别跑模板匹配与相关性匹配 → **几何拟合自检**（合成圆图 200,200 r=100，拟合中心/半径容差 ±2）→ 保存/加载往返验证 → 写日志退出。

## 代码结构

```
VisionPositionTool/
├─ MainWindow.xaml(.cs)      # 主界面：工具栏 + 显示区 + 参数/结果面板
├─ MainViewModel.cs          # 视图模型（参数、结果集合、模式）
├─ Models/
│  ├─ TemplateParams.cs      # 匹配参数（界面绑定，角度=度，内部换算弧度）
│  ├─ MatchResult.cs         # 单条匹配结果
│  ├─ MatchOutcome.cs        # 匹配输出（结果 + 叠加图形）
│  └─ Point2.cs              # 图像坐标点
├─ Controls/
│  └─ HalconDisplay.xaml(.cs) # 自绘图像显示控件（缩放/平移/框选/叠加）
├─ Core/
│  ├─ IMatchTool.cs          # 匹配工具接口（便于扩展更多定位方式）
│  ├─ ShapeMatchTool.cs      # 模板匹配实现
│  ├─ NccMatchTool.cs        # 相关性匹配实现
│  ├─ GrayMatchTool.cs       # 灰度模板实现（create_template/best_match）
│  ├─ GeometricFitTool.cs    # 几何拟合实现（edges_sub_pix + fit_*_contour_xld）
│  ├─ ParamConv.cs           # 界面参数字符串 → HALCON 参数类型转换
│  └─ ImageUtil.cs           # 单通道灰度转换等公共辅助
├─ Converters/               # 界面转换器
└─ testdata/scene.png        # 示例测试图
```

## 关键技术点

- **显示控件（自绘）**：本机 HALCON 24.11 的 WinForms 窗口控件（`HSmartWindowControl` / `HWindowControl`）在创建原生窗口时会阻塞 WPF UI 线程，导致主窗口永远无法显示。因此本项目**不使用 HALCON 的窗口控件**，而是自绘 `Controls/HalconDisplay`：把 `HImage` 像素经 `WriteableBitmap` 渲染到 WPF `Image`，用 WPF 原生实现缩放（滚轮）、平移（左键拖拽）、框选 ROI（绘制模式）与结果叠加，完全不依赖 HALCON GUI 层。
- **角度单位**：`create_shape_model` / `find_shape_model` / `create_ncc_model` / `find_ncc_model` 的**角度参数单位是弧度**，界面输入为度，换算在 `TemplateParams.StartAngleRad / AngleExtentRad` 完成。参考框架直接传度数其实是 bug。
- **结果访问**：匹配结果元组（row/col/angle/score）是 double 类型，用 `.D` / `.DArr` 访问；用 `.I` 会抛 `HTupleAccessException`。
- **单通道要求**：`create_ncc_model` / `find_ncc_model` / `create_template` 只接受单通道图像。`ImageUtil.ToSingleChannel()` 负责把彩色（3/4 通道）图像转为灰度。
- **ROI 需留白**：`create_shape_model` 若 ROI 与目标轮廓完全重合（边缘落在域边界），模型可能为空；建议 ROI 比目标略大一圈。
- **模板轮廓预览**：创建模板后用 `get_shape_model_contours` 取模型基础轮廓，平移到模型参考点（ROI 中心）叠加显示为金色。**仅模板匹配显示**——相关性匹配的模型"轮廓"（`get_ncc_model_region`）就是 ROI 矩形本身、与绿框重合、无信息量，故不显示；灰度模板无边缘轮廓概念，也不显示。调整影响模型创建的参数时自动按当前 ROI 重建模型，直观看到参数对模型的影响；匹配参数（最小分数等）不触发重建。
- **灰度模板（best_match）要点**：`best_match` 返回的位置就是**模板中心**（不是左上角）；匹配结果矩形框以中心展开。`best_match` 遵守 `reduce_domain` 的域（搜索框限制有效）。MaxError 由 MinScore 换算（`255*宽*高*(1-MinScore)`）；刻度盘等纹理重复的图，涂掉目标后仍可能在别处匹配到低误差相似纹理（正常现象）。
- **视图缩放保持**：`HalconDisplay.ShowImage` 只在图像尺寸变化时 `FitImage`，刷新同一张图（拖动 ROI 后重建模板）时保留用户当前的缩放/平移——修复"放大后动 ROI 又恢复初始大小"。
- **数字参数类型**：界面参数是字符串（`"auto"` / `"50"`），但 HALCON 的数字参数**只接受数字、不接受数字字符串**——直接传 `"50"` 会报 `#1307`（contrast）/`#1301`（numlevels）等错误导致创建失败（表现为参数改了但轮廓不变、旧模板保留）。`Core/ParamConv` 统一转换：可解析为数字则转数字（整数参数如金字塔层数用 `Int` 转 `int`，浮点参数如对比度用 `Num` 转 `double`，**角度步长用 `Rad` 转弧度**），否则原样传字符串（`"auto"` 等）。
- **角度显示归一化**：搜索范围含 ±360° 时，HALCON 会把 0° 报成 ±360° 等等价角（结果表格出现 -359.999 之类的"怪角度"）。匹配结果角度统一归一化到 [-180,180) 再显示。
- **HObject 所有权（#4056 坑）**：`create_shape_model` 后工具内部保存的 ROI（`_modelRegion`）必须**深拷贝自持**（`copy_obj`）——调用方（改参数重建 `RebuildTemplatePreview` / 框选 `Display_RoiDrawn`）会在 `finally` 里 `Dispose` 传入的 ROI，若工具只保存浅引用，释放后 `TryGetModelRoi`（`smallest_rectangle1`）会抛 `#4056 object-ID is NULL`，表现为"第一次匹配正常，改参数后再匹配报错"。
- **匹配轮廓残留（重叠假象）**：`RefreshDisplay` 必须**无条件清叠加层**再绘制——`ClearOverlay` 原先只在 `DrawOutcome`（需结果非空）内部调用，匹配失败（Outcome 为 null）时上一次的红色轮廓残留，看起来像"两次匹配的轮廓重叠"。
- **空结果保护**：`find_shape_model` / `find_ncc_model` 无匹配时返回**空元组**，直接访问 `.DArr` 抛 `HTupleAccessException`（表现为"改最小分数后程序报错"）。两个工具的 `RunMatch` 先查 `row.Length == 0` 返回空结果。
- **NCC 高阈值匹配不到（特性）**：相关性匹配（NCC）用金字塔加速，**顶层下采样后分数大幅下降**——`MinScore ≥ 0.8` 时真目标在金字塔顶层就被过滤（即使全分辨率分数 1.0），`NumLevels=auto` 下实测 0.8 匹配 0 个。NCC 的 MinScore 建议 ≤ 0.7；要配高阈值就把金字塔层数 `NumLevels` 设小（1~3），实测 `NumLevels=2` 时 0.8 可匹配到真目标（score=1.0）。
- **NCC 的 Metric 限制（#1305 坑）**：`create_ncc_model` 的 Metric **只接受** `use_polarity` / `ignore_global_polarity`——形状匹配的选项（`ignore_color_polarity` / `ignore_local_polarity` / `ignore_part_polarity`）传进来报 `#1305`。两模式共用 `Params.Metric` 时切模式会带上非法值；已三层防护：相关性面板 ComboBox 只列 2 项、切模式时非法值重置为 `use_polarity`、`NccMatchTool` 白名单兜底回退。
- **金字塔层数不影响匹配结果（特性）**：`NumLevels`（形状/NCC）只决定搜索用几层金字塔（加速策略），**不影响最终匹配分数/位置/数量**——`get_shape_model_params` 读回确认参数已正确传入（1..10 → 模型 1..8，HALCON 上限 8 层），但结果完全一致是正常现象。真正改变结果的是对比度/最小分数/角度参数。
- **角度步长单位（坑）**：`angle_step` 是**弧度**，UI 按度输入。此前直接传数字会被当作弧度（`1` = 57.3° 步长 → 角度量化误差大 → 轮廓与图像不贴合；小数 `0.5` 被截断成 0 报错）。`ParamConv.Rad` 统一把度数转弧度，`"auto"` 原样传。
- **几何拟合管线（GeometricFitTool）**：`edges_sub_pix`（reduced-domain 单通道）→ `select_contours_xld` 按长度过滤 → **逐条拟合 + 残差筛选**：选"残差 ≤ 最大拟合误差（FitMaxError，默认 2.0px）的最长轮廓"（<5 点不拟合）→ 按类型 `fit_*_contour_xld` 鲁棒拟合（geotukey/tukey）。拟合始终在绿框区域内进行（忽略搜索框）。
  - **为什么按残差选而不是只取最长**：ROI 里最长的往往是目标的多边形外边界/折角闭合轮廓（如正方体闭合边界，直线拟合残差 160px+），直接拟合会得到随目标位置角度漂移的错误角度（实测 67°/-75° 等垃圾值）；残差过滤后自动只留真正接近所选形状的轮廓（真直线残差 <1px）。真实图回归见 .verify [FIT-8]（testdata\cube\，正方体上边缘被小产品遮挡成两段的场景）。
  - **断边拼接（FitGap）**：C# 自实现——合并端点间隙 ≤ gap、且合并后拟合残差仍达标的轮廓对（被遮挡的直线/圆弧断口）。**不用 `union_adjacent_contours_xld`**：它只按端点距离合并、不看几何形状，会把直线段与邻近无关轮廓拼成弯折轮廓，拼错后整条被残差过滤拒掉（表现为 0 结果）。
  - **拟合类型必须匹配目标真实几何**：单条候选拟合失败按跳过处理（如直线轮廓拟合矩形：`fit_rectangle2_contour_xld` 抛 #3266"矩形某边无点"、椭圆会退化成短轴≈0 的线，均已处理为不合格），不匹配的轮廓被拒后结果为空。若切换拟合类型后看不到结果，说明 ROI 内没有该类型的真实几何（不是没生效）。
  - **SelectContoursXld Max1 默认 200 陷阱**：长度 >200 的轮廓（如半径 100 的圆周长≈628）会被默认 Max1 过滤成 0 结果，必须显式传 `99999`。
  - **fit 算子控制参数个数**：`fit_rectangle2_contour_xld` 6 个（含 maxClosureDist）、`fit_ellipse_contour_xld` 7 个（含 maxClosureDist + vossTabSize），与 `fit_line_contour_xld`（5 个）/`fit_circle_contour_xld`（6 个）不同，少传报参数个数错误。
  - **残差（误差列）= 轮廓点到拟合几何的最小距离 RMS**，用 `distance_pc` 计算（不手写旋转坐标变换——手写公式曾因符号约定产生 34px 级错误残差）。单位像素，理想台阶边缘实测 ~0.1px。
  - **显示折线**用 `gen_circle/rectangle2/ellipse_contour_xld` 生成 XLD 再 `get_contour_xld` 采样（圆/椭圆整圈，分辨率 ≈2° 步长控制点数）。
  - **角度**：矩形/椭圆的 Phi 存在 ±90° 等价表示（与尺寸对调），显示归一化到 [-180,180)；单一圆无角度（=0）；直线角度为方向角（0 与 ±180 等价）。
  - **ROI 留白**：`edges_sub_pix` 文档明示 reduced-domain 输入在域边界附近结果可能与全图不同，拟合 ROI 建议比目标略大一圈。
  - **"模型"=拟合区域+类型**：无模板句柄，保存 `model_fit_region.hobj + model_fit_meta.txt`（单行拟合类型），加载后回填界面 FitType。
