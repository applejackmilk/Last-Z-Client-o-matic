using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace HqExtractorLib.Diagnostics
{
    public static class BadgeCandidateReporter
    {
        public static string WriteCandidateReport(
            string imageId,
            Mat mask,
            Mat hsv,
            List<BadgeCandidate> candidates,
            string diagnosticsDir,
            ILogger logger)
        {
            Directory.CreateDirectory(diagnosticsDir);
            string aggregatePath = Path.Combine(diagnosticsDir, $"all_badge_candidates.csv");
            var fi = new FileInfo(aggregatePath);
            var newAggregateFile = !fi.Exists || fi.Length < 5;

            string path = Path.Combine(diagnosticsDir, $"{imageId}_badge_candidates.csv");
            using var aggregateWriter = new StreamWriter(aggregatePath, true);
            using var writer = new StreamWriter(path);
            if (newAggregateFile)
            {
                aggregateWriter.WriteLine("ImageID, Index,Accepted,Reason,Area,BBoxX,BBoxY,BBoxW,BBoxH,Aspect,Solidity,MeanH,MeanS,MeanV");
            }

            writer.WriteLine("Index,Accepted,Reason,Area,BBoxX,BBoxY,BBoxW,BBoxH,Aspect,Solidity,MeanH,MeanS,MeanV");

            foreach (var c in candidates.OrderBy(a => a.Accepted))
            {
                double aspect = c.BBox.Width / (double)Math.Max(1, c.BBox.Height);

                // Compute solidity
                var hull = Cv2.ConvexHull(c.Contour);
                double hullArea = Cv2.ContourArea(hull);
                double solidity = hullArea <= 0 ? 0 : c.Area / hullArea;

                // Compute mean HSV inside contour
                double meanH = 0, meanS = 0, meanV = 0;
                try
                {
                    using var roiMask = new Mat(mask.Rows, mask.Cols, MatType.CV_8UC1, Scalar.All(0));
                    Cv2.DrawContours(roiMask, new[] { c.Contour }, -1, Scalar.White, Cv2.FILLED);
                    var mean = Cv2.Mean(hsv, roiMask);
                    meanH = mean.Val0;
                    meanS = mean.Val1;
                    meanV = mean.Val2;
                }
                catch { }

                writer.WriteLine(string.Join(",",
                    c.Index,
                    c.Accepted ? "YES" : "NO",
                    Quote(c.Reason),
                    c.Area.ToString(CultureInfo.InvariantCulture),
                    c.BBox.X, c.BBox.Y, c.BBox.Width, c.BBox.Height,
                    aspect.ToString("F3"),
                    solidity.ToString("F3"),
                    meanH.ToString("F1"),
                    meanS.ToString("F1"),
                    meanV.ToString("F1")
                ));

                aggregateWriter.WriteLine(string.Join(",",
                    imageId,
                    c.Index,
                    c.Accepted ? "YES" : "NO",
                    Quote(c.Reason),
                    c.Area.ToString(CultureInfo.InvariantCulture),
                    c.BBox.X, c.BBox.Y, c.BBox.Width, c.BBox.Height,
                    aspect.ToString("F3"),
                    solidity.ToString("F3"),
                    meanH.ToString("F1"),
                    meanS.ToString("F1"),
                    meanV.ToString("F1")
                ));
                
            }
            logger.LogInformation("Candidate report written: {path}", path);
            return path;
        }

        private static string Quote(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            return $"\"{s.Replace("\"", "\"\"")}\"";
        }
    }
}
