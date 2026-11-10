using NLog;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using OpenCvSharp.DnnSuperres;
using OpenCvSharp.WpfExtensions;
using System.Globalization;
using System.Text.Json;
using TesseractOCR;
using TesseractOCR.Enums;
using WindowsMCP_Integrated.Core;
using WindowsMCP_Integrated.OCR;
using Size = OpenCvSharp.Size;

namespace WindowsMCP_Integrated
{
    public sealed class ImageProcessing : IDisposable
    {

        private readonly ILogger _logger = NLog.LogManager.GetCurrentClassLogger();
        private readonly List<ImageProcessingStage> _stages = [];
        private Mat? _image;
        private Mat? _originalImage;
        private string? _originalFilePath;
        public static bool? SaveStageFiles;
        private readonly TesseractService _tesseractService;
        public ImageProcessing(TesseractService? tesseractService = null)
        {
            _tesseractService = tesseractService ?? new TesseractService();
        }
        public static string ModelFileName = @"D:\Development\Last Z\Last Z Client-o-matic\WindowsMCP Integrated.Services\OCR\FSRCNN_x4.pb";
        public static string textDetectModelPath = @"D:\Development\Last Z\Last Z Client-o-matic\WindowsMCP Integrated.Services\OCR\text_detection_en_ppocrv3_2023may.onnx";
        public static string textRecModelPath = @"D:\Development\Last Z\Last Z Client-o-matic\WindowsMCP Integrated.Services\OCR\text_recognition_CRNN_EN_2021sep.onnx";
        public static string vocab = @"D:\Development\Last Z\Last Z Client-o-matic\WindowsMCP Integrated.Services\OCR\alphabet_36.txt";
        private TextDetectionModelDB textDetectionModel;
        private TextRecognitionModel _textRecognitionModel;

        private void EnsureModelLoaded()
        {
            if (textDetectionModel is null)
            {
                textDetectionModel = new TextDetectionModelDB(textDetectModelPath, "");
                textDetectionModel.SetBinaryThreshold(0.3f);
                textDetectionModel.SetPolygonThreshold(0.5f);
                textDetectionModel.SetUnclipRatio(2.0);
                textDetectionModel.SetMaxCandidates(200);
                textDetectionModel.SetInputSize(new Size(736, 736));
                // Matches OpenCV Zoo's own ppocr_det.py exactly, including the (unusual but
                // intentional) lack of SwapRB - the mean/scale triple is applied directly to OpenCV's
                // BGR channel order, not RGB.
                textDetectionModel.SetInputMean(new Scalar(122.67891434, 116.66876762, 104.00698793));
                textDetectionModel.SetInputScale(new Scalar(1.0 / 255.0));
                textDetectionModel.SetInputSwapRB(false);
            }

            if (_textRecognitionModel is null)
            {
                _textRecognitionModel = new TextRecognitionModel(textRecModelPath);
                _textRecognitionModel.SetVocabulary(File.ReadAllLines(vocab));
                _textRecognitionModel.SetInputScale(new Scalar(1.0 / 127.5));
                _textRecognitionModel.SetInputMean(new Scalar(127.5, 127.5, 127.5));
                _textRecognitionModel.SetInputSize(new OpenCvSharp.Size(100, 32));
                _textRecognitionModel.SetDecodeType("CTC-greedy");
            }

        }

