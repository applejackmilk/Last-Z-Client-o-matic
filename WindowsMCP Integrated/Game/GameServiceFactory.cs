using Sbroenne.WindowsMcp.Automation;
using Sbroenne.WindowsMcp.Capture;
using Sbroenne.WindowsMcp.Input;
using Sbroenne.WindowsMcp.Window;

namespace WindowsMCP_Integrated;

public static class GameServiceFactory
{
    public static async Task<GameService> CreateForCurrentGameAsync(
        string screenshotOutputPath,
        bool screenshotElementWhenClicking = false,
        string? tesseractDataPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screenshotOutputPath);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(screenshotOutputPath);

        var monitorService = new MonitorService();
        var secureDesktopDetector = new SecureDesktopDetector();
        var imageProcessor = new ImageProcessor();
        var screenshotService = new ScreenshotService(monitorService, secureDesktopDetector, imageProcessor);
        var elevationDetector = new ElevationDetector();
        var windowEnumerator = new WindowEnumerator(elevationDetector);
        var windowActivator = new WindowActivator();
        var windowService = new WindowService(
            windowEnumerator,
            windowActivator,
            monitorService,
            secureDesktopDetector);
        var mouseInputService = new MouseInputService();
        var tesseractService = new TesseractService(imageProcessor, tesseractDataPath);

        ImageProcessing.SetConfiguration(screenshotOutputPath);
        ImageProcessing.StageCompletionCallback = filePath => tesseractService.ExtractTextFromImage(filePath);

        var gameWindow = await windowService.FindWindowAsync(title: "LastZ", processName: "Survival");
        cancellationToken.ThrowIfCancellationRequested();
        if (!gameWindow.Success || gameWindow.Count != 1)
        {
            throw new InvalidOperationException("The LastZ game window was not found or was ambiguous.");
        }

        var gameService = new GameService(
            windowService,
            screenshotService,
            mouseInputService,
            tesseractService,
            gameWindow.Windows[0])
        {
            ScreenshotOutputPath = screenshotOutputPath,
            ScreenshotElementWhenClicking = screenshotElementWhenClicking
        };

        return gameService;
    }
}
