using System.Collections.ObjectModel;
using System.ComponentModel;
using VisionPositionTool.Models;

namespace VisionPositionTool
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private string _matchMode = "shape";   // shape=模板匹配 ncc=相关性匹配 gray=灰度模板 fit=几何拟合
        private string _status = "就绪";
        private ImageItem _selectedImage;
        private int _templateCount;
        private int _searchRoiCount;
        private bool _isBusy;                  // 匹配进行中（禁用工具栏，防止中途切模式/清 ROI 导致模型竞争）

        public TemplateParams Params { get; } = new TemplateParams();

        /// <summary>已导入图像列表</summary>
        public ObservableCollection<ImageItem> Images { get; } = new ObservableCollection<ImageItem>();

        /// <summary>当前选中图像</summary>
        public ImageItem SelectedImage
        {
            get => _selectedImage;
            set { _selectedImage = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedImage))); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>当前匹配方式：shape=模板匹配 ncc=相关性匹配 gray=灰度模板 fit=几何拟合</summary>
        public string MatchMode
        {
            get => _matchMode;
            set { _matchMode = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MatchMode))); }
        }

        /// <summary>匹配进行中（后台执行，期间禁用工具栏操作）</summary>
        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBusy))); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status))); }
        }

        /// <summary>模板数量（用于状态提示）</summary>
        public int TemplateCount
        {
            get => _templateCount;
            set { _templateCount = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TemplateCount))); }
        }

        /// <summary>搜索框数量</summary>
        public int SearchRoiCount
        {
            get => _searchRoiCount;
            set { _searchRoiCount = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SearchRoiCount))); }
        }
    }
}
