using OpenCvSharp.ImgHash;
using Sbroenne.WindowsMcp.Capture;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TesseractOCR;
using TesseractOCR.Enums;
using NLog;

namespace WindowsMCP_Integrated
{
    public class TesseractService
    {
        private readonly ImageProcessor _imageProcessor;
        private readonly string _trainedDataPath;
        private readonly TesseractOCR.Loggers.Stream _logger;

        public TesseractService(ImageProcessor imageProcessor, string? trainedDataPath = null, string? logFilePath = null)
        {
            _imageProcessor = imageProcessor;
            _trainedDataPath = trainedDataPath
                ?? Environment.GetEnvironmentVariable("TESSDATA_PREFIX")
                ?? @"D:\Development\Tesseract";
            if (!Directory.Exists(_trainedDataPath))
            {
                throw new DirectoryNotFoundException($"Tesseract data directory was not found: {_trainedDataPath}");
            }

            logFilePath ??= Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Last Z Client-o-matic",
                "tesseract.log");
            var logDirectory = Path.GetDirectoryName(logFilePath);
            if (!string.IsNullOrEmpty(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }
            _logger = new TesseractOCR.Loggers.Stream(
                File.Open(logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read));
        }
        public bool ProcessTablesDirectory(string imagePath, string outputFilePath)
        {
            var files = Directory.GetFiles(imagePath, "*.png");

            using var e = new TesseractOCR.Engine(_trainedDataPath, "eng", EngineMode.Default, logger: _logger);
            e.SetVariable("preserve_interword_spaces", 1);
            e.SetVariable("load_freq_dawg", 0);
            e.SetVariable("load_system_dawg", 0);
            e.SetVariable("user_defined_dpi", 300);
            e.TryPrintVariablesToFile(Path.Combine(_trainedDataPath, $"tesseract_variables_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}.txt"));
            var readRows = new List<string[]>();
            foreach (var file in files)
            {
                readRows.AddRange(TesseractVSRankingTableRead.ReadTable(file, debug: false, e));
            }

            CreateVSTableCsvFile(outputFilePath, readRows);
            return true;
        }

        private static void CreateVSTableCsvFile(string outputFilePath, List<string[]> readRows)
        {
            using (var w = new StreamWriter(outputFilePath, false, new UTF8Encoding(true)))
            {
                w.WriteLine("Rank,Username,Alliance,Score,VS Day,Check,Source");
                foreach (var c in readRows) w.WriteLine(string.Join(",", c.Select(CsvEsc)));
            }
        }

        public string ExtractTextFromImage(string imagePath, PageSegMode pageSegMode = PageSegMode.SingleLine)
        {
            using (var engine = new TesseractOCR.Engine(_trainedDataPath, "eng", EngineMode.Default, logger: _logger))
            {
                string resultText = string.Empty;
                using (var img = TesseractOCR.Pix.Image.LoadFromFile(imagePath))
                using (var page = engine.Process(img, pageSegMode))
                    resultText = page.Text;

                return resultText;
            }
        }

        static string CsvEsc(string s) =>
        s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    }

    public class TesseractVSRankingTableRead
    {

        private static readonly ILogger _logger = NLog.LogManager.GetCurrentClassLogger();
        public static List<string[]> ReadTable(string file, bool debug, Engine engine)
        {
            var rows = new List<string[]>();
            using var img = new Bitmap(file);
            var foundBoxes = Boxes.Find(img).ToList();
            if (foundBoxes.Count == 0)
            {
                rows.Add(GetRowArray("", "", "", "", "no data", Path.GetFileName(file)));
                return rows;
            }

            string stem = Path.GetFileNameWithoutExtension(file);
            string source = Path.GetFileName(file);
            string dbgDir = Path.GetDirectoryName(file);
            double kx = img.Width / Cfg.RefW;
            int prevRank = -1;
            for (int i = 0; i < foundBoxes.Count; i++)
            {
                var (top, bottom) = foundBoxes[i];
                rows.Add(ReadRow(img, top, bottom, kx, stem, dbgDir, source, i, debug, engine, ref prevRank));
            }

            return rows;
        }

        private static string[] ReadRow(Bitmap img, int top, int bottom, double kx, string stem, string dbgDir,
            string source, int index, bool debug, Engine engine, ref int prevRank)
        {
            double ky = (bottom - top + 1) / Cfg.RefBoxH;
            var (rankTxt, rankC) = ReadField(img, Fields.Rank, top, kx, ky, stem, dbgDir, index, debug, engine);
            var (userTxt, userC) = ReadField(img, Fields.User, top, kx, ky, stem, dbgDir, index, debug, engine);
            var (scoreTxt, scoreC) = ReadField(img, Fields.Score, top, kx, ky, stem, dbgDir, index, debug, engine);

            string ally = Fields.Alliance;
            if (ally == null)
                (ally, _) = ReadField(img, Fields.Ally, top, kx, ky, stem, dbgDir, index, debug, engine);

            string rankDigits = Regex.Replace(rankTxt, "[^0-9]", "");
            string scoreDigits = Regex.Replace(scoreTxt, "[^0-9]", "");
            string score = scoreDigits.Length > 0 && scoreDigits.Length < 19
                ? long.Parse(scoreDigits).ToString("N0", CultureInfo.InvariantCulture) : "?";

            var flags = new List<string>();
            if (rankDigits == "" || rankC < Cfg.LowConf) flags.Add("rank");
            else if (prevRank >= 0 && int.Parse(rankDigits) != prevRank + 1) flags.Add("rank-sequence");
            if (userTxt == "" || userC < Cfg.LowConf) flags.Add("username");
            if (scoreDigits == "" || scoreC < Cfg.LowConf) flags.Add("score");
            if (rankDigits != "") prevRank = int.Parse(rankDigits);

            string joinedFlags = string.Join(";", flags);
            _logger.Debug("Read row {0}: Rank={1}, User={2}, Ally={3}, Score={4}, Flags={5}, Source={6}",
                index + 1, rankDigits, userTxt, ally, score, joinedFlags, source);
            return GetRowArray(rankDigits, userTxt, ally, score, joinedFlags, source);
        }

