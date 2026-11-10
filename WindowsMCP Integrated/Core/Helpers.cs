using NLog;
using System.Diagnostics;
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
            var tmpRndTime = Random.Shared.Next(150 - Random.Shared.Next(10, 20), 999);
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