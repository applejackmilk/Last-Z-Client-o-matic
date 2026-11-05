using HqExtractorLib.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Tesseract;
using CvRect = OpenCvSharp.Rect;
using TessRect = Tesseract.Rect;

/// <summary>
/// Badge-first HQ extractor that finds badge, then nameplate, then extracts level and username.
/// </summary>
public sealed class HqTemplateExtractor : IDisposable
{
    private readonly HqExtractorConfig _config;
    private readonly ILogger _logger;
    private readonly TesseractEngine _tesseractDigits;
    private readonly TesseractEngine _tesseractNameplate;
    /// <summary>
    /// Create an extractor.
    /// </summary>
    /// <param name="config">Configuration object.</param>
    /// <param name="logger">ILogger instance; pass NullLogger.Instance if none available.</param>
    /// <param name="tessDataPath">Path to Tesseract tessdata folder.</param>
    public HqTemplateExtractor(HqExtractorConfig config, string tessDataPath = @"D:\Development\Tesseract", Microsoft.Extensions.Logging.ILoggerFactory loggerFactory = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = loggerFactory.CreateLogger<HqTemplateExtractor>();

        // Tesseract for digits (badge)
        _tesseractDigits = new TesseractEngine(tessDataPath, "eng", EngineMode.Default);
        _tesseractDigits.SetVariable("tessedit_char_whitelist", "0123456789");
        _tesseractDigits.SetVariable("classify_bln_numeric_mode", "1");

        // Tesseract for nameplate (allow letters, digits, brackets, common symbols)
        _tesseractNameplate = new TesseractEngine(tessDataPath, "eng", EngineMode.Default);
        _tesseractNameplate.SetVariable("tessedit_char_whitelist", "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789[]_- ");
    }

    /// <summary>
    /// Extract badge level and username from an input image path.
    /// </summary>
    public HqExtractionResult ExtractFromFile(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            throw new ArgumentException("imagePath is required", nameof(imagePath));

        if (!File.Exists(imagePath))
            throw new FileNotFoundException("Input image not found", imagePath);

        using var img = Cv2.ImRead(imagePath, ImreadModes.Color);
        return Extract(img, Path.GetFileName(imagePath));
    }

    /// <summary>
    /// Main extraction entry point using HqRect in the result model.
    /// </summary>
    public HqExtractionResult Extract(Mat inputImage, string imageId = "image")
    {
        var result = new HqExtractionResult();
        try
        {
            using var img = NormalizeToReference(inputImage);
            using var hsv = ConvertToHsv(img);

            // 1. Find badge (returns CvRect internally, convert to HqRect)
            var badgeDiagnostics = FindBestBadgeCvWithDiagnostics(hsv);
            if (badgeDiagnostics.BestBadge == null)
            {
                LogAndRecord(result, "Badge not found", imageId, "BadgeDetection");
                return FinalizeResult(result);
            }

            var badgeHqRect = RectConversions.FromCvRect(badgeDiagnostics.BestBadge.Value);
            result.BadgeRect = badgeHqRect;

            // Save badge crop for diagnostics
            using (var badgeCrop = CropMat(img, badgeDiagnostics.BestBadge.Value))
                SaveDiagnosticIfEnabled(badgeCrop, imageId, "badge_crop");

            // 2. Recognize level from badge crop
            using (var badgeCropForOcr = CropMat(img, badgeDiagnostics.BestBadge.Value))
            {
                var (level, levelConfidence) = RecognizeBadgeLevel(badgeCropForOcr);
                result.Level = level;
                result.LevelConfidence = levelConfidence;

                if (!level.HasValue)
                    LogAndRecord(result, "Badge OCR failed or produced out-of-range level", imageId, "BadgeOCR");
            }

            BadgeCandidateReporter.WriteCandidateReport(imageId, mask: badgeDiagnostics.Mask, hsv, badgeDiagnostics.Candidates, _config.DiagnosticsDirectory, _logger);
            BadgeCandidateVisualizer.SaveOverlay(imageId, img, badgeDiagnostics.Candidates, _config.DiagnosticsDirectory, _logger);

            // 3. Find nameplate above badge (returns CvRect)
            var nameplateCvRect = FindNameplateAboveBadgeCv(hsv, badgeDiagnostics.BestBadge.Value, img.Width);
            if (nameplateCvRect == null)
            {
                LogAndRecord(result, "Nameplate not found above badge", imageId, "NameplateDetection");
                if (!_config.AllowPartialExtraction)
                    return FinalizeResult(result);
            }
            else
            {
                var nameplateHqRect = RectConversions.FromCvRect(nameplateCvRect.Value);
                result.NameplateRect = nameplateHqRect;

                using (var nameplateCrop = CropMat(img, nameplateCvRect.Value))
                    SaveDiagnosticIfEnabled(nameplateCrop, imageId, "nameplate_crop");

                // 4. Classify text color relationship
                result.Relationship = ClassifyNameplateTextColor(nameplateCvRect.Value, img, out var colorConfidence);
                _logger.LogDebug("Nameplate color classification: {rel} (conf {c:F2})", result.Relationship, colorConfidence);

                // 5. OCR nameplate for alliance and username
                using (var nameplateCrop = CropMat(img, nameplateCvRect.Value))
                {
                    var (alliance, username, usernameConfidence) = RecognizeNameplate(nameplateCrop);
                    result.AllianceTag = alliance;
                    result.Username = username;
                    result.UsernameConfidence = usernameConfidence;

                    if (string.IsNullOrWhiteSpace(username))
                        LogAndRecord(result, "Username OCR failed or low confidence", imageId, "NameplateOCR");
                }
            }

            result.Success = true;
            return FinalizeResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception during extraction");
            result.Errors.Add($"Unhandled exception: {ex.Message}");
            if (_config.ThrowOnMissing)
                throw;
            return FinalizeResult(result);
        }
    }

