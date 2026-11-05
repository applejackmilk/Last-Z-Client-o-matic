using System.Collections.Generic;
using OpenCvSharp;

using CvRect = OpenCvSharp.Rect;

namespace HqExtractorLib.Diagnostics
{
    public sealed record BadgeDiagnostics(
        CvRect? BestBadge,
        List<BadgeCandidate> Candidates,
        Mat Mask
    );

    public sealed record BadgeCandidate(
        int Index,
        Point[] Contour,
        double Area,
        CvRect BBox,
        string Reason,
        bool Accepted
    );
}
