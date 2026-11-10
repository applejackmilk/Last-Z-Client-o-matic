using NLog;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using TesseractOCR;
using TesseractOCR.Enums;
using WindowsMCP_Integrated.OCR;

namespace WindowsMCP_Integrated
{
    namespace LegacyOCR
    {
        public class MemberVSRankReader
        {
            private readonly Logger _logger = LogManager.GetCurrentClassLogger();
            private readonly TextDetectionModelDB _detector;
            private readonly Engine _tesseract;

            // ---- Layout, as fractions of image width (estimated from your 420px samples; tune) ----
            private const double RankMaxCenterX = 0.22; // rank center must be left of this
            private const double NameMinLeftX = 0.28; // name/alliance left edge range
            private const double NameMaxLeftX = 0.48;
            private const double ScoreMinLeftX = 0.50; // score left edge must be right of this
            private const double MaxNameToAllianceDy = 0.10; // max vertical distance name center -> alliance center

            private const double ImageScale = 2.0;  // upscale: helps detect small text like "0" and the alliance line
            private const int EdgeMargin = 4;       // px (scaled) - text closer than this to an edge counts as clipped

            private static readonly Regex NameRegex = new(@"^[A-Za-z0-9 ?]+$");
            private static readonly Regex AllianceRegex = new(@"^\[[^\]]+\]\S.*$");

            private enum Column { Rank, Name, Score, Other }
            private enum Field { Name, Alliance, Rank, Score }

            private sealed class TextBox
            {
                public OpenCvSharp.Rect Bounds;
                public Column Column;
                public double CenterY => Bounds.Y + Bounds.Height / 2.0;
            }

            public MemberVSRankReader(TextDetectionModelDB textDetectionModel, Engine engine)
            {
                _detector = textDetectionModel;
                _detector.SetBinaryThreshold(0.3f);
                _detector.SetPolygonThreshold(0.5f);
                _detector.SetMaxCandidates(200);
                _detector.SetUnclipRatio(1.5); // 2.0 inflates boxes so much that name/alliance boxes can overlap

                _tesseract = engine;
            }

            public List<MemberVSRank> ReadImage(string imagePath, string? debugImagePath = null)
            {
                var members = new List<MemberVSRank>();

                using var original = Cv2.ImRead(imagePath, ImreadModes.Color);
                if (original.Empty())
                {
                    _logger.Error($"Could not load image: {imagePath}");
                    return members;
                }

                using var image = new Mat();
                Cv2.Resize(original, image, new OpenCvSharp.Size(), ImageScale, ImageScale, InterpolationFlags.Cubic);

                // Size the detector input to the (upscaled) image, multiples of 32, so nothing gets distorted
                _detector.SetInputParams(
                    scale: 1.0 / 255.0,
                    size: new OpenCvSharp.Size(RoundUp32(image.Width), RoundUp32(image.Height)),
                    mean: new Scalar(122.67891434, 116.66876762, 104.00698793),
                    swapRB: false);

                _detector.DetectTextRectangles(image, out RotatedRect[] rects, out float[] confidences);

                var boxes = rects.Select(r =>
                {
                    OpenCvSharp.Rect b = r.BoundingRect(); // the table is axis-aligned, so use the bounding rect
                    return new TextBox { Bounds = b, Column = Classify(b, image.Width) };
                }).ToList();

                for (int i = 0; i < boxes.Count; i++)
                    _logger.Trace($"Detected {i}: {Describe(boxes[i])} detConfidence={confidences[i]:F3}");

                if (debugImagePath != null)
                    SaveDebugImage(image, boxes, debugImagePath);

                var nameColumn = boxes.Where(b => b.Column == Column.Name).OrderBy(b => b.CenterY).ToList();
                var ranks = boxes.Where(b => b.Column == Column.Rank).ToList();
                var scores = boxes.Where(b => b.Column == Column.Score).ToList();
                double maxDy = MaxNameToAllianceDy * image.Width;

                // Name column alternates name, alliance, name, alliance...
                for (int i = 0; i < nameColumn.Count - 1; i++)
                {
                    TextBox nameBox = nameColumn[i];
                    TextBox allianceBox = nameColumn[i + 1];

                    if (allianceBox.CenterY - nameBox.CenterY > maxDy)
                    {
                        _logger.Trace($"No alliance below, skipping partial row: {Describe(nameBox)}");
                        continue;
                    }

                    i++; // name + alliance consumed as a pair

                    if (TryBuildMember(image, nameBox, allianceBox, ranks, scores, out MemberVSRank? member))
                        members.Add(member!);
                }

                return members.OrderBy(m => m.Rank).ToList();
            }

