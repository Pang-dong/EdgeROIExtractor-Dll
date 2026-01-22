using System;
using System.Collections.Generic;
using System.Drawing;

namespace EdgeROIExtractor
{
    /// <summary>
    /// ROI提取结果
    /// </summary>
    [Serializable]
    public class ROIResult
    {
        /// <summary>
        /// ROI图像数据（灰度，8位）
        /// </summary>
        public byte[] ImageData { get; set; }

        /// <summary>
        /// ROI宽度
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// ROI高度
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// 原始四边形的中心点
        /// </summary>
        public PointF Center { get; set; }

        /// <summary>
        /// 选中的边索引（0-3）
        /// </summary>
        public int EdgeIndex { get; set; }

        /// <summary>
        /// 原始四边形顶点（4个点）
        /// </summary>
        public PointF[] Quadrilateral { get; set; }

        /// <summary>
        /// 框选区域顶点（4个点）
        /// </summary>
        public PointF[] SelectionArea { get; set; }

        /// <summary>
        /// ROI的左上角在原始图像中的坐标
        /// </summary>
        public PointF RoiLocation { get; set; }

        /// <summary>
        /// 原始四边形面积
        /// </summary>
        public double Area { get; set; }

        /// <summary>
        /// 可视化图像保存路径（如果保存了的话）
        /// </summary>
        public string VisualizationPath { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public ROIResult()
        {
            Quadrilateral = new PointF[4];
            SelectionArea = new PointF[4];
        }

        /// <summary>
        /// 转换为字符串表示
        /// </summary>
        public override string ToString()
        {
            return $"ROI: {Width}x{Height}, Center: ({Center.X:F1},{Center.Y:F1}), Edge: {EdgeIndex}";
        }
    }

    /// <summary>
    /// 多个ROI结果
    /// </summary>
    [Serializable]
    public class ROIResults
    {
        /// <summary>
        /// 所有ROI结果列表
        /// </summary>
        public List<ROIResult> Results { get; set; } = new List<ROIResult>();

        /// <summary>
        /// 原始图像尺寸
        /// </summary>
        public System.Drawing.Size OriginalImageSize { get; set; }

        /// <summary>
        /// 处理时间（毫秒）
        /// </summary>
        public long ProcessingTimeMs { get; set; }

        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 错误消息
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 检测到的四边形数量
        /// </summary>
        public int QuadrilateralCount { get; set; }

        /// <summary>
        /// 可视化图像保存路径（如果保存了的话）
        /// </summary>
        public string VisualizationPath { get; set; }

        /// <summary>
        /// 可视化图像数据（如果创建了的话）
        /// </summary>
        public byte[] VisualizationImageData { get; set; }

        /// <summary>
        /// 可视化图像宽度
        /// </summary>
        public int VisualizationWidth { get; set; }

        /// <summary>
        /// 可视化图像高度
        /// </summary>
        public int VisualizationHeight { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public ROIResults()
        {
            Success = true;
        }

        /// <summary>
        /// 转换为字符串表示
        /// </summary>
        public override string ToString()
        {
            return $"{Results.Count} ROI(s) found, Time: {ProcessingTimeMs}ms";
        }
    }

    /// <summary>
    /// 提取参数
    /// </summary>
    [Serializable]
    public class ExtractionParameters
    {
        /// <summary>
        /// 框选区域宽度（垂直于边的方向）
        /// </summary>
        public int ExtensionWidth { get; set; } = 30;

        /// <summary>
        /// 框选区域长度（沿边的方向）
        /// </summary>
        public int ExtensionLength { get; set; } = 80;

        /// <summary>
        /// 框选方向（true: 向内, false: 向外）
        /// </summary>
        public bool ExtendInwards { get; set; } = true;

        /// <summary>
        /// 选择要框选哪条边 (0-3)
        /// 0: 第一条边（p1→p2）
        /// 1: 第二条边（p2→p3）
        /// 2: 第三条边（p3→p4）
        /// 3: 第四条边（p4→p1）
        /// </summary>
        public int SelectedEdgeIndex { get; set; } = 0;

        /// <summary>
        /// 最小面积阈值
        /// </summary>
        public double MinArea { get; set; } = 800;

        /// <summary>
        /// 最大面积阈值
        /// </summary>
        public double MaxArea { get; set; } = 20000;

        /// <summary>
        /// 自适应阈值块大小（必须是奇数）
        /// </summary>
        public int AdaptiveBlockSize { get; set; } = 31;

