// File: HqVisualizer.cs
// Requires OpenCvSharp4 and Microsoft.Extensions.Logging.Abstractions (optional logger).
// Place in same namespace as extractor or adjust using/imports.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

// Aliases to avoid ambiguous Rect references
using CvRect = OpenCvSharp.Rect;

namespace HqExtractorLib
{
    public static class Visualizer
    {
        /// <summary>
        /// Visualize badge & nameplate masks and annotate bounding boxes.
        /// Saves annotated image and mask images to diagnosticsDir and returns saved paths.
        /// </summary>
        public static (string annotatedPath, string badgeMaskPath, string nameplateMaskPath) VisualizeAndSave(
            Mat inputImage,
            HqExtractionResult extractionResult,
            HqExtractorConfig config,
            string imageId,
            ILogger logger = null)
        {
            logger ??= Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

            Directory.CreateDirectory(config.DiagnosticsDirectory);

            // Normalize image to reference resolution to match extractor behavior
            using var img = NormalizeToReference(inputImage, config.ReferenceResolution);

            // Convert to HSV once
            using var hsv = new Mat();
            Cv2.CvtColor(img, hsv, ColorConversionCodes.BGR2HSV);

            // Create masks
            using var badgeMask = CreateBadgeMask(hsv, config);
            using var nameplateMask = CreateNameplateMask(hsv, config, extractionResult.BadgeRect);

            // Annotate original image copy
            using var annotated = img.Clone();
            DrawMaskContours(annotated, badgeMask, new Scalar(0, 255, 255), 1);      // yellow contours for badge mask
            DrawMaskContours(annotated, nameplateMask, new Scalar(255, 0, 255), 1);  // magenta contours for nameplate mask

            // Draw bounding boxes if present
            if (extractionResult.BadgeRect.HasValue)
            {
                var cvBadge = RectConversions.ToCvRect(extractionResult.BadgeRect.Value);
                DrawBoxWithLabel(annotated, cvBadge, "Badge", new Scalar(0, 255, 0)); // green
            }

            if (extractionResult.NameplateRect.HasValue)
            {
                var cvName = RectConversions.ToCvRect(extractionResult.NameplateRect.Value);
                DrawBoxWithLabel(annotated, cvName, "Nameplate", new Scalar(0, 165, 255)); // orange
            }

            // Add text overlay with OCR results
            var y = 20;
            PutTextSafe(annotated, $"Level: {(extractionResult.Level?.ToString() ?? "N/A")} (conf {extractionResult.LevelConfidence:F2})", new Point(8, y), 0.6, new Scalar(255, 255, 255));
            y += 20;
            PutTextSafe(annotated, $"Username: {(extractionResult.Username ?? "N/A")} (conf {extractionResult.UsernameConfidence:F2})", new Point(8, y), 0.6, new Scalar(255, 255, 255));
            y += 20;
            PutTextSafe(annotated, $"Alliance: {(extractionResult.AllianceTag ?? "N/A")} Relationship: {extractionResult.Relationship}", new Point(8, y), 0.6, new Scalar(255, 255, 255));

            // Save files
            var annotatedPath = Path.Combine(config.DiagnosticsDirectory, $"{imageId}_annotated.png");
            var badgeMaskPath = Path.Combine(config.DiagnosticsDirectory, $"{imageId}_badge_mask.png");
            var nameplateMaskPath = Path.Combine(config.DiagnosticsDirectory, $"{imageId}_nameplate_mask.png");

            try
            {
                Cv2.ImWrite(annotatedPath, annotated);
                Cv2.ImWrite(badgeMaskPath, VisualizeBinaryMask(badgeMask));
                Cv2.ImWrite(nameplateMaskPath, VisualizeBinaryMask(nameplateMask));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to write visualization images");
            }

            return (annotatedPath, badgeMaskPath, nameplateMaskPath);
        }

        // -------------------------
        // Helper methods
        // -------------------------

        private static Mat NormalizeToReference(Mat src, Size reference)
        {
            if (src.Width == reference.Width && src.Height == reference.Height)
                return src.Clone();

            var dst = new Mat();
            Cv2.Resize(src, dst, reference, 0, 0, InterpolationFlags.Linear);
            return dst;
        }