            private bool TryBuildMember(Mat image, TextBox nameBox, TextBox allianceBox,
                List<TextBox> ranks, List<TextBox> scores, out MemberVSRank? member)
            {
                member = null;
                OpenCvSharp.Size size = image.Size();

                // Rule 2: the full name and alliance must be visible
                if (IsClipped(nameBox.Bounds, size) || IsClipped(allianceBox.Bounds, size))
                {
                    _logger.Trace($"Row skipped, clipped by image edge: name={Describe(nameBox)} alliance={Describe(allianceBox)}");
                    return false;
                }

                string name = CleanName(Ocr(image, nameBox.Bounds, Field.Name));
                string alliance = CleanAlliance(Ocr(image, allianceBox.Bounds, Field.Alliance));

                if (!NameRegex.IsMatch(name) || !AllianceRegex.IsMatch(alliance))
                {
                    _logger.Trace($"Row skipped, unreadable name/alliance: name=\"{name}\" alliance=\"{alliance}\"");
                    return false;
                }

                // Rule 1: rank and score centers must be below the top of the name (and above the alliance bottom)
                double rowTop = nameBox.Bounds.Top;
                double rowBottom = allianceBox.Bounds.Bottom;
                TextBox? rankBox = FindInRow(ranks, rowTop, rowBottom);
                TextBox? scoreBox = FindInRow(scores, rowTop, rowBottom);

                if (rankBox == null || scoreBox == null)
                {
                    _logger.Trace($"Row skipped, missing rank/score: name=\"{name}\" rank={(rankBox != null)} score={(scoreBox != null)}");
                    return false;
                }

                string rankText = Digits(Ocr(image, rankBox.Bounds, Field.Rank));
                string scoreText = Digits(Ocr(image, scoreBox.Bounds, Field.Score));

                if (!int.TryParse(rankText, out int rank) || scoreText.Length == 0)
                {
                    _logger.Trace($"Row skipped, unreadable rank/score: name=\"{name}\" rank=\"{rankText}\" score=\"{scoreText}\"");
                    return false;
                }

                member = new MemberVSRank (rank, name, alliance, scoreText);

                _logger.Trace(
                    $"Row: Rank={rank} Name=\"{name}\" Alliance=\"{alliance}\" Score={scoreText} | " +
                    $"rankRect={rankBox.Bounds} nameRect={nameBox.Bounds} allianceRect={allianceBox.Bounds} scoreRect={scoreBox.Bounds}");
                return true;
            }

            private static TextBox? FindInRow(IEnumerable<TextBox> candidates, double rowTop, double rowBottom)
            {
                double mid = (rowTop + rowBottom) / 2;
                return candidates
                    .Where(c => c.CenterY >= rowTop && c.CenterY <= rowBottom)
                    .OrderBy(c => Math.Abs(c.CenterY - mid))
                    .FirstOrDefault();
            }

            private static Column Classify(OpenCvSharp.Rect r, int imageWidth)
            {
                double left = (double)r.Left / imageWidth;
                double centerX = (r.X + r.Width / 2.0) / imageWidth;

                if (centerX < RankMaxCenterX) return Column.Rank;
                if (left >= NameMinLeftX && left <= NameMaxLeftX) return Column.Name;
                if (left > ScoreMinLeftX) return Column.Score;
                return Column.Other; // avatars, icons, etc.
            }

            private static bool IsClipped(OpenCvSharp.Rect r, OpenCvSharp.Size size) =>
                r.Top <= EdgeMargin || r.Bottom >= size.Height - EdgeMargin ||
                r.Left <= EdgeMargin || r.Right >= size.Width - EdgeMargin;

            // ---------------- OCR ----------------

            private string Ocr(Mat image, OpenCvSharp.Rect bounds, Field field)
            {
                OpenCvSharp.Rect roi = OpenCvSharp.Rect.Intersect(
                    new OpenCvSharp.Rect(bounds.X - 6, bounds.Y - 6, bounds.Width + 12, bounds.Height + 12),
                    new OpenCvSharp.Rect(0, 0, image.Width, image.Height));

                using var crop = new Mat(image, roi);
                using var bin = Binarize(crop, field);
                using var padded = new Mat();
                Cv2.CopyMakeBorder(bin, padded, 12, 12, 12, 12, BorderTypes.Constant, Scalar.White);

                Cv2.ImEncode(".png", padded, out byte[] png);
                using var img = TesseractOCR.Pix.Image.LoadFromMemory(png);

                _tesseract.SetVariable("tessedit_char_whitelist", Whitelist(field)); // see note below
                var psm = field == Field.Rank ? PageSegMode.SingleWord : PageSegMode.SingleLine;
                using var page = _tesseract.Process(img, psm);
                return page.Text.Trim();
            }

