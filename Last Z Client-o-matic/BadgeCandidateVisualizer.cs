using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

using CvRect = OpenCvSharp.Rect;

namespace HqExtractorLib.Diagnostics
{
    public static class BadgeCandidateVisualizer
    {
        public static string SaveOverlay(
            string imageId,
            Mat original,
            List<BadgeCandidate> candidates,
            string diagnosticsDir,
            ILogger logger)
        {
            Directory.CreateDirectory(diagnosticsDir);
            List<int> rndcolor = new();
            int getNewColor()
            {
                int rcolor = Random.Shared.Next(0, 255);
                if (rndcolor.Contains(rcolor))
                    return getNewColor();

                return rcolor;
            }

            using var overlay = original.Clone();

            foreach (var c in candidates.Where(b => b.Accepted))
            {
                Scalar color = c.Accepted ? new Scalar(0, getNewColor(), 0) : new Scalar(0, 0, getNewColor()); // green accepted, red rejected
                

                // Draw contour
                Cv2.Polylines(overlay, new[] { c.Contour }, true, color, 2);

                // Draw bounding box
                Cv2.Rectangle(overlay, c.BBox, color, 2);

                // Label
                Cv2.PutText(
                    overlay,
                    $"{c.Index}:{(c.Accepted ? "OK" : "NO")}",
                    new Point(c.BBox.X, Math.Max(0, c.BBox.Y - 5)),
                    HersheyFonts.HersheySimplex,
                    0.5,
                    color,
                    1
                );
            }

            string path = Path.Combine(diagnosticsDir, $"{imageId}_badge_overlay.png");
            Cv2.ImWrite(path, overlay);

            logger.LogInformation("Candidate overlay saved: {path}", path);
            return path;
        }
    }
}