    #region Core small methods (CvRect internal)
    // Add these helper methods inside your extractor class
    private bool IsContourCandidate(
        Mat mask,
        Mat hsv,
        Point[] contour,
        int imageArea,
        HqExtractorConfig cfg,
        out string rejectReason,
        out double contourArea,
        out CvRect bbox)
    {
        rejectReason = null;
        contourArea = Cv2.ContourArea(contour);
        bbox = Cv2.BoundingRect(contour);

        // --- Area filters ---
        if (contourArea < cfg.BadgeMinAreaPx)
        {
            rejectReason = $"area {contourArea:F0} < minAreaPx {cfg.BadgeMinAreaPx}";
            return false;
        }

        if (contourArea > cfg.BadgeMaxAreaPx)
        {
            rejectReason = $"area {contourArea:F0} > maxAreaPx {cfg.BadgeMaxAreaPx}";
            return false;
        }

        if (contourArea > imageArea * cfg.BadgeMaxAreaProportion)
        {
            rejectReason = $"area {contourArea:F0} > {cfg.BadgeMaxAreaProportion:P0} of image";
            return false;
        }

        // --- Aspect ratio ---
        double aspect = bbox.Width / (double)Math.Max(1, bbox.Height);
        if (aspect < cfg.BadgeMinAspectRatio || aspect > cfg.BadgeMaxAspectRatio)
        {
            rejectReason = $"aspect {aspect:F2} outside [{cfg.BadgeMinAspectRatio}, {cfg.BadgeMaxAspectRatio}]";
            return false;
        }

        // --- Solidity ---
        var hull = Cv2.ConvexHull(contour);
        double hullArea = Cv2.ContourArea(hull);
        double solidity = hullArea <= 0 ? 0 : contourArea / hullArea;
        if (solidity < cfg.BadgeMinSolidity)
        {
            rejectReason = $"solidity {solidity:F2} < {cfg.BadgeMinSolidity}";
            return false;
        }

        // --- Mean HSV inside contour ---
        try
        {
            using var maskRoi = new Mat(mask.Rows, mask.Cols, MatType.CV_8UC1, Scalar.All(0));
            Cv2.DrawContours(maskRoi, new[] { contour }, -1, Scalar.White, Cv2.FILLED);
            var mean = Cv2.Mean(hsv, maskRoi);
            double meanS = mean.Val1;
            double meanV = mean.Val2;
            if (meanS > cfg.BadgeSaturationIntensityMaxMean || meanV < cfg.BadgeSaturationIntensityMinMean)
            {
                rejectReason = $"meanS {meanS:F1} > {cfg.BadgeSaturationIntensityMaxMean} or meanV {meanV:F1} < {cfg.BadgeSaturationIntensityMinMean}";
                return false;
            }
        }
        catch
        {
            // ignore HSV sampling errors
        }

        return true;
    }

