using Microsoft.Extensions.Configuration;
using Sbroenne.WindowsMcp.Capture;
using Sbroenne.WindowsMcp.Models;
using Sbroenne.WindowsMcp.Window;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Windows.Storage.Streams;
using WindowsMCP_Integrated.Core;
using WindowsMCP_Integrated.Core.UI;
using static System.Windows.Forms.AxHost;

namespace WindowsMCP_Integrated
{
    public class GameService : IMouseService
    {
        private const string GameTitle = "LastZ";
        private const string ProcessName = "Survival";

        //Settings for this service
        public string ScreenshotOutputPath { get; internal set; }
        double ratio = 1.0;
        public bool ScreenshotElementWhenClicking
        {
            get => _screenshotElementWhenClicking;
            set
            {
                _screenshotElementWhenClicking = value;
                _mouseService.ScreenshotElementWhenClicking = value;
            }
        }
        private readonly WindowService _windowService;
        private readonly ScreenshotService _screenshotService;
        private readonly TesseractService _tesseractService;
        private readonly MouseService _mouseService;
        private readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        private void SetScalingRatio(int width, int height)
        {

            if (width <= Bounds.Max() && height <= Bounds.Max())
            {
                ScalingRatio = 1.0;
                ElementLocation.ScaleRatio = ScalingRatio;
                return;
            }

            if (width > height)
            {
                ScalingRatio = (double)Bounds.Max() / width;
            }
            else
            {
                ScalingRatio = (double)Bounds.Max() / height;
            }

        }
        public GameService(WindowService windowService, ScreenshotService screenshotService, MouseService mouseService, TesseractService tesseractService, WindowInfoCompact window)
        {
            _windowService = windowService;
            _screenshotService = screenshotService;
            _tesseractService = tesseractService;
            _mouseService = mouseService;
            _mouseService.GameServiceReference = this;
            Bounds = window.Bounds;
            Handle = window.Handle;

        }

        public int[] Bounds { get; internal set; }
        public string Handle { get; internal set; }
        private System.IntPtr HandlePtr => System.IntPtr.Parse(Handle);


        public double ScalingRatio
        {
            get => ratio;
            set
            {
                ratio = value;
                ElementLocation.ScaleRatio = ratio;
            }
        }

        public async Task<bool> IsForegroundAsync()
        {
            var foregroundWindow = await _windowService.GetForegroundWindowAsync();
            if (!foregroundWindow.Success || foregroundWindow.Window == null || foregroundWindow.Window.Handle != Handle)
            {
                return false;
            }

            return foregroundWindow.Window.Handle == Handle;
        }

        public async Task<bool> ValidateWindow()
        {
            var gameWindow = await _windowService.FindWindowAsync(title: GameTitle, processName: ProcessName);
            if (gameWindow.Success && gameWindow.Count > 0 && gameWindow.Windows.Count == 1 && (gameWindow.Windows[0].Handle != Handle) ||
                (gameWindow.Windows[0].Bounds != Bounds))
            {
                Bounds = gameWindow.Windows[0].Bounds;
                SetScalingRatio(GameElements.ReferenceSize.Width, GameElements.ReferenceSize.Height);
                Handle = gameWindow.Windows[0].Handle;
                return true;
            }
            return false;
        }
        private int activateGameWindowAttempts = 0;
        internal async Task<bool> ActivateGameWindow()
        {
            if (activateGameWindowAttempts > 1)
            {
                if (!await ValidateWindow())
                {
                    _logger.Error($"Game window validation failed after {activateGameWindowAttempts} attempts.");
                    return false;
                }

                Helpers.WaitForActivateGameRetry();
            }

            if (activateGameWindowAttempts > 5)
            {
                _logger.Error($"Activate window attempts exceeded");
                return false;
            }
            if (await IsForegroundAsync())
            {
                activateGameWindowAttempts = 0;
                _logger.Trace("No need to activate game window.");
                return true;
            }
            _logger.Trace($"Activating window with handle: {Handle} and bounds: {Bounds[0]},{Bounds[1]},{Bounds[2]},{Bounds[3]}");

            await _windowService.ActivateWindowAsync(HandlePtr);
            activateGameWindowAttempts++;
            _logger.Trace($"Activate game window attempt #{activateGameWindowAttempts}");
            return await ActivateGameWindow();
        }

