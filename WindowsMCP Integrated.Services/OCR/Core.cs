using System;
using System.Collections.Generic;
using System.Text;

namespace WindowsMCP_Integrated.OCR;

using NLog;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using System.Text.RegularExpressions;
using TesseractOCR;
using TesseractOCR.Enums;

public enum BinarizeMode
{
    Auto,       // grayscale + Otsu, polarity auto-corrected (gray text, green text, etc.)
    WhiteFill,  // white text with dark outline (VS rank names and scores)
    DarkText    // dark, low-saturation text; colored icons are masked out
}

public sealed record OcrOptions(
    BinarizeMode Mode = BinarizeMode.Auto,
    string? Whitelist = null,
    PageSegMode Psm = PageSegMode.SingleLine);

public sealed class TextBox
{
    public OpenCvSharp.Rect Bounds { get; init; }
    public float Confidence { get; init; }
    public double CenterX => Bounds.X + Bounds.Width / 2.0;
    public double CenterY => Bounds.Y + Bounds.Height / 2.0;
    public override string ToString() => Bounds.ToString();
}

public interface ITableParser<T>
{
    IReadOnlyList<T> Parse(TableContext ctx);
    string[] GetCsvHeaders();
}

public sealed class TableContext
{
    private readonly Mat _image;
    private readonly FieldOcr _ocr;
    private readonly Dictionary<TextBox, string> _labels = new();

    internal TableContext(Mat image, IReadOnlyList<TextBox> boxes, FieldOcr ocr)
    {
        _image = image;
        Boxes = boxes;
        _ocr = ocr;
    }

    public IReadOnlyList<TextBox> Boxes { get; }
    public int Width => _image.Width;
    public int Height => _image.Height;
    public IReadOnlyDictionary<TextBox, string> Labels => _labels;

    public string Ocr(TextBox box, OcrOptions options) => _ocr.Read(_image, box.Bounds, options);

    /// Shows up in the debug image.
    public void Label(TextBox box, string label) => _labels[box] = label;

    /// True if the box touches an image edge, meaning the text may be cut off.
    public bool IsClipped(TextBox b, int margin = 4) =>
        b.Bounds.Top <= margin || b.Bounds.Bottom >= Height - margin ||
        b.Bounds.Left <= margin || b.Bounds.Right >= Width - margin;
}

public static class TextCleaning
{
    public const string Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    public const string DigitChars = "0123456789";
    public const string NameChars = Letters + DigitChars + " ?";

    public static string Name(string raw, string allowedPattern = @"A-Za-z0-9 ?")
    {
        string s = Regex.Replace(raw, $"[^{allowedPattern}]", "");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    public static string DigitsOnly(string raw)
    {
        string s = raw.Replace('O', '0').Replace('o', '0').Replace('l', '1')
                      .Replace('I', '1').Replace('|', '1').Replace('S', '5').Replace('B', '8');
        return Regex.Replace(s, @"\D", "");
    }

    public static string NoSpaces(string raw) => Regex.Replace(raw, @"\s+", "");
    public static string Compact(string raw) => Regex.Replace(raw, @"\s+", " ").Trim();

    /// The candidate whose CenterY is inside [top, bottom] and closest to targetY.
    public static TextBox? ClosestInBand(IEnumerable<TextBox> candidates, double top, double bottom, double targetY) =>
        candidates.Where(c => c.CenterY >= top && c.CenterY <= bottom)
                  .OrderBy(c => Math.Abs(c.CenterY - targetY))
                  .FirstOrDefault();
}

internal sealed class TextBoxDetector : IDisposable
{
    private readonly TextDetectionModelDB _model;

    public TextBoxDetector(TextDetectionModelDB model) => _model = model;

    public List<TextBox> Detect(Mat image)
    {
        _model.SetInputParams(
            scale: 1.0 / 255.0,
            size: new Size((image.Width + 31) / 32 * 32, (image.Height + 31) / 32 * 32),
            mean: new Scalar(122.67891434, 116.66876762, 104.00698793),
            swapRB: false);

        _model.DetectTextRectangles(image, out RotatedRect[] rects, out float[] confidences);

        return rects.Select((r, i) => new TextBox
        {
            Bounds = r.BoundingRect(),   // these tables are axis-aligned
            Confidence = confidences[i]
        }).ToList();
    }

    public void Dispose() => _model.Dispose();
}

internal sealed class FieldOcr : IDisposable
{
    private readonly Engine _engine;

    public FieldOcr(Engine? engine = null) =>
        _engine = engine ?? TesseractService.GetEngine();