        private static (string text, double confidence) ReadField(Bitmap img, Field field, int top, double kx,
            double ky, string stem, string dbgDir, int index, bool debug, Engine engine)
        {
            var rect = new Rectangle((int)(field.X0 * kx), top + (int)(field.Y0 * ky),
                (int)((field.X1 - field.X0) * kx), (int)((field.Y1 - field.Y0) * ky));
            var crop = Prep.Crop(img, rect, field.LightText, debug, dbgDir, $"{stem}_{index}_{field.Name}");
            return Read(engine, crop, field.PageSegMode, field.Whitelist);
        }

        private static string[] GetRowArray(string rank, string username, string alliance, string score, string flags, string source)
        {
            var badRead = rank == "" || username == "" || alliance == "";
            return new[] { rank == "" ? "?" : rank, username == "" ? "?" : username, alliance == "" ? "?" : alliance, score, badRead ? "bad read": "", badRead ? source : "" };
        }
        // ---------- OCR ----------
        static (string text, double conf) Read(Engine engine, Bitmap bmp, PageSegMode psm, string whitelist)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            bmp.Dispose();

            engine.SetVariable("tessedit_char_whitelist", whitelist);   // "" clears the whitelist
            using var pix = TesseractOCR.Pix.Image.LoadFromMemory(ms.ToArray());
            using var page = engine.Process(pix, psm);

            string text = Regex.Replace(page.Text ?? "", @"\s*[\r\n]+\s*", " ").Trim();
            double conf = text == "" ? 0 : page.MeanConfidence * 100;   // MeanConfidence is 0..1
            return (text, conf);
        }



        // ---------- config ----------
        internal record Field(string Name, int X0, int X1, int Y0, int Y1, bool LightText,
            PageSegMode PageSegMode, string Whitelist);

        internal static class Fields
        {
            public const string Alliance = "[FuKR]SuperFuKRs";       // set to null to OCR the alliance

            // x range is relative to a 420-px-wide image; y range is relative to the box's top edge
            public static readonly Field Rank = new("rank", 28, 90, 22, 58, false, PageSegMode.SingleWord, "0123456789");
            public static readonly Field User = new("user", 151, 290, 6, 34, true, PageSegMode.SingleLine, "");
            public static readonly Field Ally = new("ally", 158, 290, 38, 64, false, PageSegMode.SingleLine, "");
            public static readonly Field Score = new("score", 290, 412, 14, 62, true, PageSegMode.SingleLine, "0123456789,");
        }

        internal static class Cfg
        {
            public const double RefW = 420, RefBoxH = 76;
            public const int ProbeX = 35, MinBoxH = 66, MaxBoxH = 90;
            public const int LowConf = 80;                            // flag fields below this
        }

        // ---------- box detection: complete boxes only ----------
        static class Boxes
        {
            static bool IsBoxPixel(Color c) =>
                (c.R + c.G + c.B) / 3 > 195 || (c.G > c.R + 25 && c.G > c.B + 25 && c.G > 120); // beige or green highlight

            public static IEnumerable<(int top, int bottom)> Find(Bitmap img)
            {
                int x = Math.Min(Cfg.ProbeX, img.Width - 1), start = -1;
                for (int y = 0; y <= img.Height; y++)
                {
                    var checkedPixel = y;
                    _logger.Trace("Checking pixel at ({0}, {1}): {2}", x, y, checkedPixel);
                    bool box = y < img.Height && IsBoxPixel(img.GetPixel(x, y));
                    if (box && start < 0) start = y;
                    if (!box && start >= 0)
                    {
                        int end = y - 1, h = end - start + 1;
                        if (start > 0 && end < img.Height - 1 && h >= Cfg.MinBoxH && h <= Cfg.MaxBoxH)
                            yield return (start, end);
                        start = -1;
                    }
                }
            }
        }

        // ---------- preprocessing ----------
        static class Prep
        {
            // Crop, upscale 3x, binarize to black text on white.
            // lightText = true for white-with-black-outline text (username, score).
            public static Bitmap Crop(Bitmap src, Rectangle r, bool lightText, bool debug, string dir, string name)
            {
                r.Intersect(new Rectangle(0, 0, src.Width, src.Height));
                const int S = 3, Pad = 12;
                using var up = new Bitmap(r.Width * S, r.Height * S);
                using (var g = Graphics.FromImage(up))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(src, new Rectangle(0, 0, up.Width, up.Height), r, GraphicsUnit.Pixel);
                }
                var o = new Bitmap(up.Width + 2 * Pad, up.Height + 2 * Pad);
                using (var g = Graphics.FromImage(o)) g.Clear(Color.White);
                for (int y = 0; y < up.Height; y++)
                    for (int x = 0; x < up.Width; x++)
                    {
                        var c = up.GetPixel(x, y);
                        bool ink = lightText ? Math.Min(c.R, Math.Min(c.G, c.B)) > 238
                                             : (c.R + c.G + c.B) / 3 < 165;
                        o.SetPixel(x + Pad, y + Pad, ink ? Color.Black : Color.White);
                    }
                if (debug) o.Save(Path.Combine(dir, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                return o;
            }
        }
    }
}
