using HqExtractorLib;
using HqExtractorLib.Diagnostics;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using OpenCvSharp;
using System;
using System.IO;


namespace Last_Z_Client_o_matic
{
    [Flags]
    enum ImageFeature
    {
        None = 0,
        HQBuilding = 1,
        Flag = 2,
        Username = 4,
        HQLevel = 8,
    }

    internal record TemplateImageFile(string Name, string Path, ImageFeature Features);

    internal class Program
    {
        static string TemplatesDirectory = @"Templates";
        static void RunAutoTuner(Microsoft.Extensions.Logging.ILogger logger)
        {
            var config = new HqExtractorConfig
            {
                // Use your current HSV badge thresholds
                BadgeHsvLower = new OpenCvSharp.Scalar(0, 0, 80),
                BadgeHsvUpper = new OpenCvSharp.Scalar(180, 30, 220),
                ThrowOnMissing = false,
                AllowPartialExtraction = true,
                DiagnosticsEnabled = true,
                DiagnosticsDirectory = "diag",
                ReferenceResolution = new OpenCvSharp.Size(1936, 1048),
            };

            var hqSamples = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TemplatesDirectory, "HQ", "Samples");
            var tuner = BadgeConfigAutoTuner.AnalyzeFolder(hqSamples, config, logger);

            Console.WriteLine("Suggested tuning:");
            Console.WriteLine($"MinAreaPx: {tuner.SuggestedMinArea}");
            Console.WriteLine($"MaxAreaPx: {tuner.SuggestedMaxArea}");
            Console.WriteLine($"MaxAreaFraction: {tuner.SuggestedMaxAreaFraction}");
            Console.WriteLine($"Aspect: {tuner.SuggestedMinAspect}–{tuner.SuggestedMaxAspect}");
            Console.WriteLine($"MinSolidity: {tuner.SuggestedMinSolidity}");
            Console.WriteLine($"MaxMeanS: {tuner.SuggestedMaxMeanS}");
            Console.WriteLine($"MinMeanV: {tuner.SuggestedMinMeanV}");
        }
        static void Main(string[] args)
        {
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.ClearProviders();
                builder.AddNLog();   // NLog backend
            });

            NLog.LogManager.Setup().LoadConfiguration(builder =>
            {
                builder.ForLogger().FilterMinLevel(NLog.LogLevel.Info).WriteToConsole();
                builder.ForLogger().FilterMinLevel(NLog.LogLevel.Trace).WriteToFile(fileName: "lz.clientomatic.log");
            });

            var config = new HqExtractorConfig
            {
                ThrowOnMissing = false,
                AllowPartialExtraction = true,
                DiagnosticsEnabled = true,
                DiagnosticsDirectory = "diag",
                ReferenceResolution = new OpenCvSharp.Size(1936, 1048),
                BadgeMinSolidity = 0.75,
                BadgeSaturationIntensityMinMean = 180,
                BadgeSaturationIntensityMaxMean = 40,
                BadgeMinWidth = 60,
                BadgeMaxWidth = 105,
                BadgeMinHeight = 15,
                BadgeMaxHeight = 90,
            };

            string tessDataPath = @"D:\Development\Tesseract";
            //string filePath = Path.Combine(@"D:\Development\Last Z\Last Z Client-o-matic\Last Z Client-o-matic\Tests", "world_1936x1048_81.79a.png");
            //var result = ProcessImage(config, tessDataPath, loggerFactory, filePath);
            //return;
            DirectoryInfo di = new DirectoryInfo(@"D:\Development\Last Z\Last Z Client-o-matic\Last Z Client-o-matic\Tests");
            var testFiles = di.GetFiles("*.png");
            testFiles.ToList()
                .ForEach(filePath =>
                {
                    var result = ProcessImage(config, tessDataPath, loggerFactory, filePath.FullName);
                    
                });
            

            return;
        }

        static HqExtractionResult ProcessImage(HqExtractorConfig config, string tessDataPath, ILoggerFactory loggerFactory, string filePath)
        {
            TemplateImageFile template = new TemplateImageFile("Test Template", filePath, ImageFeature.None);

            using var extractor = new HqTemplateExtractor(config, tessDataPath, loggerFactory);

            Console.WriteLine($"Processing image: {Path.GetFileName(template.Path)}");
            var result = extractor.ExtractFromFile(template.Path);

            // Visualize and save
            using var img = Cv2.ImRead(template.Path, ImreadModes.Color);
            var (annotated, badgeMask, nameplateMask) = Visualizer.VisualizeAndSave(img, result, config, $"{Path.GetFileName(template.Path)}_hq_crop");

            Console.WriteLine($"Annotated: {annotated}");
            Console.WriteLine($"Badge mask: {badgeMask}");
            return result;
        }
        static string GetExtractedOutputPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TemplatesDirectory, "extracted");
        }

        static void TestHQTemplateExtractor(TemplateImageFile imagePath)
        {

            var extractor = new LegacyHQTemplateExtractor();

            var parts = extractor.Extract(imagePath.Path);

            extractor.SaveHQParts(parts, GetExtractedOutputPath(), "hq1");
            Console.WriteLine($"Extracted");

        }
    }
}
