using CvRect = OpenCvSharp.Rect;
using TessRect = Tesseract.Rect;

internal static class RectConversions
{
    public static HqRect FromCvRect(CvRect r) => new HqRect(r.X, r.Y, r.Width, r.Height);
    public static CvRect ToCvRect(HqRect r) => new CvRect(r.X, r.Y, r.Width, r.Height);

    public static HqRect FromTessRect(TessRect r) => new HqRect(r.X1, r.Y1, r.Width, r.Height);
    public static TessRect ToTessRect(HqRect r) => new TessRect(r.X, r.Y, r.Width, r.Height);
}

public readonly struct HqRect
{
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }

    public HqRect(int x, int y, int width, int height)
    {
        X = x; Y = y; Width = width; Height = height;
    }

    public override string ToString() => $"HqRect({X},{Y},{Width}x{Height})";
}
