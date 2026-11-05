using OpenCvSharp;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text.Json;
using Size = OpenCvSharp.Size;

namespace WindowsMCP_Integrated
{
    internal sealed class ImageProcessing : IDisposable
    {
        
        private readonly Action<string>? _debugCallback;
        private readonly List<ImageProcessingStage> _stages = [];
        private Mat? _image;
        private Mat? _originalImage;
        private string? _originalFilePath;
        public static bool? SaveStageFiles;
        public ImageProcessing(Action<string>? debugCallback = null)
        {
            _debugCallback = debugCallback;
        }

        public static string PostProcessingOutputFilePath { get; set; } = Path.Combine(AppContext.BaseDirectory, "ImageProcessingOutput");

        public IReadOnlyList<ImageProcessingStage> Stages => _stages;

        public string? StageParametersJson { get; private set; }
        public static Func<string, string> StageCompletionCallback { get; internal set; }
        public static void SetConfiguration(string screenshotOutputPath)
        {
            ImageProcessing.PostProcessingOutputFilePath = Path.Combine(screenshotOutputPath, "Postprocessing");
            //ImageProcessing.SaveStageFiles = true;
        }

        public ImageProcessing ReadFile(string filePath, ImreadModes mode, bool saveOutput = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            var image = Cv2.ImRead(filePath, mode);
            if (image.Empty())
            {
                image.Dispose();
                throw new InvalidOperationException($"Could not read image file '{filePath}'.");
            }

            _image?.Dispose();
            _originalImage?.Dispose();
            _image = image;
            _originalImage = image.Clone();
            _originalFilePath = filePath;

            CompleteStage("Read File", new { FilePath = filePath, Mode = mode }, saveOutput);
            return this;
        }

        // Default normalization is grayscale conversion, but you can specify other color conversions if needed.
        public ImageProcessing Normalize(ColorConversionCodes transformation = ColorConversionCodes.BGR2GRAY, bool saveOutput = false)
        {
            var conversionCodes = new HashSet<ColorConversionCodes>
            {
                transformation,
                //ColorConversionCodes.BGR2HSV,
                //ColorConversionCodes.BGR2Lab,
                //ColorConversionCodes.BGR2YCrCb,
                //ColorConversionCodes.BGR2Luv,
                //ColorConversionCodes.BGR2HLS,
                //ColorConversionCodes.BGR2XYZ,
                //ColorConversionCodes.BGR2YUV,
            };
            var originalImage = RequireImage().Clone();
            for (int i = 0; i < conversionCodes.Count; i++)
            {
                try
                {
                    ReplaceImage(originalImage.Clone());
                    NormalizeCore(conversionCodes.ElementAt(i), saveOutput);
                }
                catch (Exception)
                {

                    Console.WriteLine($"Normalization failed for color conversion: {conversionCodes.ElementAt(i)}. Continuing with next conversion.");
                }
            }
            originalImage?.Dispose();
            return this;
        }
        public ImageProcessing NormalizeCore(ColorConversionCodes transformation, bool saveOutput = false)
        {
            var source = RequireImage();
            var normalized = new Mat();
            Cv2.CvtColor(source, normalized, transformation);
            ReplaceImage(normalized);

            CompleteStage($"Normalizing - color conversion({transformation})", new { GrayscaleTransformation = transformation }, saveOutput);
            return this;
        }

        public ImageProcessing ReduceNoise(Size? kernelSize = null, double sigmaX = 0, bool saveOutput = false)
        {
            var source = RequireImage();
            var blurKernel = kernelSize ?? new Size(3, 3);
            ValidateOddKernel(blurKernel, nameof(kernelSize));

            var denoised = new Mat();
            Cv2.GaussianBlur(source, denoised, blurKernel, sigmaX);
            ReplaceImage(denoised);

            CompleteStage($"Reduce Noise - kernelSize ({blurKernel}), sigx ({sigmaX})", new { KernelSize = blurKernel, SigmaX = sigmaX }, saveOutput);
            return this;
        }

        public ImageProcessing CreateBinaryMask(
            double threshold = 0,
            double maxValue = 255,
            ThresholdTypes thresholdType = ThresholdTypes.BinaryInv,
            bool saveOutput = false)
        {
            CreateBinaryMaskCore(threshold, 255, thresholdType, saveOutput);
            return this;
            for (int i = 100; i < 200; i += 25)
            {
                CreateBinaryMaskCore(i, maxValue, thresholdType, saveOutput);
            }
            return this;
        }

        public ImageProcessing CreateBinaryMaskCore(
            double threshold = 0,
            double maxValue = 255,
            ThresholdTypes thresholdType = ThresholdTypes.Binary,
            bool saveOutput = false)
        {
            var source = RequireImage();
            var mask = new Mat();
            
            Cv2.Threshold(source, mask, threshold, maxValue, thresholdType);
            ReplaceImage(mask);

            CompleteStage($"Create Binary Mask - threshold ({threshold}), type({thresholdType})", new
            {
                Threshold = threshold,
                MaxValue = maxValue,
                ThresholdType = thresholdType
            }, saveOutput);
            return this;
        }

