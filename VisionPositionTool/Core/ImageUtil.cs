using HalconDotNet;

namespace VisionPositionTool.Core
{
    /// <summary>图像公共辅助</summary>
    public static class ImageUtil
    {
        /// <summary>
        /// 转为单通道灰度图（create_ncc_model / find_ncc_model / create_template 只接受单通道图像）。
        /// 已单通道时原样返回（不新建对象）。
        /// </summary>
        public static HObject ToSingleChannel(HObject image)
        {
            HOperatorSet.CountChannels(image, out HTuple ch);
            int n = ch.Length > 0 ? (int)ch.D : 1;
            if (n <= 1) return image;
            if (n == 3)
            {
                HOperatorSet.Rgb1ToGray(image, out HObject gray);
                return gray;
            }
            // 其他（如 4 通道 RGBA）：取前 3 通道合成 RGB 再转灰度
            HOperatorSet.AccessChannel(image, out HObject c1, 1);
            HOperatorSet.AccessChannel(image, out HObject c2, 2);
            HOperatorSet.AccessChannel(image, out HObject c3, 3);
            HOperatorSet.Compose3(c1, c2, c3, out HObject rgb);
            c1.Dispose(); c2.Dispose(); c3.Dispose();
            try
            {
                HOperatorSet.Rgb1ToGray(rgb, out HObject gray);
                return gray;
            }
            finally { rgb.Dispose(); }
        }
    }
}