        public MouseService GetMouseService()
            => _mouseService;


        public async Task<bool> ScrollAndScreenshotTableAsync(ScrollableReadableTable table, string imageName, CancellationToken cancellationToken = default)
        {
            await ActivateGameWindow();
            for (int i = 0; i <= table.EstimatedPartitions; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var getss = await GetScreenshotAsync(
                                            $"{imageName} ({i + 1})"
                                            , table.ReadTopLeft.X, table.ReadTopLeft.Y, table.ReadRegion.Width, table.ReadRegion.Height);

                var success = await _mouseService.ScrollUpTableAsync(
                    table.CursorStartPosition.X
                    , table.CursorStartPosition.Y
                    , table.ScrollMethod().Y
                    );
            }
            return true;
        }

        public bool IncludeTimestampInScreenshotFile = true;
        private bool _screenshotElementWhenClicking = false;

        private string BuildScreenshotFilePath(string imageName)
        {
            return Path.Combine(ScreenshotOutputPath, $"{imageName}{(IncludeTimestampInScreenshotFile ? $"_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}" : "")}");
        }
        public TesseractService GetTesseractService()
            => _tesseractService;
        public bool FullscreenVisionMode = true;
        public async Task<ScreenshotControlResult> GetScreenshot(CancellationToken token)
        {
            await ActivateGameWindow();
            string filePath = BuildScreenshotFilePath("FullWindow");
            var result = await _screenshotService.ExecuteAsync(new ScreenshotControlRequest()
            {
                Action = ScreenshotAction.Capture
                ,
                Target = CaptureTarget.Window, WindowHandle = this.Handle
                , ImageFormat = Sbroenne.WindowsMcp.Models.ImageFormat.Png
    ,
                OutputMode = OutputMode.File
    ,
                OutputPath = filePath
            });
            _logger.Trace($"Screenshot saved to: '{filePath}'");
            return result;
        }
        public async Task<ScreenshotControlResult> GetRegionScreenshotAsync(string imageName, CaptureRegion region)
        {
            await ActivateGameWindow();
            _logger.Trace($"Capturing region screenshot for element: {imageName} with region: ({region})");
            var filePath = BuildScreenshotFilePath(imageName);
            var expandedRegion = new CaptureRegion(region.X - 50, region.Y - 50, region.Width + 100, region.Height + 100);
            var result = await _screenshotService.ExecuteAsync(new ScreenshotControlRequest()
            {
                Action = ScreenshotAction.Capture
                ,
                Target = CaptureTarget.Region
                ,
                ImageFormat = Sbroenne.WindowsMcp.Models.ImageFormat.Png
                ,
                OutputMode = OutputMode.File
                ,
                OutputPath = filePath
                ,
                Region = region
            });
            _logger.Trace($"Screenshot for element: {imageName} saved to: '{filePath}'");
            return result;
        }
        public void PreprocessImageForReading(string imageFilePath)
        {
            
            var newFile = Helpers.ModifyFileName(imageFilePath, "", ".preprocessed");

            using (var imgProcessor = new ImageProcessing())
                imgProcessor.ReadFile(imageFilePath, OpenCvSharp.ImreadModes.Color)
                    .Normalize(OpenCvSharp.ColorConversionCodes.BGR2GRAY)
                    .Upscale()
                    .ReduceNoise()
                    .CreateBinaryMask(threshold: GameElements.Overlay.HeaderTextBinaryMaskThreshold)
                    .PadEdges()
                    .CleanBinaryMask()
                    .SaveFileAs(newFile);
        }
        public void PreprocessImageForReadingWithDnn(string imageFilePath)
        {
            _logger.Trace($"Processing Image for Reading: {imageFilePath}");

            var newFile = Helpers.ModifyFileName(imageFilePath, "", ".processedTR");
            using (var imgProcessor = new ImageProcessing())
                imgProcessor.ProcessImageWithTextModel(imageFilePath, newFile);
        }

        public async Task<ScreenshotControlResult> GetScreenshotAsync(string imageName, ElementLocation element)
        {
            await ActivateGameWindow();
            Helpers.ThrottleThread(1000);
            var result = await GetRegionScreenshotAsync(imageName
                , new CaptureRegion(element.X, element.Y, element.Size.Width, element.Size.Height)
                );
            return result;
        }

