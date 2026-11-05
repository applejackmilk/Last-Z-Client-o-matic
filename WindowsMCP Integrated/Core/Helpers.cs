using System.Diagnostics;
using DrawingPoint = System.Drawing.Point;
using NLog;

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
            var tmpRndTime = 15 + Random.Shared.Next(0, 200 / (1 + Math.Max(1, max)));
            ThrottleThread(tmpRndTime);
        }

        internal static void WaitForActivateGameRetry()
        {
            ThrottleThread(1000);
        }
    }

public static class ConfigSettings
    {
        public static int MouseActionTypeDetail = 500;
    }
}