        /// <summary>
        /// 自适应阈值常数
        /// </summary>
        public int AdaptiveConstant { get; set; } = 7;

        /// <summary>
        /// 轮廓近似精度（0.0-1.0）
        /// </summary>
        public double ApproximationAccuracy { get; set; } = 0.03;

        /// <summary>
        /// 是否启用形态学滤波
        /// </summary>
        public bool EnableMorphology { get; set; } = true;

        /// <summary>
        /// 形态学开运算核大小
        /// </summary>
        public int OpenKernelSize { get; set; } = 3;

        /// <summary>
        /// 形态学闭运算核大小
        /// </summary>
        public int CloseKernelSize { get; set; } = 5;

        /// <summary>
        /// 是否保存可视化图像
        /// </summary>
        public bool SaveVisualization { get; set; } = false;

        /// <summary>
        /// 可视化图像保存路径
        /// 如果为空，则保存到当前运行目录
        /// </summary>
        public string VisualizationPath { get; set; } = "";

        /// <summary>
        /// 可视化图像文件名（不包含路径）
        /// </summary>
        public string VisualizationFileName { get; set; } = "edge_roi_result.png";

        /// <summary>
        /// 是否在可视化图像中显示参数信息
        /// </summary>
        public bool ShowParametersOnImage { get; set; } = true;

        /// <summary>
        /// 可视化图像质量（1-100，PNG格式时有效）
        /// </summary>
        public int ImageQuality { get; set; } = 95;

        /// <summary>
        /// 是否返回可视化图像数据
        /// </summary>
        public bool ReturnVisualizationData { get; set; } = false;

        /// <summary>
        /// 默认构造函数
        /// </summary>
        public ExtractionParameters() { }

        /// <summary>
        /// 带参数的构造函数
        /// </summary>
        public ExtractionParameters(int width, int length, bool inward, int edgeIndex)
        {
            ExtensionWidth = width;
            ExtensionLength = length;
            ExtendInwards = inward;
            SelectedEdgeIndex = edgeIndex;
        }

        /// <summary>
        /// 验证参数
        /// </summary>
        public bool Validate(out string error)
        {
            error = null;

            if (ExtensionWidth <= 0)
                error = "ExtensionWidth必须大于0";
            else if (ExtensionLength <= 0)
                error = "ExtensionLength必须大于0";
            else if (SelectedEdgeIndex < 0 || SelectedEdgeIndex > 3)
                error = "SelectedEdgeIndex必须在0-3之间";
            else if (AdaptiveBlockSize % 2 == 0)
                error = "AdaptiveBlockSize必须是奇数";
            else if (MinArea <= 0)
                error = "MinArea必须大于0";
            else if (MaxArea <= MinArea)
                error = "MaxArea必须大于MinArea";
            else if (ApproximationAccuracy <= 0 || ApproximationAccuracy > 1)
                error = "ApproximationAccuracy必须在0-1之间";
            else if (ImageQuality < 1 || ImageQuality > 100)
                error = "ImageQuality必须在1-100之间";

            return error == null;
        }

        /// <summary>
        /// 创建默认参数
        /// </summary>
        public static ExtractionParameters Default()
        {
            return new ExtractionParameters();
        }
    }

    /// <summary>
    /// 用于C++ DLL调用的数据结构
    /// </summary>
    [Serializable]
    public class ROIDataForCpp
    {
        /// <summary>
        /// 图像数据缓冲区
        /// </summary>
        public byte[] Buffer { get; set; }

        /// <summary>
        /// 图像宽度
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// 图像高度
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// 中心点X坐标
        /// </summary>
        public float CenterX { get; set; }

        /// <summary>
        /// 中心点Y坐标
        /// </summary>
        public float CenterY { get; set; }

        /// <summary>
        /// 选中的边索引
        /// </summary>
        public int EdgeIndex { get; set; }

        /// <summary>
        /// 可视化图像路径
        /// </summary>
        public string VisualizationPath { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public ROIDataForCpp()
        {
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        public ROIDataForCpp(ROIResult roi)
        {
            if (roi != null)
            {
                Buffer = roi.ImageData;
                Width = roi.Width;
                Height = roi.Height;
                CenterX = roi.Center.X;
                CenterY = roi.Center.Y;
                EdgeIndex = roi.EdgeIndex;
                VisualizationPath = roi.VisualizationPath;
            }
        }

        /// <summary>
        /// 转换为字符串表示
        /// </summary>
        public override string ToString()
        {
            return $"CppData: {Width}x{Height}, Buffer size: {Buffer?.Length ?? 0}";
        }
    }
}