        public ImageProcessing CleanBinaryMask(
            MorphTypes operation = MorphTypes.Open,
            Size? kernelSize = null,
            int iterations = 1,
            bool saveOutput = false)
        {
            var source = RequireImage();
            var morphologyKernelSize = kernelSize ?? new Size(3, 3);
            ValidateOddKernel(morphologyKernelSize, nameof(kernelSize));
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterations);

            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, morphologyKernelSize);
            var cleaned = new Mat();
            Cv2.MorphologyEx(source, cleaned, operation, kernel, iterations: iterations);
            ReplaceImage(cleaned);

            CompleteStage($"Clean Binary Mask - operation ({operation}), kernSize ({morphologyKernelSize}), iterations ({iterations})", new
            {
                Operation = operation,
                KernelSize = morphologyKernelSize,
                Iterations = iterations
            }, saveOutput);
            return this;
        }

        public ImageProcessing ExtractAndFilterObjects(
            double minimumArea = 10,
            double maximumArea = double.MaxValue,
            Scalar? annotationColor = null,
            int lineThickness = 2,
            bool saveOutput = false)
        {
            var mask = RequireImage();
            ArgumentOutOfRangeException.ThrowIfNegative(minimumArea);
            if (maximumArea < minimumArea)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumArea), "Maximum area must be at least the minimum area.");
            }

            var original = _originalImage ?? throw new InvalidOperationException("ReadFile must be called before extracting objects.");
            var contours = Cv2.FindContoursAsArray(mask, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            var annotated = CreateAnnotationImage(original);
            var color = annotationColor ?? new Scalar(0, 255, 0);
            var acceptedObjects = new List<object>();

            foreach (var contour in contours)
            {
                var area = Cv2.ContourArea(contour);
                if (area < minimumArea || area > maximumArea)
                {
                    continue;
                }

                var bounds = Cv2.BoundingRect(contour);
                Cv2.DrawContours(annotated, [contour], -1, color, lineThickness);
                Cv2.Rectangle(annotated, bounds, color, lineThickness);
                acceptedObjects.Add(new { Area = area, Bounds = bounds });
            }

            ReplaceImage(annotated);
            CompleteStage("Extract and Filter Objects", new
            {
                MinimumArea = minimumArea,
                MaximumArea = maximumArea,
                AnnotationColor = color,
                LineThickness = lineThickness,
                AcceptedObjectCount = acceptedObjects.Count,
                Objects = acceptedObjects
            }, saveOutput);
            return this;
        }
        public ImageProcessing PadEdges(bool saveOutput = false)
        {
            var source = RequireImage();
            var padding = new Mat();
            Cv2.CopyMakeBorder(source, padding, 20, 20, 20, 20, BorderTypes.Constant, Scalar.White);
            ReplaceImage(padding);

            CompleteStage($"Add Padding - 20x20x20x20, border type: ", new
            {
                BorderTypes = BorderTypes.Constant,
                Scalar = Scalar.White
            }, saveOutput);
            return this;
        }
        public ImageProcessing Upscale(
            double scaleFactor = 3.0,
            InterpolationFlags interpolation = InterpolationFlags.Cubic,
            bool saveOutput = false)
        {
            if (scaleFactor <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scaleFactor), "Scale factor must be positive.");
            }
            var source = RequireImage();
            var upscaled = new Mat();
            Cv2.Resize(source, upscaled, new Size(), scaleFactor, scaleFactor, interpolation);
            ReplaceImage(upscaled);
            CompleteStage($"Upscale - scaleFactor ({scaleFactor}), interpolation ({interpolation})", new
            {
                ScaleFactor = scaleFactor,
                Interpolation = interpolation
            }, saveOutput);
            return this;
        }

        public static string GetStageFileName(string originalFilename, string stageName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(originalFilename);
            ArgumentException.ThrowIfNullOrWhiteSpace(stageName);
            if (!Directory.Exists(PostProcessingOutputFilePath))
            {
                Directory.CreateDirectory(PostProcessingOutputFilePath);
            }
            var extension = Path.GetExtension(originalFilename);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".png";
            }

            var filename = Path.GetFileNameWithoutExtension(originalFilename);
            var safeStageName = string.Concat(stageName.Select(character =>
                Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
            return Path.Combine(PostProcessingOutputFilePath, $"{filename}_{safeStageName}{extension}");
        }

        public void Dispose()
        {
            _image?.Dispose();
            _originalImage?.Dispose();
            _image = null;
            _originalImage = null;
        }

        private void CompleteStage(string stageName, object parameters, bool saveOutput)
        {
            var parameterJson = JsonSerializer.Serialize(new { StageName = stageName, Parameters = parameters });
            StageParametersJson = parameterJson;
            LogDebug("Image stage {0} parameters: {1}", stageName, parameterJson);

            string? outputPath = null;
            if (saveOutput || (SaveStageFiles.HasValue && SaveStageFiles.Value))
            {
                var originalFilePath = _originalFilePath ?? throw new InvalidOperationException("ReadFile must be called before saving a stage.");
                outputPath = GetStageFileName($"{originalFilePath}", stageName);
                if(File.Exists(outputPath))
                {
                    outputPath = GetStageFileName($"{_stages.Count} - {Random.Shared.Next(10,1000).ToString()}-{originalFilePath}_{stageName}", stageName);
                }
                if (!Cv2.ImWrite(outputPath, RequireImage()))
                {
                    throw new IOException($"Could not write image file '{outputPath}'.");
                }
                if(StageCompletionCallback != null)
                {
                    var callbackResult = StageCompletionCallback(outputPath);
                    LogDebug("\tStage {0} saved to {1}. Callback result:\r\n\t\t{2}", stageName, outputPath, callbackResult);
                }
            }

            _stages.Add(new ImageProcessingStage(stageName, parameterJson, outputPath));
        }

        private void LogDebug(string message, params object?[] parameters)
        {
            _debugCallback?.Invoke(string.Format(CultureInfo.InvariantCulture, message, parameters));
        }

        private Mat RequireImage() => _image ?? throw new InvalidOperationException("ReadFile must be called before processing an image.");

        private void ReplaceImage(Mat replacement)
        {
            _image?.Dispose();
            _image = replacement;
        }

        private static Mat CreateAnnotationImage(Mat original)
        {
            var annotated = new Mat();
            switch (original.Channels())
            {
                case 1:
                    Cv2.CvtColor(original, annotated, ColorConversionCodes.GRAY2BGR);
                    break;
                case 4:
                    Cv2.CvtColor(original, annotated, ColorConversionCodes.BGRA2BGR);
                    break;
                default:
                    original.CopyTo(annotated);
                    break;
            }

            return annotated;
        }
        public static void ExtractRegionFromImageFile(string inputFilePath, string outputFilePath, Rect region)
        {
            using var image = Cv2.ImRead(inputFilePath);
            if (image.Empty())
            {
                throw new InvalidOperationException($"Could not read image file '{inputFilePath}'.");
            }
            var extractedRegion = new Mat(image, region);
            if (!Cv2.ImWrite(outputFilePath, extractedRegion))
            {
                throw new IOException($"Could not write image file '{outputFilePath}'.");
            }
        }

        public static double CalculateImageSimilarity(string image1Path, string image2Path)
        {
            using var image1 = Cv2.ImRead(image1Path);
            if (image1.Empty())
            {
                throw new InvalidOperationException($"Could not read image file '{image1Path}'.");
            }
            using var image2 = Cv2.ImRead(image2Path);
            if (image2.Empty())
            {
                throw new InvalidOperationException($"Could not read image file '{image2Path}'.");
            }
            if (image1.Size() != image2.Size() || image1.Type() != image2.Type())
            {
                throw new ArgumentException("Images must have the same size and type for similarity calculation.");
            }
            using var diff = new Mat();
            Cv2.Absdiff(image1, image2, diff);
            Cv2.CvtColor(diff, diff, ColorConversionCodes.BGR2GRAY);
            double similarity = 1.0 - (Cv2.CountNonZero(diff) / (double)(diff.Rows * diff.Cols));

            /*
                             Mat template = Cv2.ImRead(templateFilePath, ImreadModes.Color);
                Mat testImage = Cv2.ImRead(testFilePath, ImreadModes.Color);
                Mat result = new Mat();
                Cv2.MatchTemplate(testImage, template, result, TemplateMatchModes.CCoeffNormed);

                Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);
*/
            return similarity;
        }
        private static void ValidateOddKernel(Size kernelSize, string parameterName)
        {
            if (kernelSize.Width <= 0 || kernelSize.Height <= 0 || kernelSize.Width % 2 == 0 || kernelSize.Height % 2 == 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, "Kernel dimensions must be positive odd numbers.");
            }
        }

        public bool SaveFileAs(string filePath)
        {
            var histogram = new Mat();
            Cv2.CalcHist(images: [RequireImage()], channels: [0], mask: default, hist: histogram, dims: 1, histSize: [256], ranges: [new Rangef(0, 256)]);
            if (!Cv2.ImWrite(filePath, RequireImage()))
            {
                throw new IOException($"Could not write image file '{filePath}'.");
            }
            histogram.Dispose();
            return true;
        }


    }

    internal sealed record ImageProcessingStage(string StageName, string ParametersJson, string? OutputFilePath);
}