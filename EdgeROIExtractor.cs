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


        /// <summary>
        /// 将四边形的4个点归一化为固定顺序：左上、左下、右下、右上
        /// 目的：不管四边形是顺时针/逆时针、起点从哪个角开始，SelectedEdgeIndex 都能稳定对应同一条“物理边”
        /// 例如：SelectedEdgeIndex=0 永远是“左竖边”，不会因为旋转方向不同而误选到横边。
        /// </summary>
        private static Point[] NormalizeQuadPoints(Point[] pts)
        {
            if (pts == null || pts.Length != 4) return pts;

            // 1) 用 (x+y) 找左上(tl) 和 右下(br)
            int minSum = int.MaxValue, maxSum = int.MinValue;
            Point tl = pts[0], br = pts[0];

            for (int i = 0; i < 4; i++)
            {
                int s = pts[i].X + pts[i].Y;
                if (s < minSum) { minSum = s; tl = pts[i]; }
                if (s > maxSum) { maxSum = s; br = pts[i]; }
            }

            // 2) 剩下两个点用 (x-y) 找右上(tr) 和 左下(bl)
            int minDiff = int.MaxValue, maxDiff = int.MinValue;
            Point tr = pts[0], bl = pts[0];

            for (int i = 0; i < 4; i++)
            {
                Point p = pts[i];
                if (p == tl || p == br) continue;

                int d = p.X - p.Y;
                if (d < minDiff) { minDiff = d; tr = p; }
                if (d > maxDiff) { maxDiff = d; bl = p; }
            }

            // 3) 返回固定顺序：p1=左上、p2=左下、p3=右下、p4=右上
            return new Point[] { tl, bl, br, tr };
        }

        private ROIResult ProcessQuadrilateral(Mat srcGray, Point[] quadrilateral,
                    int index, ExtractionParameters parameters)
        {
            try
            {
                quadrilateral = NormalizeQuadPoints(quadrilateral);
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

                double halfW = Math.Max(parameters.ExtensionWidth, 16);  // 半宽（跨边方向）
                double desiredLen = parameters.ExtensionLength;
                if (desiredLen <= 0) desiredLen = edgeLength;

                // 只取边“中段”，避免把角点/相邻边一起截进去（这是 SFR 失败率高的最常见原因）
                desiredLen = Math.Min(desiredLen, edgeLength);
                if (desiredLen < 40) desiredLen = Math.Min(40, edgeLength);
                double halfLen = desiredLen / 2.0;

                var edgeMid = new Point2d((startPoint.X + endPoint.X) / 2.0, (startPoint.Y + endPoint.Y) / 2.0);
                var dirUnit = new Point2d(direction.X / edgeLength, direction.Y / edgeLength);
                var normalUnitD = new Point2d(-dirUnit.Y, dirUnit.X);

                var segStart = new Point2d(edgeMid.X - dirUnit.X * halfLen, edgeMid.Y - dirUnit.Y * halfLen);
                var segEnd = new Point2d(edgeMid.X + dirUnit.X * halfLen, edgeMid.Y + dirUnit.Y * halfLen);

                double sign = parameters.ExtendInwards ? -1.0 : 1.0; // 仅决定点顺序，不影响矩形本身
                Point[] selectionRect = new Point[4];
                selectionRect[0] = new Point(
                    (int)Math.Round(segStart.X + normalUnitD.X * sign * halfW),
                    (int)Math.Round(segStart.Y + normalUnitD.Y * sign * halfW)
                );
                selectionRect[1] = new Point(
                    (int)Math.Round(segEnd.X + normalUnitD.X * sign * halfW),
                    (int)Math.Round(segEnd.Y + normalUnitD.Y * sign * halfW)
                );
                selectionRect[2] = new Point(
                    (int)Math.Round(segEnd.X - normalUnitD.X * sign * halfW),
                    (int)Math.Round(segEnd.Y - normalUnitD.Y * sign * halfW)
                );
                selectionRect[3] = new Point(
                    (int)Math.Round(segStart.X - normalUnitD.X * sign * halfW),
                    (int)Math.Round(segStart.Y - normalUnitD.Y * sign * halfW)
                );

                return ExtractROI(srcGray, segStart, segEnd, halfW, selectionRect, quadrilateral, center, selectedEdgeIndex, index);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 处理四边形时发生错误: {ex.Message}");
                return null;
            }
        }
        /// <summary>
        /// [修改版] 提取ROI区域：自动处理旋转 + 强制4字节内存对齐
        /// </summary>
        private ROIResult ExtractROI(Mat srcGray, Point2d segStart, Point2d segEnd, double halfWidth,
            Point[] selectionRect, Point[] quadrilateral, Point2f center, int edgeIndex, int index)
        {
            try
            {
                // 1. 计算 ROI 的外接矩形（更“紧”的轴对齐矩形）
                //    不直接用 selectionRect 的 BoundingRect：斜边会导致横向膨胀，容易把相邻边/角点带进来
                int minX = (int)Math.Floor(Math.Min(segStart.X, segEnd.X) - halfWidth - 2);
                int maxX = (int)Math.Ceiling(Math.Max(segStart.X, segEnd.X) + halfWidth + 2);

                double yPad = Math.Max(2.0, halfWidth * 0.35);
                int minY = (int)Math.Floor(Math.Min(segStart.Y, segEnd.Y) - yPad);
                int maxY = (int)Math.Ceiling(Math.Max(segStart.Y, segEnd.Y) + yPad);

                int rawWidth = maxX - minX;
                if (rawWidth % 4 != 0)
                {
                    int padding = 4 - (rawWidth % 4);
                    maxX += padding; // 向右扩展
                }

                // 修正 Y 轴 (Height) - 即使是高度也建议对齐，因为旋转后高度会变成宽度
                int rawHeight = maxY - minY;
                if (rawHeight % 4 != 0)
                {
                    int padding = 4 - (rawHeight % 4);
                    maxY += padding; // 向下扩展
                }
                minX = Math.Max(0, minX);
                minY = Math.Max(0, minY);
                maxX = Math.Min(srcGray.Width, maxX);
                maxY = Math.Min(srcGray.Height, maxY);

                int width = maxX - minX;
                int height = maxY - minY;

                // 再次检查对齐（如果因越界被裁剪了，这里强制丢弃最后几个像素以保全对齐）
                width = width - (width % 4);
                height = height - (height % 4);

                if (width <= 0 || height <= 0) return null;

                Rect roiRect = new Rect(minX, minY, width, height);

                // 3. 裁剪并处理旋转
                using (var roiMat = new Mat(srcGray, roiRect))
                {
                    Mat finalMat = roiMat;
                    bool isRotated = false;

                    try
                    {
                        // 自动检测方向并旋转
                        // 如果 宽 > 高，说明是横向边，需要旋转成竖向
                        if (finalMat.Width > finalMat.Height)
                        {
                            var rotated = new Mat();
                            Cv2.Rotate(roiMat, rotated, RotateFlags.Rotate90Clockwise);
                            finalMat = rotated;
                            isRotated = true;

                            // 更新宽高
                            width = finalMat.Width;
                            height = finalMat.Height;
                        }

                        // 将Mat转换为字节数组
                        // 此时 width 一定是 4 的倍数，C++ 读取绝对安全
                        byte[] imageData = new byte[finalMat.Rows * finalMat.Cols];

                        if (finalMat.IsContinuous())
                        {
                            Marshal.Copy(finalMat.Data, imageData, 0, imageData.Length);
                        }
                        else
                        {
                            for (int i = 0; i < finalMat.Rows; i++)
                            {
                                IntPtr srcPtr = finalMat.Ptr(i);
                                Marshal.Copy(srcPtr, imageData, i * finalMat.Cols, finalMat.Cols);
                            }
                        }

                        var result = new ROIResult
                        {
                            ImageData = imageData,
                            Width = width,
                            Height = height,
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
                    finally
                    {
                        if (isRotated && finalMat != null) finalMat.Dispose();
                    }
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

        // 在 EdgeROIExtractorEngine 类中添加/修改以下方法

        /// <summary>
        /// [极速版] 直接处理Mat对象，避免 byte[] <-> Mat 的来回拷贝
        /// </summary>
        public ROIResults ExtractROIsFromMat(Mat srcMat, ExtractionParameters parameters = null)
        {
            var results = new ROIResults
            {
                OriginalImageSize = new System.Drawing.Size(srcMat.Cols, srcMat.Rows)
            };

            if (srcMat.Empty())
            {
                results.Success = false;
                results.ErrorMessage = "输入Mat为空";
                return results;
            }

            if (parameters == null) parameters = ExtractionParameters.Default();

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // 优化点：直接在内部管理生命周期，不进行额外拷贝
                using (var processingMat = new Mat())
                {
                    // 1. 通道处理：如果是彩色图，直接提取绿色通道（最快且符合你的逻辑）
                    if (srcMat.Channels() == 3 || srcMat.Channels() == 4)
                    {
                        // 索引1是绿色通道 (BGR中的G)
                        Cv2.ExtractChannel(srcMat, processingMat, 1);
                    }
                    else
                    {
                        srcMat.CopyTo(processingMat);
                    }
                    ProcessImage(processingMat, parameters, results);
                }
            }
            catch (Exception ex)
            {
                results.Success = false;
                results.ErrorMessage = $"处理Mat时发生错误: {ex.Message}";
            }

            stopwatch.Stop();
            results.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
            return results;
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