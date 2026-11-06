using System.Collections.ObjectModel;
using System.ComponentModel;
using HalconDotNet;

namespace VisionPositionTool.Models
{
    /// <summary>一张已导入图像及其匹配结果</summary>
    public class ImageItem : INotifyPropertyChanged
    {
        private string _resultSummary = "未匹配";

        /// <summary>显示名（文件名）</summary>
        public string Name { get; set; }

        /// <summary>文件路径</summary>
        public string FilePath { get; set; }

        /// <summary>HALCON 图像</summary>
        public HImage Image { get; set; }

        /// <summary>该图像的匹配结果</summary>
        public ObservableCollection<MatchResult> Results { get; } = new ObservableCollection<MatchResult>();

        /// <summary>该图像的匹配叠加图形（轮廓/十字），用于切换显示时重绘</summary>
        public MatchOutcome Outcome { get; set; }

        /// <summary>结果摘要（如"找到 3 个" / "匹配失败"）</summary>
        public string ResultSummary
        {
            get => _resultSummary;
            set { _resultSummary = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ResultSummary))); }
        }

        private bool _matchFailed;

        /// <summary>该图匹配是否失败（用于搜索框变红等提示）</summary>
        public bool MatchFailed
        {
            get => _matchFailed;
            set { _matchFailed = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MatchFailed))); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