        public List<T> ProcessTableImageDirectoryByTextModel<T>(string source, string debugImagePath, ITableParser<T> parser)
        {
            EnsureModelLoaded();
            using var tableReader = new TableReader(textDetectionModel);

            var files = Directory.GetFiles(source, "*.png");
            var rows = new List<T>();
            int rowChange;
            foreach (var file in files)
            {
                rowChange = rows.Count;
                rows.AddRange(tableReader.Read(imagePath: file,
                                               parser: parser,
                                               debugImagePath: Path.Combine(Path.GetDirectoryName(debugImagePath)
                                               , Helpers.ModifyFileName(debugImagePath, "", " table"))));
                _logger.Trace("Table builder added {0} rows", rows.Count - rowChange);
            }

            return rows;
        }
        public bool ProcessImageWithTextModel(string source, string destination)
        {
            EnsureModelLoaded();
            var tableReader = new TableReader(textDetectionModel);
            var vsRanks = tableReader.Read(source, new MemberVSRankParser(), Path.Combine(Path.GetDirectoryName(destination), "debug", Helpers.ModifyFileName(destination, "", " table")));

            vsRanks.ToList().ForEach(itm => _logger.Trace(itm));
            return true;
        }
        public string DetectTextDebugImage(string source, string destination)
        {
            EnsureModelLoaded();

            var src = new Mat(source);
            using var dst = src.Clone();
            //textDetectionModel.Detect(src, out OpenCvSharp.Point[][] polygons, out var confidences);
            textDetectionModel.DetectTextRectangles(src, out OpenCvSharp.RotatedRect[] rectangles, out var confidences);
            int rectangleCounter = 0;
            var orderedRect = rectangles
                .OrderBy(t => Math.Round(t.BoundingRect().Right / 5.0) * 5)
                .ToArray();
            for (int i = 0; i < orderedRect.Length; i++)
            {
                RotatedRect rectangle = orderedRect[i];
                Cv2.Rectangle(dst, rectangle.BoundingRect(), Scalar.BlueViolet, 1, LineTypes.Link4);
                Cv2.PutText(dst, $"{rectangle.BoundingRect().X}, {rectangle.BoundingRect().Y} center: {(rectangle.BoundingRect().Width / 2) + rectangle.BoundingRect().X}", new OpenCvSharp.Point(rectangle.BoundingRect().X, rectangle.BoundingRect().Y),
                HersheyFonts.HersheySimplex, 1, new Scalar(0, 255, 0));
                var (text, confidence) = RecognizeRegionTesseract(src, rectangle.Points());

                _logger.Trace($"{Math.Round(rectangle.BoundingRect().Right / 5.0) * 5},{rectangle.BoundingRect().Left},{rectangle.BoundingRect().Top},{rectangle.BoundingRect().Right},{rectangle.BoundingRect().Bottom},{rectangle.Center},\"{text}\",{confidence}");
                rectangleCounter++;
            }
            Cv2.ImWrite(destination, dst);
            _logger.Trace($"TextModel Processed file written: '{destination}");
            return destination;
        }

        private (string text, float confidence) RecognizeRegionTesseract(Mat image, OpenCvSharp.Point2f[] corners)
        {
            // Order: bottom-left, top-left, top-right, bottom-right
            int height = (int)Math.Round(Distance(corners[0], corners[1]));
            int width = (int)Math.Round(Distance(corners[1], corners[2]));
            if (width < 2 || height < 2)
                return (string.Empty, 0f);

            var dst = new[]
            {
            new Point2f(0, height - 1),
            new Point2f(0, 0),
            new Point2f(width - 1, 0),
            new Point2f(width - 1, height - 1)
        };

            using var transform = Cv2.GetPerspectiveTransform(corners, dst);
            using var cropped = new Mat();
            Cv2.WarpPerspective(image, cropped, transform, new Size(width, height),
                InterpolationFlags.Cubic, BorderTypes.Replicate);

            // Tesseract does best with text around 30-40 px tall; upscale small crops
            Mat prepared = cropped;
            Mat? scaled = null;
            if (height < 32)
            {
                double scale = 32.0 / height;
                scaled = new Mat();
                Cv2.Resize(cropped, scaled, new Size(), scale, scale, InterpolationFlags.Cubic);
                prepared = scaled;
            }

            using var gray = new Mat();
            Cv2.CvtColor(prepared, gray, ColorConversionCodes.BGR2GRAY);

            // A small white border helps Tesseract with text touching the edges
            using var padded = new Mat();
            Cv2.CopyMakeBorder(gray, padded, 10, 10, 10, 10, BorderTypes.Constant, Scalar.White);

            scaled?.Dispose();

            // Hand the crop to Tesseract as PNG bytes
            Cv2.ImEncode(".png", padded, out byte[] png);
            using var img = TesseractOCR.Pix.Image.LoadFromMemory(png);

            // The crop is a single line of text
            Cv2.ImWrite("", padded);


            return _tesseractService.ProcessImage(img);
        }

