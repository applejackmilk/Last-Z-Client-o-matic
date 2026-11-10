using NLog;
using Sbroenne.WindowsMcp.Input;
using Sbroenne.WindowsMcp.Models;
using WindowsMCP_Integrated.Core;
using WindowsMCP_Integrated.Core.UI;

namespace WindowsMCP_Integrated
{
    public interface IMouseService
    {
        Task<bool> ClickAndForgetAsync();
        Task<bool> ClickElementAsync(ElementLocation element, int retryTimes, int retryDelay, Func<Task<bool>> waitUntil, CancellationToken cancellationToken = default);
        Task<bool> ClickPositionAndForgetAsync(ElementLocation element, CancellationToken cancellationToken = default);
        Task<bool> ClickPositionAndForgetAsync(int? x, int? y);
        Task<bool> ClickPositionAndForgetAsync(Point? point, bool antiDetect = false);
        Task<bool> DragMouseAsync(int startX, int startY, int endX, int endY);
        Task<bool> ScrollUpTableAsync(int startX, int startY, int height);
        Task<bool> SetCursorPositionAsync(int x, int y, CancellationToken token);
    }

    public class MouseService : IMouseService
    {
        public bool ScreenshotElementWhenClicking { get; set; } = false;
        private readonly MouseInputService _mouseInputService;
        protected readonly Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        public GameService GameServiceReference { get; internal set; }

        public MouseService(MouseInputService mouseInputService)
        {
            _mouseInputService = mouseInputService;
        }

        public async Task<bool> DragMouseAsync(int startX, int startY, int endX, int endY)
        {
            var dragger = await _mouseInputService.DragAsync(startX, startY, endX, endY);
            if (!dragger.Success)
            {
                _logger.Error($"Failed. Suggested mouse drag: {dragger.RecoverySuggestion}");
            }
            return dragger.Success;
        }

        public async Task<bool> ScrollUpTableAsync(int startX, int startY, int height)
        {
            _mouseInputService.SetDragSpeed(true);
            var dragger = await _mouseInputService.DragAsync(startX, startY, startX, startY - height);
            if (!dragger.Success)
            {
                _logger.Error($"Failed. Suggested mouse drag: {dragger.RecoverySuggestion}");
            }
            return dragger.Success;
        }

        public async Task<bool> ClickPositionAndForgetAsync(System.Drawing.Point? point, bool antiDetect = false)
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

        public async Task<bool> ClickPositionAndForgetAsync(int? x, int? y)
        {
            _logger.Trace($"Clicking mouse {x}x{y}");
            var clickResult = await ClickAsyncCore(x, y);
            return clickResult.Success;
        }

        public async Task<bool> ClickPositionAndForgetAsync(ElementLocation element, CancellationToken cancellationToken = default)
        {
            _logger.Trace($"Clicking element: {element.Name} at ({element.ClickX}, {element.ClickY}) with size ({element.Size.Width}, {element.Size.Height})");
            var clickResult = await ClickAsyncCore(element, cancellationToken);
            return clickResult.Success;
        }

        public async Task<bool> ClickAndForgetAsync()
        {
            _logger.Trace($"Clicking at current position");
            var clickResult = await ClickAsyncCore(null, null);
            return clickResult.Success;
        }

        private async Task<MouseControlResult> ClickAsyncCore(ElementLocation element, CancellationToken cancellationToken = default)
        {
            if (ScreenshotElementWhenClicking)
            {
                var screenshotResult = await GameServiceReference.GetScreenshotAsync($"{element.Name} Before Click", element);
                if (!screenshotResult.Success)
                {
                    _logger.Error($"Failed to capture screenshot for element: {element.Name}. Error: {screenshotResult.ErrorCode}");
                }
            }

            var clickResult = await ClickAsyncCore(element.ClickX, element.ClickY, cancellationToken);

            if (ScreenshotElementWhenClicking)
            {
                var screenshotResult = await GameServiceReference.GetScreenshotAsync($"{element.Name} After Click", element);
                if (!screenshotResult.Success)
                {
                    _logger.Error($"Failed to capture screenshot for element: {element.Name}. Error: {screenshotResult.ErrorCode}");
                }
            }

            if (!clickResult.Success)
            {
                _logger.Error("Failed to click on element.");
            }
            _logger.Trace("Element clicked successfully");

            await Task.Delay(ConfigSettings.MouseActionTypeDetail, cancellationToken);
            return clickResult;
        }

        private async Task<MouseControlResult> ClickAsyncCore(int? x, int? y, CancellationToken? token = null)
        {
            _logger.Trace($"Clicking mouse at {x}x{y}");
            var clickResult = await _mouseInputService.ClickAsync(x: x, y: y, cancellationToken: token ?? CancellationToken.None);
            return clickResult;
        }

        public async Task<bool> ClickElementAsync(ElementLocation element, int retryTimes, int retryDelay, Func<Task<bool>> waitUntil, CancellationToken cancellationToken = default)
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

        public async Task<bool> SetCursorPositionAsync(int x, int y, CancellationToken token)
        {
            var result = await _mouseInputService.MoveAsync(x, y, token);
            _logger.Trace($"cursor set at {result.Position[0]}x{result.Position[1]} with target window info: {result.TargetWindow}");
            return result.Success;
        }

    }

}
