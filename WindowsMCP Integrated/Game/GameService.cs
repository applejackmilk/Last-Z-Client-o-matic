using Sbroenne.WindowsMcp.Capture;
using Sbroenne.WindowsMcp.Input;
using Sbroenne.WindowsMcp.Models;
using Sbroenne.WindowsMcp.Window;
using WindowsMCP_Integrated.Core;
using WindowsMCP_Integrated.Core.UI;
using static System.Windows.Forms.AxHost;

namespace WindowsMCP_Integrated
{
    public class GameService
    {
        private const string GameTitle = "LastZ";
        private const string ProcessName = "Survival";
        
        //Settings for this service
        public string ScreenshotOutputPath { get; internal set; }
        double ratio = 1.0;
        public bool ScreenshotElementWhenClicking { get; set; } = false;

        private readonly WindowService _windowService;
        private readonly ScreenshotService _screenshotService;
        private readonly MouseInputService _mouseInputService;
        private readonly TesseractService _tesseractService;

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
        public GameService(WindowService windowService, ScreenshotService screenshotService, MouseInputService mouseInputService, TesseractService tesseractService, WindowInfoCompact window)
        {
            _windowService = windowService;
            _screenshotService = screenshotService;
            _mouseInputService = mouseInputService;
            _tesseractService = tesseractService;
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
            /*
                        if(await ActivateGameWindow())
                        {
                            return true;
                        }
            */
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
                if(!await ValidateWindow())
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
            _logger.Trace($"Activating window with handle: {Handle} and bounds: {Bounds[0]},{Bounds[1]},{Bounds[2]},{Bounds[3]}");
            if (await IsForegroundAsync())
            {
                activateGameWindowAttempts = 0;
                return true;
            }

            await _windowService.ActivateWindowAsync(HandlePtr);
            activateGameWindowAttempts++;
            _logger.Trace($"Activate game window attempt #{activateGameWindowAttempts}");
            return await ActivateGameWindow();
        }

        internal async Task<bool> ScrollAndScreenshotTableAsync(ScrollableReadableTable table, string imageName, CancellationToken cancellationToken = default)
        {
            for (int i = table.EstimatedPartitions-1; i <= table.EstimatedPartitions; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var getss = await GetScreenshotAsync(
                                            $"{imageName} ({i + 1})"
                                            , table.ReadTopLeft.X, table.ReadTopLeft.Y, table.ReadRegion.Width, table.ReadRegion.Height);

                var success = await ScrollUpTableAsync(
                    table.CursorStartPosition.X
                    , table.CursorStartPosition.Y
                    , table.ScrollMethod().Y
                    );
            }
            return true;
        }

        internal async Task<bool> DragMouseAsync(int startX, int startY, int endX, int endY)
        {
            await ActivateGameWindow();
            var dragger = await _mouseInputService.DragAsync(startX, startY, endX, endY);
            if (!dragger.Success)
            {
                _logger.Error($"Failed. Suggested mouse drag: {dragger.RecoverySuggestion}");
            }
            return dragger.Success;
        }

        internal async Task<bool> ScrollUpTableAsync(int startX, int startY, int height)
        {
            await ActivateGameWindow();
            _mouseInputService.SetDragSpeed(true);
            var dragger = await _mouseInputService.DragAsync(startX, startY, startX, startY - height);
            if (!dragger.Success)
            {
                _logger.Error($"Failed. Suggested mouse drag: {dragger.RecoverySuggestion}");
            }
            return dragger.Success;
        }

        internal async Task<bool> ClickPositionAndForgetAsync(System.Drawing.Point? point, bool antiDetect = false)
        {
            _logger.Trace($"Clicking mouse {point} with anti-detect at {antiDetect}");
            MouseControlResult? clickResult;
            if (point.HasValue)
            {
                clickResult = await ClickAsyncCore(point.Value.X + (antiDetect ? +Random.Shared.Next(-4, 4) : 0), point.Value.Y + (antiDetect ? +Random.Shared.Next(-4, 4) : 0));
            }
            else
            {
                clickResult = await ClickAsyncCore(null, null);
            }
            return clickResult.Success;
        }

        internal async Task<bool> ClickPositionAndForgetAsync(int? x, int? y)
        {
            _logger.Trace($"Clicking mouse {x}x{y}");
            var clickResult = await ClickAsyncCore(x, y);
            return clickResult.Success;
        }


        internal async Task<bool> ClickPositionAndForgetAsync(ElementLocation element, CancellationToken cancellationToken = default)
        {
            await ActivateGameWindow();
            _logger.Trace($"Clicking element: {element.Name} at ({element.ClickX}, {element.ClickY}) with size ({element.Size.Width}, {element.Size.Height})");
            var clickResult = await ClickAsyncCore(element, cancellationToken);
            return clickResult.Success;
        }

