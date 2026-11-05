using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

using CvRect = OpenCvSharp.Rect;

namespace HqExtractorLib.Diagnostics
{
    public static class BadgeConfigAutoTuner
    {
        public sealed class TuningResult
        {
            public double SuggestedMinArea { get; set; }
            public double SuggestedMaxArea { get; set; }
            public double SuggestedMaxAreaFraction { get; set; }
            public double SuggestedMinAspect { get; set; }
            public double SuggestedMaxAspect { get; set; }
            public double SuggestedMinSolidity { get; set; }
            public double SuggestedMaxMeanS { get; set; }
            public double SuggestedMinMeanV { get; set; }
        }

        public static TuningResult AnalyzeFolder(
            string folder,
            HqExtractorConfig config,
            ILogger logger)
        {
            var areas = new List<double>();
            var areaFractions = new List<double>();
            var aspects = new List<double>();
            var solidities = new List<double>();
            var meanS = new List<double>();
            var meanV = new List<double>();

            foreach (var file in Directory.GetFiles(folder))
            {
                using var img = Cv2.ImRead(file, ImreadModes.Color);
                using var hsv = new Mat();
                Cv2.CvtColor(img, hsv, ColorConversionCodes.BGR2HSV);

                using var mask = new Mat();
                Cv2.InRange(hsv, config.BadgeHsvLower, config.BadgeHsvUpper, mask);
                Cv2.MorphologyEx(mask, mask, MorphTypes.Close,
                    Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5)));
                Cv2.MorphologyEx(mask, mask, MorphTypes.Open,
                    Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3)));

                var contours = Cv2.FindContoursAsMat(mask, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                int imageArea = mask.Rows * mask.Cols;

                foreach (var cnt in contours)
                {
                    var pts = cnt.ToArray();
                    if (pts.Length < 5) continue;

                    double area = Cv2.ContourArea(pts);
                    var bbox = Cv2.BoundingRect(pts);
                    double aspect = bbox.Width / (double)Math.Max(1, bbox.Height);

                    var hull = Cv2.ConvexHull(pts);
                    double hullArea = Cv2.ContourArea(hull);
                    double solidity = hullArea <= 0 ? 0 : area / hullArea;

                    using var roiMask = new Mat(mask.Rows, mask.Cols, MatType.CV_8UC1, Scalar.All(0));
                    Cv2.DrawContours(roiMask, new[] { pts }, -1, Scalar.White, Cv2.FILLED);
                    var mean = Cv2.Mean(hsv, roiMask);

                    areas.Add(area);
                    areaFractions.Add(area / imageArea);
                    aspects.Add(aspect);
                    solidities.Add(solidity);
                    meanS.Add(mean.Val1);
                    meanV.Add(mean.Val2);
                }
            }

            logger.LogInformation("Auto-tuner analyzed {count} images", Directory.GetFiles(folder).Length);

            return new TuningResult
            {
                SuggestedMinArea = Percentile(areas, 5),
                SuggestedMaxArea = Percentile(areas, 95),
                SuggestedMaxAreaFraction = Percentile(areaFractions, 95),
                SuggestedMinAspect = Percentile(aspects, 5),
                SuggestedMaxAspect = Percentile(aspects, 95),
                SuggestedMinSolidity = Percentile(solidities, 5),
                SuggestedMaxMeanS = Percentile(meanS, 95),
                SuggestedMinMeanV = Percentile(meanV, 5)
            };
        }

        private static double Percentile(List<double> values, double percentile)
        {
            if (values.Count == 0) return 0;
            values.Sort();
            int index = (int)((percentile / 100.0) * (values.Count - 1));
            return values[index];
        }
    }
}
