using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using OpenCvSharp;
using Point = OpenCvSharp.Point;
using Size = OpenCvSharp.Size;

namespace EdgeROIExtractor
{
    /// <summary>
    /// 边缘ROI提取器
    /// </summary>
    public class EdgeROIExtractorEngine : IDisposable
    {
        private bool _disposed = false;

        /// <summary>
        /// 版本信息
        /// </summary>
        public static string Version => "1.1.0-NoWarp";

        /// <summary>
        /// 从灰度图像中提取ROI
        /// </summary>
        public ROIResults ExtractROIs(byte[] grayImageData, int width, int height,
            ExtractionParameters parameters = null)
        {
            var results = new ROIResults
            {
                OriginalImageSize = new System.Drawing.Size(width, height)
            };

            if (grayImageData == null || grayImageData.Length == 0)
            {
                results.Success = false;
                results.ErrorMessage = "图像数据为空";
                return results;
            }

            if (width <= 0 || height <= 0)
            {
                results.Success = false;
                results.ErrorMessage = "图像尺寸无效";
                return results;
            }

            if (grayImageData.Length != width * height)
            {
                results.Success = false;
                results.ErrorMessage = "图像数据尺寸与指定的宽高不匹配";
                return results;
            }

            if (parameters == null)
                parameters = ExtractionParameters.Default();

            string validationError;
            if (!parameters.Validate(out validationError))
            {
                results.Success = false;
                results.ErrorMessage = validationError;
                return results;
            }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                unsafe
                {
                    fixed (byte* p = grayImageData)
                    {
                        IntPtr dataPtr = new IntPtr(p);
                        using (var srcGray = Mat.FromPixelData(height, width, MatType.CV_8UC1, dataPtr))
                        {
                            // 处理图像并提取ROI
                            ProcessImage(srcGray, parameters, results);

                            // 如果需要保存可视化图像或返回可视化数据
                            if ((parameters.SaveVisualization || parameters.ReturnVisualizationData)
                                && results.Success && results.Results.Count > 0)
                            {
                                byte[] visualizationData = CreateVisualizationImage(
                                    srcGray, results, parameters,
                                    out int vizWidth, out int vizHeight);

                                results.VisualizationWidth = vizWidth;
                                results.VisualizationHeight = vizHeight;

                                if (parameters.ReturnVisualizationData)
                                {
                                    results.VisualizationImageData = visualizationData;
                                }

                                if (parameters.SaveVisualization && visualizationData != null)
                                {
                                    string savePath = SaveVisualizationToFile(
                                        visualizationData, vizWidth, vizHeight, parameters);
                                    results.VisualizationPath = savePath;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                results.Success = false;
                results.ErrorMessage = $"处理图像时发生错误: {ex.Message}";
                return results;
            }

            stopwatch.Stop();
            results.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;

            return results;
        }

        /// <summary>
        /// 从彩色图像中提取ROI（修改：提取绿色通道以匹配手动工具）
        /// </summary>
        public ROIResults ExtractROIsFromColor(byte[] colorImageData, int width, int height,
            int channels, ExtractionParameters parameters = null)
        {
            if (colorImageData == null || colorImageData.Length == 0)
            {
                return new ROIResults { Success = false, ErrorMessage = "图像数据为空" };
            }

            if (channels != 3 && channels != 4)
            {
                return new ROIResults { Success = false, ErrorMessage = "仅支持3或4通道的彩色图像" };
            }

            try
            {
                unsafe
                {
                    fixed (byte* p = colorImageData)
                    {
                        IntPtr dataPtr = new IntPtr(p);

                        // 确定Mat类型
                        MatType matType = channels == 3 ? MatType.CV_8UC3 : MatType.CV_8UC4;

                        using (var colorMat = Mat.FromPixelData(height, width, matType, dataPtr))
                        using (var grayMat = new Mat())
                        {
                            // [关键修改]：提取绿色通道 (Green Channel, Index 1) 
                            // 之前的 BGR2GRAY 会混合三个通道，可能降低锐度。
                            // SFR测试通常推荐使用未经混合的 RAW 或 绿色通道。
                            Cv2.ExtractChannel(colorMat, grayMat, 1);

                            // 提取灰度图像数据
                            byte[] grayData = new byte[grayMat.Rows * grayMat.Cols];
                            Marshal.Copy(grayMat.Data, grayData, 0, grayData.Length);

                            return ExtractROIs(grayData, grayMat.Cols, grayMat.Rows, parameters);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return new ROIResults { Success = false, ErrorMessage = $"处理彩色图像时发生错误: {ex.Message}" };
            }
        }

        /// <summary>
        /// 从文件加载图像并提取ROI
        /// </summary>
        public ROIResults ExtractROIsFromFile(string filePath, ExtractionParameters parameters = null)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return new ROIResults { Success = false, ErrorMessage = $"文件不存在: {filePath}" };
            }

            try
            {
                // [关键修改]：加载为彩色以提取绿色通道
                using (var mat = Cv2.ImRead(filePath, ImreadModes.Color))
                {
                    if (mat.Empty())
                    {
                        return new ROIResults { Success = false, ErrorMessage = $"无法读取图像: {filePath}" };
                    }

                    int channels = mat.Channels();
                    byte[] imageData = new byte[mat.Rows * mat.Cols * channels];
                    Marshal.Copy(mat.Data, imageData, 0, imageData.Length);

                    return ExtractROIsFromColor(imageData, mat.Cols, mat.Rows, channels, parameters);
                }
            }
            catch (Exception ex)
            {
                return new ROIResults { Success = false, ErrorMessage = $"从文件加载时发生错误: {ex.Message}" };
            }
        }

        private void ProcessImage(Mat srcGray, ExtractionParameters parameters, ROIResults results)
        {
            // 1) 自适应二值化
            using (var binary = new Mat())
            {
                Cv2.AdaptiveThreshold(
                    srcGray, binary, 255,
                    AdaptiveThresholdTypes.GaussianC,
                    ThresholdTypes.BinaryInv,
                    parameters.AdaptiveBlockSize,
                    parameters.AdaptiveConstant
                );

                // 2) 形态学滤波
                if (parameters.EnableMorphology)
                {
                    using (var k3 = Cv2.GetStructuringElement(MorphShapes.Rect,
                           new Size(parameters.OpenKernelSize, parameters.OpenKernelSize)))
                    {
                        Cv2.MorphologyEx(binary, binary, MorphTypes.Open, k3, iterations: 1);
                    }

                    using (var k5 = Cv2.GetStructuringElement(MorphShapes.Rect,
                           new Size(parameters.CloseKernelSize, parameters.CloseKernelSize)))
                    {
                        Cv2.MorphologyEx(binary, binary, MorphTypes.Close, k5, iterations: 1);
                    }
                }

                // 3) 轮廓检测
                Point[][] contours;
                HierarchyIndex[] hierarchy;
                Cv2.FindContours(binary, out contours, out hierarchy,
                    RetrievalModes.External,
                    ContourApproximationModes.ApproxSimple);

                // 4) 处理每个轮廓
                int quadCount = 0;
                foreach (var contour in contours)
                {
                    double area = Cv2.ContourArea(contour);
                    if (area < parameters.MinArea || area > parameters.MaxArea)
                        continue;

                    double peri = Cv2.ArcLength(contour, true);
                    Point[] approx = Cv2.ApproxPolyDP(contour,
                        parameters.ApproximationAccuracy * peri, true);

                    if (!Cv2.IsContourConvex(approx))
                        continue;

                    if (approx.Length == 4)
                    {
                        ROIResult roiResult = ProcessQuadrilateral(
                            srcGray, approx, quadCount, parameters);

                        if (roiResult != null)
                        {
                            roiResult.Area = area;
                            results.Results.Add(roiResult);
                            quadCount++;
                        }
                    }
                }

                results.QuadrilateralCount = quadCount;
            }
        }

        private ROIResult ProcessQuadrilateral(Mat srcGray, Point[] quadrilateral,
            int index, ExtractionParameters parameters)
        {
            try
            {
                Point p1 = quadrilateral[0], p2 = quadrilateral[1],
                      p3 = quadrilateral[2], p4 = quadrilateral[3];

                Moments m = Cv2.Moments(quadrilateral);
                if (Math.Abs(m.M00) < 1e-6) return null;

                Point2f center = new Point2f(
                    (float)(m.M10 / m.M00),
                    (float)(m.M01 / m.M00)
                );

                Point[][] edges = new Point[4][];
                edges[0] = new Point[] { p1, p2 };
                edges[1] = new Point[] { p2, p3 };
                edges[2] = new Point[] { p3, p4 };
                edges[3] = new Point[] { p4, p1 };

                int selectedEdgeIndex = parameters.SelectedEdgeIndex;
                if (selectedEdgeIndex < 0) selectedEdgeIndex = 0;
                if (selectedEdgeIndex > 3) selectedEdgeIndex = 3;

                Point startPoint = edges[selectedEdgeIndex][0];
                Point endPoint = edges[selectedEdgeIndex][1];

                Point direction = new Point(endPoint.X - startPoint.X, endPoint.Y - startPoint.Y);
                double edgeLength = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);

                if (edgeLength < 10) return null;

                Point normal = new Point(-direction.Y, direction.X);
                double normalLength = Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y);
                if (normalLength < 1e-6) return null;

                double scaleFactor = 100.0;
                Point normalUnit = new Point(
                    (int)(normal.X / normalLength * scaleFactor),
                    (int)(normal.Y / normalLength * scaleFactor)
                );

                int directionSign = parameters.ExtendInwards ? -1 : 1;
                Point edgeMidPoint = new Point(
                    (startPoint.X + endPoint.X) / 2,
                    (startPoint.Y + endPoint.Y) / 2
                );

                Point fromMidToStart = new Point(
                    startPoint.X - edgeMidPoint.X,
                    startPoint.Y - edgeMidPoint.Y
                );
                Point fromMidToEnd = new Point(
                    endPoint.X - edgeMidPoint.X,
                    endPoint.Y - edgeMidPoint.Y
                );

                float lengthScale = (float)parameters.ExtensionLength / 100.0f;
                Point scaledStart = new Point(
                    edgeMidPoint.X + (int)(fromMidToStart.X * lengthScale),
                    edgeMidPoint.Y + (int)(fromMidToStart.Y * lengthScale)
                );
                Point scaledEnd = new Point(
                    edgeMidPoint.X + (int)(fromMidToEnd.X * lengthScale),
                    edgeMidPoint.Y + (int)(fromMidToEnd.Y * lengthScale)
                );

                int widthOffset = parameters.ExtensionWidth * (int)scaleFactor / 100;
                Point[] selectionRect = new Point[4];

                selectionRect[0] = new Point(
                    scaledStart.X + normalUnit.X * directionSign * widthOffset / (int)scaleFactor,
                    scaledStart.Y + normalUnit.Y * directionSign * widthOffset / (int)scaleFactor
                );
                selectionRect[1] = new Point(
                    scaledEnd.X + normalUnit.X * directionSign * widthOffset / (int)scaleFactor,
                    scaledEnd.Y + normalUnit.Y * directionSign * widthOffset / (int)scaleFactor
                );
                selectionRect[2] = new Point(
                    scaledEnd.X - normalUnit.X * directionSign * widthOffset / (int)scaleFactor,
                    scaledEnd.Y - normalUnit.Y * directionSign * widthOffset / (int)scaleFactor
                );
                selectionRect[3] = new Point(
                    scaledStart.X - normalUnit.X * directionSign * widthOffset / (int)scaleFactor,
                    scaledStart.Y - normalUnit.Y * directionSign * widthOffset / (int)scaleFactor
                );

                return ExtractROI(srcGray, selectionRect, quadrilateral, center, selectedEdgeIndex, index);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 处理四边形时发生错误: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// [关键修改] 提取ROI区域：使用外接矩形直接裁剪，不进行透视变换
        /// </summary>
        private ROIResult ExtractROI(Mat srcGray, Point[] selectionRect,
            Point[] quadrilateral, Point2f center, int edgeIndex, int index)
        {
            try
            {
                // 1. 计算 selectionRect 的外接矩形 (Bounding Rect)
                // 这样可以保留边缘在原始图像中的倾斜角度，这对SFR计算至关重要
                int minX = int.MaxValue, minY = int.MaxValue;
                int maxX = int.MinValue, maxY = int.MinValue;

                foreach (var pt in selectionRect)
                {
                    if (pt.X < minX) minX = pt.X;
                    if (pt.X > maxX) maxX = pt.X;
                    if (pt.Y < minY) minY = pt.Y;
                    if (pt.Y > maxY) maxY = pt.Y;
                }

                // 2. 限制在图像范围内
                minX = Math.Max(0, minX);
                minY = Math.Max(0, minY);
                maxX = Math.Min(srcGray.Width, maxX);
                maxY = Math.Min(srcGray.Height, maxY);

                int width = maxX - minX;
                int height = maxY - minY;

                if (width <= 0 || height <= 0) return null;

                Rect roiRect = new Rect(minX, minY, width, height);

                // 3. 直接裁剪 (Crop)
                using (var roiMat = new Mat(srcGray, roiRect))
                {
                    // 将Mat转换为字节数组
                    byte[] imageData = new byte[roiMat.Rows * roiMat.Cols];

                    // 注意：这里需要考虑内存连续性，Mat的子图可能不连续
                    if (roiMat.IsContinuous())
                    {
                        Marshal.Copy(roiMat.Data, imageData, 0, imageData.Length);
                    }
                    else
                    {
                        // 如果不连续，逐行复制
                        for (int i = 0; i < roiMat.Rows; i++)
                        {
                            IntPtr srcPtr = roiMat.Ptr(i);
                            Marshal.Copy(srcPtr, imageData, i * roiMat.Cols, roiMat.Cols);
                        }
                    }

                    // 创建ROI结果对象
                    var result = new ROIResult
                    {
                        ImageData = imageData,
                        Width = width,   // 使用实际裁剪的宽度
                        Height = height, // 使用实际裁剪的高度
                        Center = new PointF(center.X, center.Y),
                        EdgeIndex = edgeIndex,
                        RoiLocation = new PointF(minX, minY)
                    };

                    for (int i = 0; i < 4; i++)
                    {
                        result.Quadrilateral[i] = new PointF(quadrilateral[i].X, quadrilateral[i].Y);
                        result.SelectionArea[i] = new PointF(selectionRect[i].X, selectionRect[i].Y);
                    }

                    return result;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 提取ROI区域时发生错误: {ex.Message}");
                return null;
            }
        }

        private byte[] CreateVisualizationImage(Mat srcGray, ROIResults results,
            ExtractionParameters parameters, out int width, out int height)
        {
            width = 0; height = 0;
            if (srcGray == null || srcGray.Empty()) return null;

            try
            {
                using (var colorImage = new Mat())
                {
                    Cv2.CvtColor(srcGray, colorImage, ColorConversionCodes.GRAY2BGR);

                    foreach (var roi in results.Results)
                    {
                        // 绘制原始四边形
                        for (int i = 0; i < 4; i++)
                        {
                            Point p1 = new Point((int)roi.Quadrilateral[i].X, (int)roi.Quadrilateral[i].Y);
                            Point p2 = new Point((int)roi.Quadrilateral[(i + 1) % 4].X, (int)roi.Quadrilateral[(i + 1) % 4].Y);
                            Cv2.Line(colorImage, p1, p2, new Scalar(255, 0, 0), 2);
                        }

                        // 绘制框选区域 (注意：现在是外接矩形内的旋转区域，依然画出来方便看)
                        Point[] selectionPoints = new Point[4];
                        for (int i = 0; i < 4; i++)
                        {
                            selectionPoints[i] = new Point((int)roi.SelectionArea[i].X, (int)roi.SelectionArea[i].Y);
                        }

                        // 绘制实际裁剪的矩形框 (新增)框选区域是按照边缘向量方向生成的，这样裁剪出来的图像会导致SFR算法失效，所以实际裁剪时要裁剪一个正矩形
                        Rect cropRect = new Rect((int)roi.RoiLocation.X, (int)roi.RoiLocation.Y, roi.Width, roi.Height);
                        Cv2.Rectangle(colorImage, cropRect, new Scalar(0, 255, 255), 1); // 黄色表示实际裁剪框

                        for (int i = 0; i < 4; i++)
                        {
                            Point p1 = selectionPoints[i];
                            Point p2 = selectionPoints[(i + 1) % 4];
                            Cv2.Line(colorImage, p1, p2, new Scalar(0, 255, 0), 2);
                        }

                        // 标记选中的边
                        int edgeIndex = roi.EdgeIndex;
                        if (edgeIndex >= 0 && edgeIndex < 4)
                        {
                            Point p1 = new Point((int)roi.Quadrilateral[edgeIndex].X, (int)roi.Quadrilateral[edgeIndex].Y);
                            Point p2 = new Point((int)roi.Quadrilateral[(edgeIndex + 1) % 4].X, (int)roi.Quadrilateral[(edgeIndex + 1) % 4].Y);
                            Cv2.Line(colorImage, p1, p2, new Scalar(0, 0, 255), 3);
                        }

                        Cv2.PutText(colorImage, $"#{results.Results.IndexOf(roi)}",
                                  new Point((int)roi.Center.X, (int)roi.Center.Y),
                                  HersheyFonts.HersheySimplex, 0.7, Scalar.Red, 2);
                    }

                    width = colorImage.Cols;
                    height = colorImage.Rows;
                    byte[] imageData = new byte[width * height * 3];
                    Marshal.Copy(colorImage.Data, imageData, 0, imageData.Length);

                    return imageData;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 创建可视化图像时发生错误: {ex.Message}");
                return null;
            }
        }

        private string SaveVisualizationToFile(byte[] imageData, int width, int height,
            ExtractionParameters parameters)
        {
            if (imageData == null || imageData.Length == 0) return null;
            try
            {
                string savePath = parameters.VisualizationPath;
                if (string.IsNullOrEmpty(savePath))
                {
                    string fileName = parameters.VisualizationFileName;
                    if (string.IsNullOrEmpty(Path.GetExtension(fileName))) fileName += ".png";
                    savePath = Path.Combine(Directory.GetCurrentDirectory(), fileName);
                }

                unsafe
                {
                    fixed (byte* p = imageData)
                    {
                        IntPtr dataPtr = new IntPtr(p);
                        using (var mat = Mat.FromPixelData(height, width, MatType.CV_8UC3, dataPtr))
                        {
                            Cv2.ImWrite(savePath, mat);
                            return savePath;
                        }
                    }
                }
            }
            catch (Exception) { return null; }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed) { _disposed = true; }
        }

        ~EdgeROIExtractorEngine()
        {
            Dispose(false);
        }
    }
}