        private static double Distance(Point2f a, Point2f b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private string RecognizeRegion(Mat image, OpenCvSharp.Point2f[] quad)
        {
            // DB returns points as: bottom-left, top-left, top-right, bottom-right
            var src = quad.Select(p => new Point2f(p.X, p.Y)).ToArray();
            var dst = new[]
            {
            new Point2f(0, 31),
            new Point2f(0, 0),
            new Point2f(99, 0),
            new Point2f(99, 31)
        };

            using var transform = Cv2.GetPerspectiveTransform(src, dst);
            using var cropped = new Mat();
            Cv2.WarpPerspective(image, cropped, transform, new Size(100, 32));

            // The CRNN models expect grayscale input
            if (cropped.Channels() > 1)
            {
                using var gray = new Mat();
                Cv2.CvtColor(cropped, gray, ColorConversionCodes.BGR2GRAY);
                return _textRecognitionModel.Recognize(gray);
            }
            return _textRecognitionModel.Recognize(cropped);
        }

        public bool ProcessImageWithDnn(string source, string destination)
        {
            using var dnn = new DnnSuperResImpl("fsrcnn", 4);
            dnn.ReadModel(ModelFileName);

            using var src = new Mat(source, ImreadModes.Color);
            using var dst = new Mat();
            dnn.Upsample(src, dst);
            Cv2.ImWrite(destination, dst);
            _logger.Trace($"DNN Processed file written: '{destination}");
            return true;
        }
        public static string PostProcessingOutputFilePath { get; set; } = System.IO.Path.Combine(AppContext.BaseDirectory, "ImageProcessingOutput");

        public IReadOnlyList<ImageProcessingStage> Stages => _stages;

        public string? StageParametersJson { get; private set; }
        public static Func<string, string> StageCompletionCallback { get; internal set; }
        public static void SetConfiguration(string screenshotOutputPath)
        {
            ImageProcessing.PostProcessingOutputFilePath = System.IO.Path.Combine(screenshotOutputPath, "Postprocessing");
            //ImageProcessing.SaveStageFiles = true;
        }

        public ImageProcessing ReadFile(string filePath, ImreadModes mode, bool saveOutput = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            var image = Cv2.ImRead(filePath, mode);
            SetNewFile(filePath, image);

            CompleteStage("Read File", new { FilePath = filePath, Mode = mode }, saveOutput);
            return this;
        }

        private void SetNewFile(string filePath, Mat image)
        {
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

                    _logger.Error($"Normalization failed for color conversion: {conversionCodes.ElementAt(i)}. Continuing with next conversion.");
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
            var extension = System.IO.Path.GetExtension(originalFilename);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".png";
            }

            var filename = System.IO.Path.GetFileNameWithoutExtension(originalFilename);
            var safeStageName = string.Concat(stageName.Select(character =>
                System.IO.Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
            return System.IO.Path.Combine(PostProcessingOutputFilePath, $"{filename}_{safeStageName}{extension}");
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
                if (File.Exists(outputPath))
                {
                    outputPath = GetStageFileName($"{_stages.Count} - {Random.Shared.Next(10, 1000).ToString()}-{originalFilePath}_{stageName}", stageName);
                }
                if (!Cv2.ImWrite(outputPath, RequireImage()))
                {
                    throw new IOException($"Could not write image file '{outputPath}'.");
                }
                if (StageCompletionCallback != null)
                {
                    var callbackResult = StageCompletionCallback(outputPath);
                    LogDebug("\tStage {0} saved to {1}. Callback result:\r\n\t\t{2}", stageName, outputPath, callbackResult);
                }
            }

            _stages.Add(new ImageProcessingStage(stageName, parameterJson, outputPath));
        }

        private void LogDebug(string message, params object?[] parameters)
        {
            _logger.Debug(string.Format(CultureInfo.InvariantCulture, message, parameters));
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
        public static void ExtractRegionFromImageFile(string inputFilePath, string outputFilePath, OpenCvSharp.Rect region)
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

    public sealed record ImageProcessingStage(string StageName, string ParametersJson, string? OutputFilePath);
}