    public string Read(Mat image, OpenCvSharp.Rect bounds, OcrOptions options)
    {
        OpenCvSharp.Rect roi = OpenCvSharp.Rect.Intersect(
            new OpenCvSharp.Rect(bounds.X - 6, bounds.Y - 6, bounds.Width + 12, bounds.Height + 12),
            new OpenCvSharp.Rect(0, 0, image.Width, image.Height));
        if (roi.Width < 2 || roi.Height < 2) return string.Empty;

        using var crop = new Mat(image, roi);
        using var bin = Binarize(crop, options.Mode);
        using var padded = new Mat();
        Cv2.CopyMakeBorder(bin, padded, 12, 12, 12, 12, BorderTypes.Constant, Scalar.White);

        Cv2.ImEncode(".png", padded, out byte[] png);
        using var img = TesseractOCR.Pix.Image.LoadFromMemory(png);

        // Always set it (empty = no restriction) so a previous whitelist doesn't linger
        _engine.SetVariable("tessedit_char_whitelist", options.Whitelist ?? "");
        using var page = _engine.Process(img, options.Psm);
        return page.Text.Trim();
    }

    /// Produces black text on a white background.
    private static Mat Binarize(Mat crop, BinarizeMode mode)
    {
        var result = new Mat();

        switch (mode)
        {
            case BinarizeMode.WhiteFill:
                {
                    // Only the white fill is bright in ALL channels: min(B,G,R) > threshold
                    Cv2.Split(crop, out Mat[] ch);
                    using var minBg = new Mat();
                    using var minAll = new Mat();
                    Cv2.Min(ch[0], ch[1], minBg);
                    Cv2.Min(minBg, ch[2], minAll);
                    foreach (var c in ch) c.Dispose();
                    Cv2.Threshold(minAll, result, 230, 255, ThresholdTypes.BinaryInv);
                    break;
                }
            case BinarizeMode.DarkText:
                {
                    // Dark AND unsaturated. Colored icons (gender symbols, shields) fail the saturation test.
                    using var hsv = new Mat();
                    using var mask = new Mat();
                    Cv2.CvtColor(crop, hsv, ColorConversionCodes.BGR2HSV);
                    Cv2.InRange(hsv, new Scalar(0, 0, 0), new Scalar(180, 130, 140), mask);
                    Cv2.BitwiseNot(mask, result);
                    break;
                }
            default:
                {
                    using var gray = new Mat();
                    Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
                    Cv2.Threshold(gray, result, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
                    if (Cv2.Mean(result).Val0 < 127) Cv2.BitwiseNot(result, result);
                    break;
                }
        }
        return result;
    }

    public void Dispose() => _engine.Dispose();
}

public sealed class TableReader : IDisposable
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly TextBoxDetector _detector;
    private readonly FieldOcr _ocr;

    public double ImageScale { get; init; } = 2.0;

    public TableReader(TextDetectionModelDB detectionModelDB)
    {
        _detector = new TextBoxDetector(detectionModelDB);
        _ocr = new FieldOcr();
    }

    public IReadOnlyList<T> Read<T>(string imagePath, ITableParser<T> parser, string? debugImagePath = null)
    {
        using var original = Cv2.ImRead(imagePath, ImreadModes.Color);
        if (original.Empty())
        {
            _logger.Error($"Could not load image: {imagePath}");
            return Array.Empty<T>();
        }

        using var image = new Mat();
        Cv2.Resize(original, image, new Size(), ImageScale, ImageScale, InterpolationFlags.Cubic);

        var boxes = _detector.Detect(image);
        for (int i = 0; i < boxes.Count; i++)
            _logger.Trace($"Detected {i}: rect={boxes[i].Bounds} detConfidence={boxes[i].Confidence:F3}");

        var ctx = new TableContext(image, boxes, _ocr);
        var results = parser.Parse(ctx);

        if (debugImagePath != null)
            SaveDebugImage(image, ctx, debugImagePath);

        return results;
    }

    private static void SaveDebugImage(Mat image, TableContext ctx, string path)
    {
        using var canvas = image.Clone();
        foreach (var b in ctx.Boxes)
        {
            bool labeled = ctx.Labels.TryGetValue(b, out string? label) && label != "Other";
            var color = labeled ? new Scalar(0, 160, 0) : new Scalar(160, 160, 160);
            Cv2.Rectangle(canvas, b.Bounds, color, 2);
            if (labeled)
                Cv2.PutText(canvas, label!, new Point(b.Bounds.X, Math.Max(12, b.Bounds.Y - 3)),
                    HersheyFonts.HersheySimplex, 0.4, color, 1);
        }

        StringBuilder outputFilePath = new StringBuilder(path);
        if (String.IsNullOrEmpty(Path.GetExtension(path)))
        {
            outputFilePath.Append(".png");
        }
        Cv2.ImWrite(outputFilePath.ToString(), canvas);
    }

    public void Dispose()
    {
        _detector.Dispose();
        _ocr.Dispose();
    }
}
