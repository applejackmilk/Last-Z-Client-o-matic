using OpenCvSharp;
using Tesseract;
/// <summary>
/// Result model returned by the extractor.
/// </summary>
public sealed class HqExtractionResult
{
    public bool Success { get; set; }
    public HqRect? BadgeRect { get; set; }
    public int? Level { get; set; }
    public float LevelConfidence { get; set; }
    public HqRect? NameplateRect { get; set; }
    public string Username { get; set; }
    public float UsernameConfidence { get; set; }
    public string AllianceTag { get; set; }
    public Relationship Relationship { get; set; } = Relationship.Unknown;
    public List<string> Errors { get; } = new List<string>();
    public string DiagnosticsPath { get; set; }
}
