using OpenCvSharp;

public record ScalarRange(Scalar Upper, Scalar Lower);
public record Range_i(int Min, int Max);
public record Range_d(double Min, double Max);

/// <summary>
/// Configuration for the HQ extractor behavior and thresholds.
/// </summary>
public sealed class HqExtractorConfig
{
    public void ConfigureBadgeSettings(Scalar badgeHsvLower,
                             Scalar badgeHsvUpper,
                             Range_i width,
                             Range_i height,
                             Range_i areaPx,
                             double badgeMaxAreaProportion,
                             Range_d aspectRatio,
                             double badgeMinSolidity,
                             Range_d saturationIntensity)
    {
        BadgeHsvLower = badgeHsvLower;
        BadgeHsvUpper = badgeHsvUpper;
        BadgeMinWidth = width.Min;
        BadgeMaxWidth = width.Max;
        BadgeMinHeight = height.Min;
        BadgeMaxHeight = height.Max;
        BadgeMinAreaPx = areaPx.Min;
        BadgeMaxAreaPx = areaPx.Max;
        BadgeMaxAreaProportion = badgeMaxAreaProportion;
        BadgeMinAspectRatio = aspectRatio.Min;
        BadgeMaxAspectRatio = aspectRatio.Max;
        BadgeMinSolidity = badgeMinSolidity;
        BadgeSaturationIntensityMinMean = saturationIntensity.Min;
        BadgeSaturationIntensityMaxMean = saturationIntensity.Max;
    }

    /// <summary>Whether to throw exceptions when required features are missing.</summary>
    public bool ThrowOnMissing { get; set; } = false;

    /// <summary>Whether to allow returning partial extraction results.</summary>
    public bool AllowPartialExtraction { get; set; } = true;

    /// <summary>Whether to save diagnostic images on failures.</summary>
    public bool DiagnosticsEnabled { get; set; } = false;

    /// <summary>Directory to write diagnostics into when enabled.</summary>
    public string DiagnosticsDirectory { get; set; } = "diagnostics";

    /// <summary>HSV lower bound for badge detection (H,S,V).</summary>
    public Scalar BadgeHsvLower { get; set; } = new Scalar(0, 0, 80);

    /// <summary>HSV upper bound for badge detection (H,S,V).</summary>
    public Scalar BadgeHsvUpper { get; set; } = new Scalar(180, 30, 220);

    /// <summary>Minimum badge width in pixels at normalized resolution.</summary>
    public int BadgeMinWidth { get; set; } = 40;

    /// <summary>Maximum badge width in pixels at normalized resolution.</summary>
    public int BadgeMaxWidth { get; set; } = 100;

    /// <summary>Minimum badge height in pixels at normalized resolution.</summary>
    public int BadgeMinHeight { get; set; } = 20;

    /// <summary>Maximum badge height in pixels at normalized resolution.</summary>
    public int BadgeMaxHeight { get; set; } = 45;
    public int BadgeMinAreaPx { get; set; } = 200;          // smallest plausible badge contour
    public int BadgeMaxAreaPx { get; set; } = 20000;        // largest plausible badge contour
    public double BadgeMaxAreaProportion { get; set; } = 0.02; // reject >2% of image area

    public double BadgeMinAspectRatio { get; set; } = 0.8;  // width/height lower bound
    public double BadgeMaxAspectRatio { get; set; } = 3.5;  // width/height upper bound

    public double BadgeMinSolidity { get; set; } = 0.50;    // Solidity is the ratio of contour area to its convex hull area.

    public double BadgeSaturationIntensityMaxMean { get; set; } = 40;         // badge should have low saturation
    public double BadgeSaturationIntensityMinMean { get; set; } = 70;         // Mean Color or Mean Intensity
    /// <summary>OCR confidence threshold for accepting numeric level (0..1).</summary>
    public float LevelOcrConfidenceThreshold { get; set; } = 0.6f;

    /// <summary>Allowed numeric level range inclusive.</summary>
    public Range_i LevelRange { get; set; } = new(10, 35);

    /// <summary>Reference resolution to normalize input images to (width, height).</summary>
    public Size ReferenceResolution { get; set; } = new Size(1920, 1080);

    /// <summary>Horizontal alignment tolerance in pixels when pairing nameplate to badge.</summary>
    public int HorizontalAlignmentTolerancePx { get; set; } = 40;

    /// <summary>Vertical multiplier to search for nameplate above badge (badge.Height * multiplier).</summary>
    public double NameplateSearchMultiplier { get; set; } = 2.5;

    /// <summary>HSV ranges for relationship color classification (H,S,V lower/upper pairs).</summary>
    public ScalarRange BlueTextRange { get; set; } = new(new Scalar(90, 80, 40), new Scalar(140, 255, 255));
    public ScalarRange RedTextRange { get; set; } = new(new Scalar(0, 80, 40), new Scalar(10, 255, 255));
    public ScalarRange YellowTextRange { get; set; } = new(new Scalar(15, 80, 40), new Scalar(40, 255, 255));
    public ScalarRange WhiteTextRange { get; set; } = new(new Scalar(0, 0, 200), new Scalar(180, 40, 255));
}