        public async Task<ScreenshotControlResult> GetScreenshotAsync(string imageName, int X, int Y, int Width, int Height)
        {
            return await GetRegionScreenshotAsync(imageName
                , new CaptureRegion(X, Y, Width, Height));
        }

        public async Task<bool> ReadHeaderText()
        {
            var overlayHeaderImage = await GetScreenshotAsync(nameof(GameElements.Overlay.HeaderText), GameElements.Overlay.HeaderText);
            if (!overlayHeaderImage.Success)
            {
                _logger.Error($"Failed to capture overlay header image: {overlayHeaderImage.ErrorCode}");
                return true;
            }
            if (!Path.Exists(overlayHeaderImage.FilePath))
            {
                _logger.Error($"Overlay header image file does not exist: {overlayHeaderImage.FilePath}");
                return true;
            }

            var imgProcessor = new ImageProcessing();

            imgProcessor.ReadFile(overlayHeaderImage.FilePath, OpenCvSharp.ImreadModes.Color)
                .Normalize(OpenCvSharp.ColorConversionCodes.BGR2GRAY)
                .Upscale()
                .ReduceNoise()
                .CreateBinaryMask(threshold: GameElements.Overlay.HeaderTextBinaryMaskThreshold, saveOutput: true)
                .PadEdges()
                .CleanBinaryMask(saveOutput: true)
                ;

            return true;
        }

        public async Task<string> GetOverlayHeaderText(string? expectedValue = null)
        {
            var overlayHeaderImage = await GetScreenshotAsync(expectedValue ?? "Overlay.Header.png", GameElements.Overlay.HeaderText);
            if (overlayHeaderImage.Success)
            {
                return _tesseractService.ExtractTextFromImage(overlayHeaderImage.FilePath);
            }

            return string.Empty;
        }

        public void SetScreenshotOutputPath(string outputDir) => ScreenshotOutputPath = outputDir;
        public async Task<bool> ClickAndForgetAsync()
        {
            if(!await ActivateGameWindow())
            {
                return false;
            }
            return await ((IMouseService)_mouseService).ClickAndForgetAsync();
        }

        public async Task<bool> ClickElementAsync(ElementLocation element, int retryTimes, int retryDelay, Func<Task<bool>> waitUntil, CancellationToken cancellationToken = default)
        {
            if (!await ActivateGameWindow())
            {
                return false;
            }
            return await ((IMouseService)_mouseService).ClickElementAsync(element, retryTimes, retryDelay, waitUntil, cancellationToken);
        }
        public async Task<bool> ClickPositionAndForgetAsync(ElementLocation element, CancellationToken cancellationToken = default)
        {
            if (!await ActivateGameWindow())
            {
                return false;
            }
            return await ((IMouseService)_mouseService).ClickPositionAndForgetAsync(element, cancellationToken);
        }

        public async Task<bool> ClickPositionAndForgetAsync(int? x, int? y)
        {
            if (!await ActivateGameWindow())
            {
                return false;
            }
            return await ((IMouseService)_mouseService).ClickPositionAndForgetAsync(x, y);
        }
        public async Task<bool> ClickPositionAndForgetAsync(System.Drawing.Point? point, bool antiDetect = false)
        {
            if (!await ActivateGameWindow())
            {
                return false;
            }
            return await ((IMouseService)_mouseService).ClickPositionAndForgetAsync(point, antiDetect);
        }
        public async Task<bool> DragMouseAsync(int startX, int startY, int endX, int endY)
        {
            if (!await ActivateGameWindow())
            {
                return false;
            }
            return await ((IMouseService)_mouseService).DragMouseAsync(startX, startY, endX, endY);
        }
        public async Task<bool> ScrollUpTableAsync(int startX, int startY, int height)
        {
            if (!await ActivateGameWindow())
            {
                return false;
            }
            return await ((IMouseService)_mouseService).ScrollUpTableAsync(startX, startY, height);
        }
        public async Task<bool> SetCursorPositionAsync(int x, int y, CancellationToken token)
        {
            if (!await ActivateGameWindow())
            {
                return false;
            }
            return await ((IMouseService)_mouseService).SetCursorPositionAsync(x, y, token);
        }
    }



}