    /// <summary>Find the best badge contour and return an OpenCvSharp.Rect (CvRect).</summary>
    private BadgeDiagnostics
        FindBestBadgeCvWithDiagnostics(Mat hsv)
    {
        using var mask = new Mat();
        Cv2.InRange(hsv, _config.BadgeHsvLower, _config.BadgeHsvUpper, mask);
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close,
            Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5)));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Open,
            Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3)));

        var contours = Cv2.FindContoursAsMat(mask, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var candidates = new List<BadgeCandidate>();
        CvRect best = default;
        bool found = false;
        int imageArea = mask.Rows * mask.Cols;

        int idx = 0;
        foreach (var cnt in contours)
        {
            var pts = cnt.ToArray();
            if (pts.Length < 5)
            {
                idx++;
                continue;
            }

            bool accepted = IsContourCandidate(mask, hsv, pts, imageArea, _config,
                out var reason, out var area, out var bbox);

            candidates.Add(new (idx, pts, area, bbox, reason, accepted));

            if (accepted && IsPlausibleBadgeSize(bbox))
            {
                if (!found || bbox.Y > best.Y)
                {
                    best = bbox;
                    found = true;
                }
            }

            idx++;
        }

        return new (BestBadge: found ? best : (CvRect?)null, 
            Candidates: candidates,
            Mask: mask.Clone());
    }

    private CvRect? FindBestBadgeCv(Mat hsv)
    {
        using var mask = new Mat();
        Cv2.InRange(hsv, _config.BadgeHsvLower, _config.BadgeHsvUpper, mask);
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close,
            Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5)));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Open,
            Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3)));

        var contours = Cv2.FindContoursAsMat(mask, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        CvRect best = default;
        bool found = false;
        int imageArea = mask.Rows * mask.Cols;

        int idx = 0;
        foreach (var cnt in contours)
        {
            var pts = cnt.ToArray();
            if (pts.Length < 5)
            {
                idx++;
                continue;
            }

            if (!IsContourCandidate(mask, hsv, pts, imageArea, _config,
                out var reason, out var area, out var bbox))
            {
                _logger.LogDebug("Badge candidate {i} rejected: {reason} bbox={bbox} area={area:F0}",
                    idx, reason, bbox, area);
                idx++;
                continue;
            }

            if (!IsPlausibleBadgeSize(bbox))
            {
                _logger.LogDebug("Badge candidate {i} failed size check bbox={bbox}", idx, bbox);
                idx++;
                continue;
            }

            if (!found || bbox.Y > best.Y)
            {
                best = bbox;
                found = true;
                _logger.LogDebug("Badge candidate {i} accepted bbox={bbox} area={area:F0}", idx, bbox, area);
            }

            idx++;
        }

        var candidateList = new List<(int, Point[], double, CvRect, string, bool)>();

        foreach (var cnt in contours)
        {
            var pts = cnt.ToArray();
            if (pts.Length < 5) continue;

            bool accepted = IsContourCandidate(mask, hsv, pts, imageArea, _config,
                out var reason, out var area, out var bbox);

            candidateList.Add((idx, pts, area, bbox, reason, accepted));

            idx++;
        }


        return found ? (CvRect?)best : null;
    }

    private bool IsPlausibleBadgeSize(CvRect rect)
    {
        return rect.Width >= _config.BadgeMinWidth &&
               rect.Width <= _config.BadgeMaxWidth &&
               rect.Height >= _config.BadgeMinHeight &&
               rect.Height <= _config.BadgeMaxHeight;
    }

    /// <summary>Recognize numeric level inside a badge crop (Mat).</summary>
    private (int? Level, float Confidence) RecognizeBadgeLevel(Mat badgeCrop)
    {
        try
        {
            using var gray = new Mat();
            Cv2.CvtColor(badgeCrop, gray, ColorConversionCodes.BGR2GRAY);

            using var thresh = new Mat();
            Cv2.Threshold(gray, thresh, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);

            var scaleFactor = Math.Max(1.0, 64.0 / Math.Max(1, thresh.Height));
            using var scaled = new Mat();
            Cv2.Resize(thresh, scaled, new Size(), scaleFactor, scaleFactor, InterpolationFlags.Linear);
            Cv2.ImEncode(".png", scaled, out byte[] imageBytes);

            using var pix = Pix.LoadFromMemory(imageBytes);
            using var page = _tesseractDigits.Process(pix, PageSegMode.SingleBlock);
            var text = page.GetText()?.Trim() ?? string.Empty;
            var confidence = page.GetMeanConfidence();

            var digits = new string(text.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var level))
            {
                if (level >= _config.LevelRange.Min && level <= _config.LevelRange.Max && confidence >= _config.LevelOcrConfidenceThreshold)
                    return (level, confidence);
            }

            if (int.TryParse(digits, out var parsed))
                return (null, confidence);

            return (null, confidence);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during badge OCR");
            return (null, 0f);
        }
    }

    /// <summary>Find nameplate above badge; returns CvRect in full-image coordinates.</summary>
    private CvRect? FindNameplateAboveBadgeCv(Mat hsv, CvRect badgeRect, int imageWidth)
    {
        int searchTop = Math.Max(0, badgeRect.Y - (int)(badgeRect.Height * _config.NameplateSearchMultiplier));
        int searchBottom = Math.Max(0, badgeRect.Y - 2);
        if (searchBottom <= searchTop)
            return null;

        var band = new CvRect(0, searchTop, imageWidth, searchBottom - searchTop);
        using var bandMat = new Mat(hsv, band);

        Scalar lowerGrey = new Scalar(0, 0, 80);
        Scalar upperGrey = new Scalar(180, 40, 200);
        using var mask = new Mat();
        Cv2.InRange(bandMat, lowerGrey, upperGrey, mask);
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, Cv2.GetStructuringElement(MorphShapes.Rect, new Size(7, 3)));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Open, Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3)));

        var contours = Cv2.FindContoursAsMat(mask, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        CvRect best = default;
        bool found = false;

        foreach (var cnt in contours)
        {
            var r = Cv2.BoundingRect(cnt);
            var fullRect = new CvRect(r.X, r.Y + band.Y, r.Width, r.Height);

            if (fullRect.Width < 80 || fullRect.Height > 60)
                continue;

            var nameCenterX = fullRect.X + fullRect.Width / 2;
            var badgeCenterX = badgeRect.X + badgeRect.Width / 2;
            if (Math.Abs(nameCenterX - badgeCenterX) > _config.HorizontalAlignmentTolerancePx)
                continue;

            if (!found || fullRect.Y > best.Y)
            {
                best = fullRect;
                found = true;
            }
        }

        return found ? (CvRect?)best : null;
    }

    /// <summary>Classify nameplate text color by sampling glyph pixels inside the nameplate crop.</summary>
    private Relationship ClassifyNameplateTextColor(CvRect nameplateCvRect, Mat fullImage, out double confidence)
    {
        confidence = 0.0;
        try
        {
            using var nameplateCrop = new Mat(fullImage, nameplateCvRect);
            using var hsv = new Mat();
            Cv2.CvtColor(nameplateCrop, hsv, ColorConversionCodes.BGR2HSV);

            // Create a simple glyph mask by detecting pixels that differ from grey background
            using var greyMask = new Mat();
            Cv2.InRange(hsv, new Scalar(0, 0, 0), new Scalar(180, 40, 255), greyMask);

            var hsvPixels = new List<Vec3b>();
            for (int y = 0; y < hsv.Rows; y++)
            {
                for (int x = 0; x < hsv.Cols; x++)
                {
                    if (greyMask.At<byte>(y, x) > 0)
                        hsvPixels.Add(hsv.At<Vec3b>(y, x));
                }
            }

            if (hsvPixels.Count == 0)
                return Relationship.Unknown;

            var medianH = Median(hsvPixels.Select(v => (int)v.Item0).ToArray());
            var medianS = Median(hsvPixels.Select(v => (int)v.Item1).ToArray());
            var medianV = Median(hsvPixels.Select(v => (int)v.Item2).ToArray());
            var sample = new Scalar(medianH, medianS, medianV);

            if (InRange(sample, _config.BlueTextRange.Lower, _config.BlueTextRange.Upper))
            {
                confidence = 1.0;
                return Relationship.InAlliance;
            }

            if (InRange(sample, _config.RedTextRange.Lower, _config.RedTextRange.Upper))
            {
                confidence = 1.0;
                return Relationship.Threat;
            }

            if (InRange(sample, _config.YellowTextRange.Lower, _config.YellowTextRange.Upper) ||
                InRange(sample, _config.WhiteTextRange.Lower, _config.WhiteTextRange.Upper))
            {
                confidence = 1.0;
                return Relationship.SameServerNonThreat;
            }

            var blueCenter = HsvCenter(_config.BlueTextRange);
            var redCenter = HsvCenter(_config.RedTextRange);
            var whiteCenter = HsvCenter(_config.WhiteTextRange);

            var dBlue = ColorDistance(sample, blueCenter);
            var dRed = ColorDistance(sample, redCenter);
            var dWhite = ColorDistance(sample, whiteCenter);

            var min = Math.Min(dBlue, Math.Min(dRed, dWhite));
            confidence = 1.0 / (1.0 + min);

            if (min == dBlue) return Relationship.InAlliance;
            if (min == dRed) return Relationship.Threat;
            return Relationship.SameServerNonThreat;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during nameplate color classification");
            return Relationship.Unknown;
        }
    }

    /// <summary>Recognize alliance tag and username from a nameplate crop (Mat).</summary>
    private (string Alliance, string Username, float Confidence) RecognizeNameplate(Mat nameplateCrop)
    {
        try
        {
            using var gray = new Mat();
            Cv2.CvtColor(nameplateCrop, gray, ColorConversionCodes.BGR2GRAY);

            using var clahe = Cv2.CreateCLAHE(2.0, new Size(8, 8));
            var enhanced = new Mat();
            clahe.Apply(gray, enhanced);

            var thresh = new Mat();
            Cv2.AdaptiveThreshold(enhanced, thresh, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, 11, 2);

            using var masked = MaskOutFlagAreaIfPresent(nameplateCrop, thresh);

            var scaleFactor = Math.Max(1.0, 128.0 / Math.Max(1, masked.Height));
            using var scaled = new Mat();
            Cv2.Resize(masked, scaled, new Size(), scaleFactor, scaleFactor, InterpolationFlags.Linear);
            Cv2.ImEncode(".png", scaled, out byte[] imageBytes);

            using var pix = Pix.LoadFromMemory(imageBytes);
            using var page = _tesseractNameplate.Process(pix, PageSegMode.SingleLine);
            var raw = page.GetText()?.Trim() ?? string.Empty;
            var conf = page.GetMeanConfidence();

            var alliance = ExtractAllianceTag(raw, out var username);
            return (alliance, username, conf);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during nameplate OCR");
            return (null, null, 0f);
        }
    }

    #endregion

    #region Small utility helpers

    private Mat NormalizeToReference(Mat src)
    {
        if (src.Width == _config.ReferenceResolution.Width && src.Height == _config.ReferenceResolution.Height)
            return src.Clone();

        var dst = new Mat();
        Cv2.Resize(src, dst, _config.ReferenceResolution, 0, 0, InterpolationFlags.Linear);
        return dst;
    }

    private Mat ConvertToHsv(Mat src)
    {
        var hsv = new Mat();
        Cv2.CvtColor(src, hsv, ColorConversionCodes.BGR2HSV);
        return hsv;
    }

    private Mat CropMat(Mat fullImage, CvRect rect)
    {
        return new Mat(fullImage, rect).Clone();
    }

    // Assumes CvRect = OpenCvSharp.Rect alias at top of file
    private Mat MaskOutFlagAreaIfPresent(Mat colorNameplate, Mat binaryMask)
    {
        try
        {
            int scanWidth = Math.Min(40, colorNameplate.Width / 4);
            var leftRegion = new CvRect(0, 0, scanWidth, colorNameplate.Height);

            using var leftCrop = new Mat(colorNameplate, leftRegion);
            using var hsv = new Mat();
            Cv2.CvtColor(leftCrop, hsv, ColorConversionCodes.BGR2HSV);

            using var flagMask = new Mat();
            Cv2.InRange(hsv, new Scalar(0, 60, 40), new Scalar(180, 255, 255), flagMask);
            var nonZero = Cv2.CountNonZero(flagMask);

            // If a flag-like region exists, zero out the left area in a clone of the binary mask
            if (nonZero > (leftCrop.Width * leftCrop.Height * 0.05))
            {
                var masked = binaryMask.Clone();

                // Create a sub-matrix for the ROI and set it to zero
                var roi = new CvRect(0, 0, scanWidth, masked.Height);
                using (var sub = new Mat(masked, roi))
                {
                    sub.SetTo(new Scalar(0));
                }

                return masked;
            }

            return binaryMask;
        }
        catch
        {
            return binaryMask;
        }
    }

    private string ExtractAllianceTag(string rawOcr, out string username)
    {
        username = null;
        if (string.IsNullOrWhiteSpace(rawOcr))
            return null;

        var s = rawOcr.Replace("\n", " ").Trim();
        var start = s.IndexOf('[');
        var end = s.IndexOf(']');
        if (start >= 0 && end > start)
        {
            var tag = s.Substring(start + 1, end - start - 1).Trim();
            var remainder = s.Substring(end + 1).Trim();
            username = CleanUsername(remainder);
            return string.IsNullOrWhiteSpace(tag) ? null : tag;
        }

        username = CleanUsername(s);
        return null;
    }

    private string CleanUsername(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var cleaned = new string(raw.Where(c => c >= 32 && c <= 126).ToArray()).Trim();
        while (cleaned.Contains("  ")) cleaned = cleaned.Replace("  ", " ");
        return cleaned;
    }

    private void SaveDiagnosticIfEnabled(Mat crop, string imageId, string tag)
    {
        if (!_config.DiagnosticsEnabled) return;
        try
        {
            Directory.CreateDirectory(_config.DiagnosticsDirectory);
            var filename = Path.Combine(_config.DiagnosticsDirectory, $"{imageId}_{tag}_{Guid.NewGuid():N}.png");
            Cv2.ImWrite(filename, crop);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save diagnostic image");
        }
    }

    private void LogAndRecord(HqExtractionResult result, string message, string imageId, string step)
    {
        var logMsg = $"{step}: {message} (image={imageId})";
        _logger.LogWarning(logMsg);
        result.Errors.Add(logMsg);

        if (_config.DiagnosticsEnabled)
            result.DiagnosticsPath = _config.DiagnosticsDirectory;

        if (_config.ThrowOnMissing)
            throw new InvalidOperationException(logMsg);
    }

    private HqExtractionResult FinalizeResult(HqExtractionResult result)
    {
        if (result.BadgeRect.HasValue)
        {
            if (_config.AllowPartialExtraction)
                result.Success = true;
            else
                result.Success = result.Level.HasValue && !string.IsNullOrWhiteSpace(result.Username);
        }
        else
        {
            result.Success = false;
        }

        return result;
    }

    #endregion

    #region Small math & color helpers

    private static int Median(int[] values)
    {
        if (values == null || values.Length == 0) return 0;
        Array.Sort(values);
        int mid = values.Length / 2;
        return values.Length % 2 == 0 ? (values[mid - 1] + values[mid]) / 2 : values[mid];
    }

    private static bool InRange(Scalar sample, Scalar lower, Scalar upper)
    {
        return sample.Val0 >= lower.Val0 && sample.Val0 <= upper.Val0 &&
               sample.Val1 >= lower.Val1 && sample.Val1 <= upper.Val1 &&
               sample.Val2 >= lower.Val2 && sample.Val2 <= upper.Val2;
    }

    private static Scalar HsvCenter(ScalarRange range)
    {
        return new Scalar(
            (range.Lower.Val0 + range.Upper.Val0) / 2.0,
            (range.Lower.Val1 + range.Upper.Val1) / 2.0,
            (range.Lower.Val2 + range.Upper.Val2) / 2.0
        );
    }

    private static double ColorDistance(Scalar a, Scalar b)
    {
        var dh = a.Val0 - b.Val0;
        var ds = a.Val1 - b.Val1;
        var dv = a.Val2 - b.Val2;
        return Math.Sqrt(dh * dh + ds * ds + dv * dv);
    }

    #endregion

    public void Dispose()
    {
        _tesseractDigits?.Dispose();
        _tesseractNameplate?.Dispose();
    }
}