            /// Produces black text on a white background.
            private static Mat Binarize(Mat crop, Field field)
            {
                var result = new Mat();

                if (field is Field.Name or Field.Score)
                {
                    // White fill + black outline on a colored row: only the fill is bright in ALL channels,
                    // so take the per-pixel minimum of B, G, R and keep only the very bright pixels.
                    Cv2.Split(crop, out Mat[] ch);
                    using var minBg = new Mat();
                    using var minAll = new Mat();
                    Cv2.Min(ch[0], ch[1], minBg);
                    Cv2.Min(minBg, ch[2], minAll);
                    foreach (var c in ch) c.Dispose();

                    Cv2.Threshold(minAll, result, 230, 255, ThresholdTypes.BinaryInv);
                }
                else
                {
                    // Rank (dark text on light) and alliance (gray text): grayscale + Otsu
                    using var gray = new Mat();
                    Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
                    Cv2.Threshold(gray, result, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

                    // Background should be the majority (white); flip if not
                    if (Cv2.Mean(result).Val0 < 127)
                        Cv2.BitwiseNot(result, result);
                }

                return result;
            }

            private static string Whitelist(Field field) => field switch
            {
                Field.Rank => "0123456789",
                Field.Score => "0123456789,",
                Field.Name => "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 ?",
                _ => "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 []?"
            };

            // ---------------- Cleanup ----------------

            private static string CleanName(string raw)
            {
                string s = Regex.Replace(raw, @"[^A-Za-z0-9 ?]", "");   // drop anything not allowed
                return Regex.Replace(s, @"\s+", " ").Trim();
            }

            private static string CleanAlliance(string raw)
            {
                string s = Regex.Replace(raw, @"\s+", " ").Trim();
                return Regex.Replace(s, @"^(\[[^\]]+\])\s+", "$1");     // "[ORYX] Desert" -> "[ORYX]Desert"
            }

            private static string Digits(string raw)
            {
                // Fix common digit look-alikes, then keep digits only (this also strips the score commas)
                string s = raw.Replace('O', '0').Replace('o', '0').Replace('l', '1')
                              .Replace('I', '1').Replace('|', '1').Replace('S', '5').Replace('B', '8');
                return Regex.Replace(s, @"\D", "");
            }

            // ---------------- Helpers ----------------

            private static int RoundUp32(int v) => (v + 31) / 32 * 32;

            private static string Describe(TextBox b) => $"column={b.Column} rect={b.Bounds}";

            private static void SaveDebugImage(Mat image, List<TextBox> boxes, string path)
            {
                using var canvas = image.Clone();
                foreach (var b in boxes)
                {
                    Scalar color = b.Column switch
                    {
                        Column.Rank => new Scalar(255, 0, 0),   // blue
                        Column.Name => new Scalar(0, 160, 0),   // green
                        Column.Score => new Scalar(0, 0, 255),   // red
                        _ => new Scalar(160, 160, 160)
                    };
                    Cv2.Rectangle(canvas, b.Bounds, color, 2);
                }
                Cv2.ImWrite(path, canvas);
            }

            public void Dispose()
            {
                _detector?.Dispose();
                _tesseract?.Dispose();
            }
        }

    }

    public class MemberVSRank : IEquatable<MemberVSRank?>, IEqualityComparer<MemberVSRank>
    {
        public MemberVSRank()
        {
        }
        public MemberVSRank(int rank, string name, string allianceName, string score)
        {
            Rank = rank;
            Name = name;
            AllianceName = allianceName;
            SetScoreValue(score);
        }
        public MemberVSRank(int rank, string name, string allianceName, int score)
        {
            Rank = rank;
            Name = name;
            AllianceName = allianceName;
            Score = score;
        }

        public int Rank { get; internal set; }
        public string Name { get; internal set; }
        public string AllianceName { get; internal set; }
        public int Score { get; internal set; }

        public override bool Equals(object? obj) => Equals(obj as MemberVSRank);
        public bool Equals(MemberVSRank? other) => other is not null && Name == other.Name && AllianceName == other.AllianceName && Score == other.Score;
        public bool Equals(MemberVSRank? x, MemberVSRank? y)
            => x.Equals(y);
        public override int GetHashCode() => HashCode.Combine(Name, AllianceName, Score);
        public int GetHashCode([DisallowNull] MemberVSRank obj) => obj.GetHashCode();

        public bool SetScoreValue(string score)
        {
            var result = Int32.TryParse(score, out int _score);
            Score = _score;
            return result;
        }
        public override string? ToString() 
            => $"Name: {Name}, Rank: {Rank}, Alliance: {AllianceName}, Score: {Score}";
    }
}