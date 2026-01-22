using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;

namespace EdgeROIExtractor
{
    /// <summary>
    /// 简单提取器接口
    /// </summary>
    public static class SimpleExtractor
    {
        /// <summary>
        /// 从灰度图像中提取ROI
        /// </summary>
        public static ROIResults Extract(byte[] grayImageData, int width, int height,
            int extensionWidth = 30, int extensionLength = 80,
            bool extendInwards = true, int selectedEdgeIndex = 0,
            bool saveVisualization = false, string outputPath = "")
        {
            using (var extractor = new EdgeROIExtractorEngine())
            {
                var parameters = new ExtractionParameters
                {
                    ExtensionWidth = extensionWidth,
                    ExtensionLength = extensionLength,
                    ExtendInwards = extendInwards,
                    SelectedEdgeIndex = selectedEdgeIndex,
                    SaveVisualization = saveVisualization
                };

                if (!string.IsNullOrEmpty(outputPath))
                {
                    parameters.VisualizationPath = outputPath;
                }
                else if (saveVisualization)
                {
                    // 自动生成文件名
                    string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    parameters.VisualizationFileName = $"edge_roi_result_{timeStamp}.png";
                }

                return extractor.ExtractROIs(grayImageData, width, height, parameters);
            }
        }

        /// <summary>
        /// 从彩色图像中提取ROI
        /// </summary>
        public static ROIResults ExtractFromColor(byte[] colorImageData, int width, int height,
            int channels = 3, int extensionWidth = 30, int extensionLength = 80,
            bool extendInwards = true, int selectedEdgeIndex = 0,
            bool saveVisualization = false, string outputPath = "")
        {
            using (var extractor = new EdgeROIExtractorEngine())
            {
                var parameters = new ExtractionParameters
                {
                    ExtensionWidth = extensionWidth,
                    ExtensionLength = extensionLength,
                    ExtendInwards = extendInwards,
                    SelectedEdgeIndex = selectedEdgeIndex,
                    SaveVisualization = saveVisualization
                };

                if (!string.IsNullOrEmpty(outputPath))
                {
                    parameters.VisualizationPath = outputPath;
                }
                else if (saveVisualization)
                {
                    // 自动生成文件名
                    string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    parameters.VisualizationFileName = $"edge_roi_result_{timeStamp}.png";
                }

                return extractor.ExtractROIsFromColor(colorImageData, width, height, channels, parameters);
            }
        }

        /// <summary>
        /// 从文件加载图像并提取ROI
        /// </summary>
        public static ROIResults ExtractFromFile(string filePath,
            int extensionWidth = 30, int extensionLength = 80,
            bool extendInwards = true, int selectedEdgeIndex = 0,
            bool saveVisualization = false, string outputPath = "")
        {
            using (var extractor = new EdgeROIExtractorEngine())
            {
                var parameters = new ExtractionParameters
                {
                    ExtensionWidth = extensionWidth,
                    ExtensionLength = extensionLength,
                    ExtendInwards = extendInwards,
                    SelectedEdgeIndex = selectedEdgeIndex,
                    SaveVisualization = saveVisualization
                };

                if (!string.IsNullOrEmpty(outputPath))
                {
                    parameters.VisualizationPath = outputPath;
                }
                else if (saveVisualization)
                {
                    // 自动生成文件名
                    string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    parameters.VisualizationFileName = $"edge_roi_result_{timeStamp}.png";
                }

                return extractor.ExtractROIsFromFile(filePath, parameters);
            }
        }

        /// <summary>
        /// 获取ROI数据用于C++ DLL调用
        /// </summary>
        public static List<ROIDataForCpp> GetROIDataForCpp(ROIResults results)
        {
            var cppDataList = new List<ROIDataForCpp>();

            if (results == null || !results.Success || results.Results.Count == 0)
                return cppDataList;

            foreach (var roi in results.Results)
            {
                var cppData = new ROIDataForCpp(roi);
                cppDataList.Add(cppData);
            }

            return cppDataList;
        }

        /// <summary>
        /// 保存单个ROI图像到文件
        /// </summary>
        public static bool SaveROIToFile(ROIResult roi, string filePath)
        {
            if (roi == null || roi.ImageData == null || string.IsNullOrEmpty(filePath))
                return false;

            try
            {
                // 确保目录存在
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // 使用 Mat.FromPixelData 保存图像
                unsafe
                {
                    fixed (byte* p = roi.ImageData)
                    {
                        IntPtr dataPtr = new IntPtr(p);

                        // 使用 Mat.FromPixelData 创建Mat（灰度，1通道）
                        using (var mat = Mat.FromPixelData(roi.Height, roi.Width, MatType.CV_8UC1, dataPtr))
                        {
                            return Cv2.ImWrite(filePath, mat);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 保存ROI图像时发生错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 批量保存所有ROI图像
        /// </summary>
        public static List<string> SaveAllROIs(ROIResults results, string outputDirectory)
        {
            var savedPaths = new List<string>();

            if (results == null || !results.Success || results.Results.Count == 0)
                return savedPaths;

            try
            {
                // 确保目录存在
                if (!Directory.Exists(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                // 保存每个ROI
                for (int i = 0; i < results.Results.Count; i++)
                {
                    string filePath = Path.Combine(outputDirectory, $"roi_{i:000}.bmp");
                    if (SaveROIToFile(results.Results[i], filePath))
                    {
                        savedPaths.Add(filePath);
                    }
                }

                return savedPaths;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 批量保存ROI图像时发生错误: {ex.Message}");
                return savedPaths;
            }
        }

        /// <summary>
        /// 获取版本信息
        /// </summary>
        public static string GetVersion()
        {
            return EdgeROIExtractorEngine.Version;
        }
    }
}