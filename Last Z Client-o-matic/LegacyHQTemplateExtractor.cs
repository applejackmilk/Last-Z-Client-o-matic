using OpenCvSharp;

public static class OpenCvExtensions
{
    public static int GetPerimeter(this Rect rect)
        => 2 * rect.Height * rect.Width;
    public static bool IsEmpty(this Rect rect)
        => rect.GetPerimeter() == 0;
}
public class LegacyHQTemplateExtractor
{
    public class HQParts
    {
        public Mat? Building;
        public Mat? Nameplate;
        public Mat? Badge;
    }

    public HQParts Extract(string inputPath)
    {
        var img = Cv2.ImRead(inputPath, ImreadModes.Color);
        if (img.Empty())
            return new HQParts();

        Mat hsv = new Mat();
        Cv2.CvtColor(img, hsv, ColorConversionCodes.BGR2HSV);

        // Extract each component using dedicated methods
        var nameplateRect = ExtractNameplate(hsv);
        var badgeRect = ExtractBadge(hsv);
        var buildingRect = ExtractBuilding(img, nameplateRect);

        // Crop final parts
        Mat building = new Mat(img, buildingRect);
        Mat nameplate = new Mat(img, nameplateRect);
        Mat badge = new Mat(img, badgeRect);

        return new HQParts
        {
            Building = building,
            Nameplate = nameplate,
            Badge = badge
        };
    }

    // ---------------------------------------------------------
    // NAMEPLATE EXTRACTION
    // ---------------------------------------------------------
    private Rect ExtractNameplate(Mat hsv)
    {
        Scalar lowerGrey = new Scalar(0, 0, 80);
        Scalar upperGrey = new Scalar(180, 40, 200);

        Mat greyMask = new Mat();
        Cv2.InRange(hsv, lowerGrey, upperGrey, greyMask);

        Cv2.MorphologyEx(greyMask, greyMask, MorphTypes.Close,
            Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5)));

        var contours = Cv2.FindContoursAsMat(greyMask, RetrievalModes.External,
                                             ContourApproximationModes.ApproxSimple);

        foreach (var cnt in contours)
        {
            var rect = Cv2.BoundingRect(cnt);

            // Nameplate is wide and short
            if (rect.Width > 80 && rect.Height < 40)
                return rect;
        }

        throw new Exception("Could not detect nameplate");
    }

    // ---------------------------------------------------------
    // BADGE EXTRACTION
    // ---------------------------------------------------------
    private Rect ExtractBadge(Mat hsv)
    {
        // Badge background: opaque grey, low saturation, medium brightness
        Scalar lowerBadge = new Scalar(0, 0, 60);
        Scalar upperBadge = new Scalar(180, 40, 160);

        Mat badgeMask = new Mat();
        Cv2.InRange(hsv, lowerBadge, upperBadge, badgeMask);

        Cv2.MorphologyEx(badgeMask, badgeMask, MorphTypes.Close,
            Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5)));

        var contours = Cv2.FindContoursAsMat(badgeMask, RetrievalModes.External,
                                             ContourApproximationModes.ApproxSimple);

        Rect best = new();

        foreach (var cnt in contours)
        {
            var rect = Cv2.BoundingRect(cnt);

            // Badge shape constraints
            bool plausibleSize =
                rect.Width >= 40 && rect.Width <= 120 &&
                rect.Height >= 20 && rect.Height <= 60;

            if (!plausibleSize)
                continue;

            // Badge is usually near bottom of HQ cluster
            if (best.IsEmpty() || rect.Y > best.Y)
                best = rect;
        }

        if (best.IsEmpty())
            throw new Exception("Could not detect HQ level badge");

        return best;
    }
    // ---------------------------------------------------------
    // BUILDING SILHOUETTE EXTRACTION
    // ---------------------------------------------------------
    private Rect ExtractBuilding(Mat img, Rect nameplateRect)
    {
        int buildingTop = Math.Max(0, nameplateRect.Y - 200);
        int buildingBottom = nameplateRect.Y - 5;

        Rect searchRegion = new Rect(
            0,
            buildingTop,
            img.Width,
            buildingBottom - buildingTop
        );

        Mat buildingCrop = new Mat(img, searchRegion);

        Mat edges = new Mat();
        Cv2.Canny(buildingCrop, edges, 50, 150);

        var contours = Cv2.FindContoursAsMat(edges, RetrievalModes.External,
                                             ContourApproximationModes.ApproxSimple);

        foreach (var cnt in contours)
        {
            var rect = Cv2.BoundingRect(cnt);

            if (rect.Width > 80 && rect.Height > 80)
            {
                // Convert rect back to full-image coordinates
                return new Rect(
                    rect.X,
                    rect.Y + searchRegion.Y,
                    rect.Width,
                    rect.Height
                );
            }
        }

        // fallback: use entire region
        return searchRegion;
    }

    public void SaveHQParts(HQParts parts, string outputDir, string baseName)
    {
        Directory.CreateDirectory(outputDir);

        Cv2.ImWrite(Path.Combine(outputDir, $"{baseName}_building.png"), parts.Building);
        Cv2.ImWrite(Path.Combine(outputDir, $"{baseName}_nameplate.png"), parts.Nameplate);
        Cv2.ImWrite(Path.Combine(outputDir, $"{baseName}_badge.png"), parts.Badge);
    }
}