        internal async Task<bool> ClickAndForgetAsync()
        {
            _logger.Trace($"Clicking at current position");
            var clickResult = await ClickAsyncCore(null, null);
            return clickResult.Success;
        }
        private async Task<MouseControlResult> ClickAsyncCore(ElementLocation element, CancellationToken cancellationToken = default)
        {
            int randomClickId = Random.Shared.Next(Int32.MinValue, Int32.MaxValue);
            if (ScreenshotElementWhenClicking)
            {
                var screenshotResult = await GetScreenshotAsync($"{element.Name} {randomClickId} Before Click", element);
                if (!screenshotResult.Success)
                {
                    _logger.Error($"Failed to capture screenshot for element: {element.Name}. Error: {screenshotResult.ErrorCode}");
                }
            }

            var clickResult = await ClickAsyncCore(element.ClickX, element.ClickY, cancellationToken);
            if (!clickResult.Success)
            {
                _logger.Error("Failed to click on element.");
            }
            _logger.Trace($"Element clicked successfully at the location: {clickResult.FinalPosition.X}x{clickResult.FinalPosition.Y}");

            await Task.Delay(ConfigSettings.MouseActionTypeDetail, cancellationToken);
            if (ScreenshotElementWhenClicking)
            {
                var screenshotResult = await GetScreenshotAsync($"{element.Name} {randomClickId} Post Click", element);
                if (!screenshotResult.Success)
                {
                    _logger.Error($"Failed to capture screenshot for element: {element.Name}. Error: {screenshotResult.ErrorCode}");
                }
            }

            return clickResult;
        }

        private async Task<MouseControlResult> ClickAsyncCore(int? x, int? y, CancellationToken? token = null)
        {
            await ActivateGameWindow();
            _logger.Trace($"Clicking mouse at {x}x{y}");
            var clickResult = await _mouseInputService.ClickAsync(x: x, y: y, cancellationToken: token ?? CancellationToken.None);
            return clickResult;
        }

        internal async Task<bool> ClickElementAsync(ElementLocation element, int retryTimes, int retryDelay, Func<Task<bool>> waitUntil, CancellationToken cancellationToken = default)
        {
            _logger.Trace($"Clicking element: {element.Name} at ({element.ClickX}, {element.ClickY}) with size ({element.Size.Width}, {element.Size.Height})");
            var result = await ClickAsyncCore(element, cancellationToken);
            int attempts = 0;
            while (attempts < retryTimes)
            {
                await Task.Delay(retryDelay, cancellationToken);
                attempts++;
                if (await waitUntil())
                {
                    return true;
                }
                _logger.Warn($"Wait criteria not met after {attempts} attempts.");
            }
            return false;
        }


        public bool IncludeTimestampInScreenshotFile = true;
        private string BuildScreenshotFilePath(string imageName)
        {
            return Path.Combine(ScreenshotOutputPath, $"{imageName}{(IncludeTimestampInScreenshotFile ? $"_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}" : "")}");
        }
        internal TesseractService GetTesseractService()
            => _tesseractService;
        internal async Task<ScreenshotControlResult> GetRegionScreenshotAsync(string imageName, CaptureRegion region)
        {
            await ActivateGameWindow();
            _logger.Trace($"Capturing region screenshot for element: {imageName} with region: ({region})");

            return await _screenshotService.ExecuteAsync(new ScreenshotControlRequest()
            {
                Action = ScreenshotAction.Capture
                ,
                Target = CaptureTarget.Region
                ,
                ImageFormat = Sbroenne.WindowsMcp.Models.ImageFormat.Png
                ,
                OutputMode = OutputMode.File
                ,
                OutputPath = BuildScreenshotFilePath(imageName)
                ,
                Region = region
            });
        }
        public void PreprocessImageForReading(string imageFilePath)
        {
            var newFile = imageFilePath.Insert(imageFilePath.Length - Path.GetExtension(imageFilePath).Count(), ".preprocessed");

            using (var imgProcessor = new ImageProcessing(c => _logger.Debug(c)))
                imgProcessor.ReadFile(imageFilePath, OpenCvSharp.ImreadModes.Color)
                    .Normalize(OpenCvSharp.ColorConversionCodes.BGR2GRAY)
                    .Upscale()
                    .ReduceNoise()
                    .CreateBinaryMask(threshold: GameElements.Overlay.HeaderTextBinaryMaskThreshold)
                    .PadEdges()
                    .CleanBinaryMask()
                    .SaveFileAs(newFile)
                    ;
        }
        internal async Task<ScreenshotControlResult> GetScreenshotAsync(string imageName, ElementLocation element)
        {
            _logger.Trace($"Capturing screenshot for element: {element.Name}");
            var result = await GetRegionScreenshotAsync(imageName
                , new CaptureRegion(element.X, element.Y, element.Size.Width, element.Size.Height)
                );
            return result;
        }

        internal async Task<ScreenshotControlResult> GetScreenshotAsync(string imageName, int X, int Y, int Width, int Height)
        {
            return await GetRegionScreenshotAsync(imageName
                , new CaptureRegion(X, Y, Width, Height));
        }

        internal async Task<bool> ReadHeaderText()
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

            var imgProcessor = new ImageProcessing(c => _logger.Debug(c));

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

        internal async Task<string> GetOverlayHeaderText(string? expectedValue = null)
        {
            var overlayHeaderImage = await GetScreenshotAsync(expectedValue ?? "Overlay.Header.png", GameElements.Overlay.HeaderText);
            if (overlayHeaderImage.Success)
            {
                return _tesseractService.ExtractTextFromImage(overlayHeaderImage.FilePath);
            }

            return string.Empty;
        }

        internal async Task ApplyShield(int v)
        {
            throw new NotImplementedException();
        }

        internal async Task<bool> SetCursorPositionAsync(int x, int y, CancellationToken token)
        {
            var result =  await _mouseInputService.MoveAsync(x, y, token);
             _logger.Trace($"cursor set at {result.Position[0]}x{result.Position[1]} with target window info: {result.TargetWindow}");
            return result.Success;
        }
    }

}
