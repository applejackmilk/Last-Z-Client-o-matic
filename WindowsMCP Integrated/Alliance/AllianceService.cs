using NLog;
using Sbroenne.WindowsMcp.Models;
using System.Diagnostics;
using WindowsMCP_Integrated.Core;
using WindowsMCP_Integrated.Core.UI;

namespace WindowsMCP_Integrated.Alliance;

public class AllianceService(GameService _gameService)
{
    private readonly ILogger _logger = NLog.LogManager.GetCurrentClassLogger();
    private static readonly ElementLocation _bountyMapButton = ElementLocation.Create(name: "Bounty Map Button", centerX: 960, centerY: 523, width: 25, height: 25, container: ContainerName.WorldMap_Bounty);
    private static readonly ElementLocation _bountyMapHelp = ElementLocation.Create(name: "Bounty Map Help", centerX: 961, centerY: 601, width: 25, height: 25, container: ContainerName.WorldMap_Bounty);

    public async Task<bool> OpenAllianceScreen()
    {
        if (await _gameService.GetOverlayHeaderText(nameof(GameElements.Main.AllianceButton)) == "Alliance")
        {
            return true; // Already on the Alliance screen, no need to click
        }

        var clickResult = await _gameService.GetMouseService().ClickElementAsync(element: GameElements.Main.AllianceButton
            , retryTimes: 3, retryDelay: 1000, waitUntil: async () =>
            {
                var headerText = await _gameService.GetOverlayHeaderText(nameof(GameElements.Main.AllianceButton));
                return headerText == "Alliance";
            });
        return clickResult;
    }
    public async Task<bool> RapidClickHeli(Point? location, int? duration = 5)
    {
        var stopwatch = Stopwatch.StartNew();

        Point? point = location.GetValueOrDefault(new Point(UtilityCursorPositions.Center.ClickX, UtilityCursorPositions.Center.ClickY));
        if (!location.HasValue)
        {
            point = new Point(UtilityCursorPositions.Center.ClickX, UtilityCursorPositions.Center.ClickY);
        }
        else
        {
            point = location.Value;
        }

        while (stopwatch.Elapsed.TotalSeconds < duration.GetValueOrDefault(5))
        {
            await _gameService.GetMouseService().ClickPositionAndForgetAsync(point, true);
            Helpers.ThrottleFastClick(duration.GetValueOrDefault(5) - (int)stopwatch.Elapsed.TotalSeconds);
        }

        return true;
    }

    public async Task<bool> RapidClickBounty(int? duration = 5)
    {
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed.TotalSeconds < duration.GetValueOrDefault(5))
        {
            await _gameService.GetMouseService().ClickPositionAndForgetAsync(_bountyMapButton.Center, true);
            Helpers.ThrottleFastClick((int)stopwatch.Elapsed.TotalSeconds);
            await _gameService.GetMouseService().ClickPositionAndForgetAsync(_bountyMapHelp.Center, true);
            Helpers.ThrottleFastClick((int)stopwatch.Elapsed.TotalSeconds);
        }
        return true;
    }
    public async Task CaptureAllianceScreenshot()
    {
        var screenshotTask = await _gameService.GetScreenshotAsync(
            imageName: nameof(GameElements.Main.AllianceButton)
            , element: GameElements.Main.AllianceButton);
        if (screenshotTask.Success)
        {
            _logger.Info($"Screenshot captured successfully to: {screenshotTask.FilePath}");
        }
        else
        {
            _logger.Error($"Failed to capture screenshot: {screenshotTask.ErrorCode}");
        }
        _logger.Trace($"Screenshot result: {screenshotTask.Message}");
    }
}
