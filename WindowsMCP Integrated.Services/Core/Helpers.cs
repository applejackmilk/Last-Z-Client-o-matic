using NLog;
using System.Diagnostics;
using System.Text;
using System.Windows;
using DrawingPoint = System.Drawing.Point;

namespace WindowsMCP_Integrated.Core
{
    public static class Helpers
    {
        private static readonly ILogger _logger = NLog.LogManager.GetCurrentClassLogger();

        internal static DrawingPoint? ParsePosition(string input)
        {
            var normalized = input.Trim();
            var separatorIndex = normalized.IndexOfAny(new[] { 'x', 'X' });
            if (separatorIndex <= 0 || separatorIndex >= normalized.Length - 1)
            {
                return null;
            }

            var xText = normalized.Substring(0, separatorIndex);
            var yText = normalized.Substring(separatorIndex + 1);
            if (int.TryParse(xText, out var x) && int.TryParse(yText, out var y))
            {
                return new DrawingPoint(x, y);
            }

            return null;
        }

        internal static void ThrottleThread(int duration)
        {
            _logger.Trace("Throttling thread for {throttleDelay}", duration);
            Thread.Sleep(duration);
        }
        internal static void ThrottleNewMouseActionType()
        {
            ThrottleThread(ConfigSettings.MouseActionTypeDetail);
        }

        internal static void ThrottleFastClick(int max)
        {
            var tmpRndTime = Random.Shared.Next(0 + Random.Shared.Next(0, 20), 300);
            ThrottleThread(tmpRndTime);
            Debug.WriteLine($"{tmpRndTime} from max {max}");
        }

        internal static void WaitForActivateGameRetry()
        {
            ThrottleThread(1000);
        }

        public static string ModifyFileName(string filePath, string prefix, string suffix)
        {
            string dir = Path.GetDirectoryName(filePath);
            string name = Path.GetFileNameWithoutExtension(filePath);
            string ext = Path.GetExtension(filePath);
            return Path.Combine(dir, $"{name}{suffix}{ext}");
        }

        public static bool ToCsvFile(string filePath, string[] header, List<string[]> rows)
        {
            using (var w = new StreamWriter(filePath, false, new UTF8Encoding(true)))
            {
                if(header != null)
                {
                    rows.Insert(0, header);
                }
                foreach (var c in rows) w.WriteLine(string.Join(",", c.Select(CsvEsc)));
            }
            return true;
        }

        static string CsvEsc(string s) =>
            s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;


    }

    public static class ConfigSettings
    {
        public static int MouseActionTypeDetail = 500;
    }
}