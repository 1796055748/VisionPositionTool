using HalconDotNet;
using VisionPositionTool.Models;

namespace VisionPositionTool.Core
{
    /// <summary>
    /// 匹配工具接口：模板匹配 / 灰度匹配均实现该接口，便于后续扩展更多定位方式。
    /// 不依赖任何 HALCON 窗口控件，显示由 WPF 层负责。
    /// </summary>
    public interface IMatchTool
    {
        /// <summary>显示名称</summary>
        string DisplayName { get; }

        /// <summary>是否已创建/加载模板</summary>
        bool HasModel { get; }

        /// <summary>从指定 ROI 区域创建模板</summary>
        bool CreateModelFromRegion(HObject image, HObject roi, TemplateParams p, out string message);

        /// <summary>
        /// 在当前图像上查找模板，返回结果与叠加图形。
        /// searchRegion 为可选的搜索区域（搜索框），为空则在全图搜索。
        /// </summary>
        MatchOutcome RunMatch(HObject image, TemplateParams p, HObject searchRegion);

        /// <summary>保存模板到指定文件夹</summary>
        bool SaveModel(string folder, out string message);

        /// <summary>从指定文件夹加载模板</summary>
        bool LoadModel(string folder, out string message);

        /// <summary>获取模型 ROI 的外接矩形（图像坐标，用于加载后显示）</summary>
        bool TryGetModelRoi(out double r1, out double c1, out double r2, out double c2);

        /// <summary>
        /// 获取模板基础轮廓（分段，每段为独立子轮廓；模型坐标系，参考点在 (0,0)），
        /// 用于模板轮廓预览与匹配结果叠加。显示时每段需平移到模型参考点（ROI 中心）。
        /// 无模板/无轮廓返回 false。
        /// </summary>
        bool TryGetBaseContour(out System.Collections.Generic.List<double[]> rowsParts,
                               out System.Collections.Generic.List<double[]> colsParts);

        /// <summary>释放模板资源</summary>
        void ClearModel();
    }
}