        private static Mat CreateBadgeMask(Mat hsv, HqExtractorConfig config)
        {
            var mask = new Mat();
            Cv2.InRange(hsv, config.BadgeHsvLower, config.BadgeHsvUpper, mask);
            Cv2.MorphologyEx(mask, mask, MorphTypes.Close, Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5)));
            Cv2.MorphologyEx(mask, mask, MorphTypes.Open, Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3)));
            return mask;
        }

        private static Mat CreateNameplateMask(Mat hsv, HqExtractorConfig config, HqRect? badgeRect)
        {
            // If badgeRect is not available, create a broad mask across the image
            if (!badgeRect.HasValue)
            {
                var mask = new Mat();
                Scalar lowerGrey = new Scalar(0, 0, 80);
                Scalar upperGrey = new Scalar(180, 40, 200);
                Cv2.InRange(hsv, lowerGrey, upperGrey, mask);
                Cv2.MorphologyEx(mask, mask, MorphTypes.Close, Cv2.GetStructuringElement(MorphShapes.Rect, new Size(7, 3)));
                Cv2.MorphologyEx(mask, mask, MorphTypes.Open, Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3)));
                return mask;
            }

            // Otherwise restrict search band above the badge (same logic as extractor)
            var badge = RectConversions.ToCvRect(badgeRect.Value);
            int searchTop = Math.Max(0, badge.Y - (int)(badge.Height * config.NameplateSearchMultiplier));
            int searchBottom = Math.Max(0, badge.Y - 2);
            if (searchBottom <= searchTop)
            {
                // fallback to full-image mask
                return CreateNameplateMask(hsv, config, (HqRect?)null);
            }

            var band = new CvRect(0, searchTop, hsv.Width, searchBottom - searchTop);
            using var bandMat = new Mat(hsv, band);

            Scalar lowerGrey2 = new Scalar(0, 0, 80);
            Scalar upperGrey2 = new Scalar(180, 40, 200);
            var maskBand = new Mat();
            Cv2.InRange(bandMat, lowerGrey2, upperGrey2, maskBand);
            Cv2.MorphologyEx(maskBand, maskBand, MorphTypes.Close, Cv2.GetStructuringElement(MorphShapes.Rect, new Size(7, 3)));
            Cv2.MorphologyEx(maskBand, maskBand, MorphTypes.Open, Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3)));

            // Place maskBand back into full-size mask
            var fullMask = Mat.Zeros(hsv.Rows, hsv.Cols, MatType.CV_8UC1);
            var roi = new CvRect(band.X, band.Y, maskBand.Width, maskBand.Height);
            using (var sub = new Mat(fullMask, roi))
            {
                maskBand.CopyTo(sub);
            }

            maskBand.Dispose();
            return fullMask;
        }

        private static Mat VisualizeBinaryMask(Mat mask)
        {
            // Convert single-channel mask to 3-channel color for easier viewing
            var color = new Mat();
            Cv2.CvtColor(mask, color, ColorConversionCodes.GRAY2BGR);
            // Optionally colorize: set mask pixels to a color
            for (int y = 0; y < color.Rows; y++)
            {
                for (int x = 0; x < color.Cols; x++)
                {
                    var v = color.At<Vec3b>(y, x);
                    if (v.Item0 != 0) // mask pixel
                    {
                        // set to cyan-ish for badge / magenta for nameplate later; here keep white
                        color.Set(y, x, new Vec3b(255, 255, 255));
                    }
                }
            }
            return color;
        }

        private static void DrawMaskContours(Mat target, Mat mask, Scalar color, int thickness = 1)
        {
            try
            {
                var contours = Cv2.FindContoursAsMat(mask, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                foreach (var c in contours)
                {
                    var pts = Cv2.ApproxPolyDP(c.ToArray(), 3, true);
                    Cv2.Polylines(target, new[] { pts }, true, color, thickness);
                }
            }
            catch
            {
                // ignore contour drawing errors
            }
        }

        private static void DrawBoxWithLabel(Mat img, CvRect rect, string label, Scalar boxColor)
        {
            Cv2.Rectangle(img, rect, boxColor, 2);
            var textPt = new Point(rect.X, Math.Max(12, rect.Y - 6));
            PutTextSafe(img, label, textPt, 0.5, boxColor, thickness: 2, bg: new Scalar(0, 0, 0));
        }

        private static void PutTextSafe(Mat img, string text, Point org, double scale, Scalar color, int thickness = 1, Scalar? bg = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            var font = HersheyFonts.HersheySimplex;
            var size = Cv2.GetTextSize(text, font, scale, thickness, out int baseline);
            if (bg.HasValue)
            {
                var rect = new Rect(org.X, org.Y - size.Height - baseline, size.Width + 6, size.Height + baseline + 4);
                // clamp rect inside image
                rect.X = Math.Max(0, Math.Min(rect.X, img.Width - rect.Width));
                rect.Y = Math.Max(0, Math.Min(rect.Y, img.Height - rect.Height));
                Cv2.Rectangle(img, rect, bg.Value, Cv2.FILLED);
            }
            Cv2.PutText(img, text, org, font, scale, color, thickness);
        }
    }
}
