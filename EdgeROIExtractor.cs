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
        public static string Version => "1.0.0";

        /// <summary>
        /// 从灰度图像中提取ROI
        /// </summary>
        /// <param name="grayImageData">灰度图像数据（8位）</param>
        /// <param name="width">图像宽度</param>
        /// <param name="height">图像高度</param>
        /// <param name="parameters">提取参数</param>
        /// <returns>ROI提取结果</returns>
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
                // 使用 Mat.FromPixelData 替代已过时的构造函数
                unsafe
                {
                    fixed (byte* p = grayImageData)
                    {
                        IntPtr dataPtr = new IntPtr(p);

                        // 使用 Mat.FromPixelData 创建 Mat 对象
                        using (var srcGray = Mat.FromPixelData(height, width, MatType.CV_8UC1, dataPtr))
                        {
                            // 处理图像并提取ROI
                            ProcessImage(srcGray, parameters, results);

                            // 如果需要保存可视化图像或返回可视化数据
                            if ((parameters.SaveVisualization || parameters.ReturnVisualizationData)
                                && results.Success && results.Results.Count > 0)
                            {
                                // 创建可视化图像
                                byte[] visualizationData = CreateVisualizationImage(
                                    srcGray, results, parameters,
                                    out int vizWidth, out int vizHeight);

                                results.VisualizationWidth = vizWidth;
                                results.VisualizationHeight = vizHeight;

                                // 如果需要返回数据
                                if (parameters.ReturnVisualizationData)
                                {
                                    results.VisualizationImageData = visualizationData;
                                }

                                // 如果需要保存到文件
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
        /// 从彩色图像中提取ROI（自动转换为灰度）
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
                // 使用 Mat.FromPixelData 替代已过时的构造函数
                unsafe
                {
                    fixed (byte* p = colorImageData)
                    {
                        IntPtr dataPtr = new IntPtr(p);

                        // 确定Mat类型
                        MatType matType = channels == 3 ? MatType.CV_8UC3 : MatType.CV_8UC4;

                        // 使用 Mat.FromPixelData 创建彩色图像Mat
                        using (var colorMat = Mat.FromPixelData(height, width, matType, dataPtr))
                        using (var grayMat = new Mat())
                        {
                            // 转换为灰度图像
                            Cv2.CvtColor(colorMat, grayMat,
                                channels == 3 ? ColorConversionCodes.BGR2GRAY : ColorConversionCodes.BGRA2GRAY);

                            // 提取灰度图像数据
                            byte[] grayData = new byte[grayMat.Rows * grayMat.Cols];
                            unsafe
                            {
                                byte* grayDataPtr = (byte*)grayMat.Data.ToPointer();
                                for (int i = 0; i < grayData.Length; i++)
                                {
                                    grayData[i] = grayDataPtr[i];
                                }
                            }

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
                // 读取图像
                using (var mat = Cv2.ImRead(filePath, ImreadModes.Grayscale))
                {
                    if (mat.Empty())
                    {
                        return new ROIResults { Success = false, ErrorMessage = $"无法读取图像: {filePath}" };
                    }

                    // 提取图像数据
                    byte[] imageData = new byte[mat.Rows * mat.Cols];
                    Marshal.Copy(mat.Data, imageData, 0, imageData.Length);

                    return ExtractROIs(imageData, mat.Cols, mat.Rows, parameters);
                }
            }
            catch (Exception ex)
            {
                return new ROIResults { Success = false, ErrorMessage = $"从文件加载时发生错误: {ex.Message}" };
            }
        }

        /// <summary>
        /// 处理图像并提取ROI
        /// </summary>
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

                    // 多边形近似
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
        /// 处理单个四边形并提取ROI
        /// </summary>
        private ROIResult ProcessQuadrilateral(Mat srcGray, Point[] quadrilateral,
            int index, ExtractionParameters parameters)
        {
            try
            {
                // 获取四边形的四个角点
                Point p1 = quadrilateral[0], p2 = quadrilateral[1],
                      p3 = quadrilateral[2], p4 = quadrilateral[3];

                // 计算四边形的中心点
                Moments m = Cv2.Moments(quadrilateral);
                if (Math.Abs(m.M00) < 1e-6)
                    return null;

                Point2f center = new Point2f(
                    (float)(m.M10 / m.M00),
                    (float)(m.M01 / m.M00)
                );

                // 定义四条边
                Point[][] edges = new Point[4][];
                edges[0] = new Point[] { p1, p2 };  // 边1：p1→p2
                edges[1] = new Point[] { p2, p3 };  // 边2：p2→p3
                edges[2] = new Point[] { p3, p4 };  // 边3：p3→p4
                edges[3] = new Point[] { p4, p1 };  // 边4：p4→p1

                // 获取选中的边
                int selectedEdgeIndex = parameters.SelectedEdgeIndex;
                if (selectedEdgeIndex < 0) selectedEdgeIndex = 0;
                if (selectedEdgeIndex > 3) selectedEdgeIndex = 3;

                Point startPoint = edges[selectedEdgeIndex][0];
                Point endPoint = edges[selectedEdgeIndex][1];

                // 计算边的方向向量
                Point direction = new Point(endPoint.X - startPoint.X, endPoint.Y - startPoint.Y);
                double edgeLength = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);

                if (edgeLength < 10)
                    return null;

                // 计算法向量（垂直于边的方向）
                Point normal = new Point(-direction.Y, direction.X);
                double normalLength = Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y);

                if (normalLength < 1e-6)
                    return null;

                // 计算单位向量
                double scaleFactor = 100.0;
                Point normalUnit = new Point(
                    (int)(normal.X / normalLength * scaleFactor),
                    (int)(normal.Y / normalLength * scaleFactor)
                );

                // 确定框选方向
                int directionSign = parameters.ExtendInwards ? -1 : 1;

                // 计算边的中点
                Point edgeMidPoint = new Point(
                    (startPoint.X + endPoint.X) / 2,
                    (startPoint.Y + endPoint.Y) / 2
                );

                // 计算从边中点出发的向量
                Point fromMidToStart = new Point(
                    startPoint.X - edgeMidPoint.X,
                    startPoint.Y - edgeMidPoint.Y
                );
                Point fromMidToEnd = new Point(
                    endPoint.X - edgeMidPoint.X,
                    endPoint.Y - edgeMidPoint.Y
                );

                // 缩放边的端点（控制框选长度）
                float lengthScale = (float)parameters.ExtensionLength / 100.0f;
                Point scaledStart = new Point(
                    edgeMidPoint.X + (int)(fromMidToStart.X * lengthScale),
                    edgeMidPoint.Y + (int)(fromMidToStart.Y * lengthScale)
                );
                Point scaledEnd = new Point(
                    edgeMidPoint.X + (int)(fromMidToEnd.X * lengthScale),
                    edgeMidPoint.Y + (int)(fromMidToEnd.Y * lengthScale)
                );

                // 计算框选矩形的四个角点
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

                // 提取ROI区域
                return ExtractROI(srcGray, selectionRect,
                    parameters.ExtensionWidth, (int)edgeLength,
                    quadrilateral, center, selectedEdgeIndex, index);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 处理四边形时发生错误: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 提取ROI区域
        /// </summary>
        private ROIResult ExtractROI(Mat srcGray, Point[] selectionRect,
            int roiHeight, int roiWidth,
            Point[] quadrilateral, Point2f center, int edgeIndex, int index)
        {
            try
            {
                // 将Point转换为Point2f
                Point2f[] srcPoints = new Point2f[4];
                for (int i = 0; i < 4; i++)
                {
                    srcPoints[i] = new Point2f(selectionRect[i].X, selectionRect[i].Y);
                }

                // 目标矩形顶点
                Point2f[] dstPoints = new Point2f[4]
                {
                    new Point2f(0, 0),
                    new Point2f(roiWidth, 0),
                    new Point2f(roiWidth, roiHeight),
                    new Point2f(0, roiHeight)
                };

                // 计算透视变换矩阵
                using (var transformMatrix = Cv2.GetPerspectiveTransform(srcPoints, dstPoints))
                {
                    // 进行透视变换
                    using (var roiMat = new Mat())
                    {
                        Cv2.WarpPerspective(srcGray, roiMat, transformMatrix,
                            new Size(roiWidth, roiHeight));

                        // 将Mat转换为字节数组
                        byte[] imageData = new byte[roiMat.Rows * roiMat.Cols];
                        Marshal.Copy(roiMat.Data, imageData, 0, imageData.Length);

                        // 创建ROI结果对象
                        var result = new ROIResult
                        {
                            ImageData = imageData,
                            Width = roiWidth,
                            Height = roiHeight,
                            Center = new PointF(center.X, center.Y),
                            EdgeIndex = edgeIndex,
                            RoiLocation = new PointF(srcPoints[0].X, srcPoints[0].Y)
                        };

                        // 保存原始四边形顶点
                        for (int i = 0; i < 4; i++)
                        {
                            result.Quadrilateral[i] = new PointF(
                                quadrilateral[i].X, quadrilateral[i].Y);
                        }

                        // 保存框选区域顶点
                        for (int i = 0; i < 4; i++)
                        {
                            result.SelectionArea[i] = new PointF(
                                selectionRect[i].X, selectionRect[i].Y);
                        }

                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 提取ROI区域时发生错误: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 创建可视化图像
        /// </summary>
        private byte[] CreateVisualizationImage(Mat srcGray, ROIResults results,
            ExtractionParameters parameters, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (srcGray == null || srcGray.Empty() || results == null || results.Results.Count == 0)
                return null;

            try
            {
                using (var colorImage = new Mat())
                {
                    // 转换为彩色图像
                    Cv2.CvtColor(srcGray, colorImage, ColorConversionCodes.GRAY2BGR);

                    // 绘制每个ROI
                    foreach (var roi in results.Results)
                    {
                        // 绘制原始四边形（蓝色）
                        for (int i = 0; i < 4; i++)
                        {
                            Point p1 = new Point((int)roi.Quadrilateral[i].X,
                                                 (int)roi.Quadrilateral[i].Y);
                            Point p2 = new Point((int)roi.Quadrilateral[(i + 1) % 4].X,
                                                 (int)roi.Quadrilateral[(i + 1) % 4].Y);
                            Cv2.Line(colorImage, p1, p2, new Scalar(255, 0, 0), 2);
                        }

                        // 绘制框选区域（绿色半透明）
                        Point[] selectionPoints = new Point[4];
                        for (int i = 0; i < 4; i++)
                        {
                            selectionPoints[i] = new Point((int)roi.SelectionArea[i].X,
                                                           (int)roi.SelectionArea[i].Y);
                        }

                        // 先填充半透明区域
                        using (Mat overlay = colorImage.Clone())
                        {
                            Cv2.FillPoly(overlay, new Point[][] { selectionPoints },
                                       new Scalar(0, 255, 0, 128));
                            Cv2.AddWeighted(overlay, 0.3, colorImage, 0.7, 0, colorImage);
                        }

                        // 绘制边框
                        for (int i = 0; i < 4; i++)
                        {
                            Point p1 = selectionPoints[i];
                            Point p2 = selectionPoints[(i + 1) % 4];
                            Cv2.Line(colorImage, p1, p2, new Scalar(0, 255, 0), 2);
                        }

                        // 标记选中的边（红色）
                        int edgeIndex = roi.EdgeIndex;
                        if (edgeIndex >= 0 && edgeIndex < 4)
                        {
                            Point p1 = new Point((int)roi.Quadrilateral[edgeIndex].X,
                                                 (int)roi.Quadrilateral[edgeIndex].Y);
                            Point p2 = new Point((int)roi.Quadrilateral[(edgeIndex + 1) % 4].X,
                                                 (int)roi.Quadrilateral[(edgeIndex + 1) % 4].Y);
                            Cv2.Line(colorImage, p1, p2, new Scalar(0, 0, 255), 3);
                        }

                        // 绘制中心点（红色）
                        Cv2.Circle(colorImage,
                            new Point((int)roi.Center.X, (int)roi.Center.Y),
                            6, Scalar.Red, -1);

                        // 标记每个顶点
                        for (int i = 0; i < 4; i++)
                        {
                            Point p = new Point((int)roi.Quadrilateral[i].X,
                                                (int)roi.Quadrilateral[i].Y);
                            Cv2.Circle(colorImage, p, 5, new Scalar(255, 255, 0), -1);
                            Cv2.PutText(colorImage, $"P{i + 1}", new Point(p.X + 5, p.Y - 5),
                                      HersheyFonts.HersheySimplex, 0.5, new Scalar(255, 255, 0), 1);
                        }

                        // 在四边形中心显示序号
                        Cv2.PutText(colorImage, $"#{results.Results.IndexOf(roi)}",
                                  new Point((int)roi.Center.X - 10, (int)roi.Center.Y + 5),
                                  HersheyFonts.HersheySimplex, 0.7, Scalar.Red, 2);
                    }

                    // 添加参数信息
                    if (parameters.ShowParametersOnImage)
                    {
                        string paramText = $"Width: {parameters.ExtensionWidth}, " +
                                          $"Length: {parameters.ExtensionLength}, " +
                                          $"Edge: E{parameters.SelectedEdgeIndex}, " +
                                          $"Inward: {parameters.ExtendInwards}";

                        // 添加背景框
                        int baseline = 0;
                        var textSize = Cv2.GetTextSize(paramText, HersheyFonts.HersheySimplex, 0.6, 1, out baseline);
                        Cv2.Rectangle(colorImage, new Point(10, 10),
                                     new Point(10 + textSize.Width + 10, 10 + textSize.Height + baseline + 10),
                                     new Scalar(0, 0, 0, 200), -1);

                        Cv2.PutText(colorImage, paramText, new Point(15, 15 + textSize.Height),
                                  HersheyFonts.HersheySimplex, 0.6, new Scalar(255, 255, 255), 1);

                        // 添加结果信息
                        string resultText = $"Found: {results.Results.Count} ROI(s), " +
                                           $"Time: {results.ProcessingTimeMs}ms";
                        var resultTextSize = Cv2.GetTextSize(resultText, HersheyFonts.HersheySimplex, 0.6, 1, out baseline);
                        Cv2.Rectangle(colorImage, new Point(10, 40),
                                     new Point(10 + resultTextSize.Width + 10, 40 + resultTextSize.Height + baseline + 10),
                                     new Scalar(0, 0, 0, 200), -1);

                        Cv2.PutText(colorImage, resultText, new Point(15, 45 + resultTextSize.Height),
                                  HersheyFonts.HersheySimplex, 0.6, new Scalar(255, 255, 255), 1);
                    }

                    // 转换为字节数组
                    width = colorImage.Cols;
                    height = colorImage.Rows;
                    byte[] imageData = new byte[width * height * 3]; // RGB = 3通道
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

        /// <summary>
        /// 保存可视化图像到文件
        /// </summary>
        private string SaveVisualizationToFile(byte[] imageData, int width, int height,
            ExtractionParameters parameters)
        {
            if (imageData == null || imageData.Length == 0)
                return null;

            try
            {
                // 确定保存路径
                string savePath;
                if (!string.IsNullOrEmpty(parameters.VisualizationPath))
                {
                    // 如果指定了完整路径，使用指定路径
                    savePath = parameters.VisualizationPath;
                }
                else
                {
                    // 否则保存到运行目录
                    string directory = Directory.GetCurrentDirectory();
                    string fileName = parameters.VisualizationFileName;

                    // 确保文件名包含扩展名
                    if (string.IsNullOrEmpty(Path.GetExtension(fileName)))
                    {
                        fileName += ".png";
                    }

                    savePath = Path.Combine(directory, fileName);

                    // 如果文件已存在，添加时间戳
                    if (File.Exists(savePath))
                    {
                        string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                        string fileExt = Path.GetExtension(fileName);
                        fileName = $"{fileNameWithoutExt}_{timeStamp}{fileExt}";
                        savePath = Path.Combine(directory, fileName);
                    }
                }

                // 使用 Mat.FromPixelData 从字节数组创建Mat
                unsafe
                {
                    fixed (byte* p = imageData)
                    {
                        IntPtr dataPtr = new IntPtr(p);

                        // 使用 Mat.FromPixelData 创建Mat（RGB，3通道）
                        using (var mat = Mat.FromPixelData(height, width, MatType.CV_8UC3, dataPtr))
                        {
                            string extension = Path.GetExtension(savePath).ToLower();
                            var imwriteParams = new int[2];

                            if (extension == ".jpg" || extension == ".jpeg")
                            {
                                // JPEG质量参数
                                imwriteParams = new int[]
                                {
                            (int)ImwriteFlags.JpegQuality,
                            parameters.ImageQuality
                                };
                            }
                            else if (extension == ".png")
                            {
                                // PNG压缩级别（0-9，0是无压缩，9是最大压缩）
                                int compressionLevel = 9 - (parameters.ImageQuality / 10);
                                compressionLevel = Math.Max(0, Math.Min(9, compressionLevel));
                                imwriteParams = new int[]
                                {
                            (int)ImwriteFlags.PngCompression,
                            compressionLevel
                                };
                            }

                            bool saveResult = Cv2.ImWrite(savePath, mat, imwriteParams);

                            if (saveResult)
                            {
                                Console.WriteLine($"[INFO] 可视化图像已保存到: {savePath}");
                                return savePath;
                            }
                            else
                            {
                                Console.WriteLine($"[ERROR] 保存可视化图像失败: {savePath}");
                                return null;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 保存可视化图像时发生错误: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 释放托管资源
                }

                _disposed = true;
            }
        }

        ~EdgeROIExtractorEngine()
        {
            Dispose(false);
        }
    }
}