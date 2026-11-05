using Sbroenne.WindowsMcp.Models;
using WindowsMCP_Integrated.Core;
using WindowsMCP_Integrated.Core.UI;

namespace WindowsMCP_Integrated.Alliance
{
    public class AllianceService
    {
        private readonly GameService _gameService;
        public AllianceService(GameService gameService)
        {
            _gameService = gameService;
        }
        public async Task<bool> OpenAllianceScreen()
        {
            if(await _gameService.GetOverlayHeaderText(nameof(GameElements.Main.AllianceButton)) == "Alliance")
            {
                return true; // Already on the Alliance screen, no need to click
            }

            var clickResult = await _gameService.ClickElementAsync(element: GameElements.Main.AllianceButton
                , retryTimes: 3, retryDelay: 1000, waitUntil: async () =>
                {
                    var headerText = await _gameService.GetOverlayHeaderText(nameof(GameElements.Main.AllianceButton));
                    Console.WriteLine($"Overlay header text after click: {headerText}");
                    return headerText == "Alliance";
                });
            return clickResult;
        }

        public async Task CaptureAllianceScreenshot()
        {
            var screenshotTask = await _gameService.GetScreenshotAsync(
                imageName: nameof(GameElements.Main.AllianceButton)
                , element: GameElements.Main.AllianceButton);
            if (screenshotTask.Success)
            {
                Console.WriteLine($"Screenshot captured successfully to: {screenshotTask.FilePath}");
            }
            else
            {
                Console.WriteLine($"Failed to capture screenshot: {screenshotTask.ErrorCode}");
            }
            Console.WriteLine($"Screenshot result: {screenshotTask.Message}");
        }
    }


}
