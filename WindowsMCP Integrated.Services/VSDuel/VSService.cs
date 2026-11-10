
using NLog;
using System.Windows.Forms;
using WindowsMCP_Integrated.Core;
using WindowsMCP_Integrated.OCR;

namespace WindowsMCP_Integrated.VSDuel
{
    public class VSService
    {
        private readonly GameService _gameService;
        private readonly VSElements _vsElements = new VSElements();
        private readonly ILogger _logger = NLog.LogManager.GetCurrentClassLogger();
        public VSService(GameService gameService)
        {
            _gameService = gameService;
        }
        private List<Task<bool>> _mouseTasks;
        private async Task<bool> RunMouseTasks(CancellationToken cancellationToken)
        {
            foreach (var task in _mouseTasks)
            {
                if(!await task)
                {
                    return false;
                }
                await Task.Delay(ConfigSettings.MouseActionTypeDetail, cancellationToken);
            }
            return true;
        }
        public async Task<bool> CaptureDailyVSRankingTestBatching(int? dayIndex, CancellationToken cancellationToken = default)
        {
            try { 
            cancellationToken.ThrowIfCancellationRequested();
            _mouseTasks = new();
            _mouseTasks.Add(OpenVSScreen(cancellationToken));
            _mouseTasks.Add(ClickVSRankingsTable(cancellationToken));
            _mouseTasks.Add(ClickAllianceCheckbox(cancellationToken));
            var setupSuccessful = await RunMouseTasks(cancellationToken);
            if(!await CaptureVSDayRankingTable(dayIndex, cancellationToken))
                {
                    return false;
                }
            var imagePath = _gameService.ScreenshotOutputPath;
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                throw new InvalidOperationException("Screenshot output path is not configured.");
            }

            var daySuffix = dayIndex.HasValue ? $" {dayIndex.Value}" : string.Empty;
            var outputPath = Path.Combine(imagePath, $"{DateTime.Now:yyyyMMdd}{daySuffix} table.csv");
            cancellationToken.ThrowIfCancellationRequested();

            return await Task.Run(
                () => {
                    return ProcessVSRankingsTableDirectoryToFile(imagePath, outputPath);
                },
                cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.Info("VS rankings capture was canceled.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "VS rankings capture failed.");
                return false;
            }
        }

        public async Task<bool> CaptureDailyVSRanking(int? dayIndex, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await OpenVSScreen(cancellationToken))
                {
                    return false;
                }

                await Task.Delay(ConfigSettings.MouseActionTypeDetail, cancellationToken);
                if (!await ClickVSRankingsTable(cancellationToken))
                {
                    return false;
                }

                await Task.Delay(ConfigSettings.MouseActionTypeDetail, cancellationToken);
                if (!await ClickAllianceCheckbox(cancellationToken))
                {
                    return false;
                }

                await Task.Delay(ConfigSettings.MouseActionTypeDetail, cancellationToken);
                if (!await CaptureVSDayRankingTable(dayIndex, cancellationToken))
                {
                    return false;
                }

                var imagePath = _gameService.ScreenshotOutputPath;
                if (string.IsNullOrWhiteSpace(imagePath))
                {
                    throw new InvalidOperationException("Screenshot output path is not configured.");
                }

                await Task.Delay(ConfigSettings.MouseActionTypeDetail, cancellationToken);
                await _gameService.ClickPositionAndForgetAsync(_vsElements.Rankings.ExitButton, cancellationToken);
                await Task.Delay(ConfigSettings.MouseActionTypeDetail, cancellationToken);
                await _gameService.ClickPositionAndForgetAsync(_vsElements.Rankings.ExitButton, cancellationToken);



                var daySuffix = dayIndex.HasValue ? $" {dayIndex.Value}" : string.Empty;
                var outputPath = Path.Combine(imagePath, $"{DateTime.Now:yyyyMMdd}{daySuffix} table.csv");
                cancellationToken.ThrowIfCancellationRequested();

                return await Task.Run(
                    () => {
                        //_gameService.GetTesseractService().ProcessTablesDirectory(imagePath, outputPath);
                        return ProcessVSRankingsTableDirectoryToFile(imagePath, outputPath);
                        },
                    cancellationToken);

            }
            catch (OperationCanceledException)
            {
                _logger.Info("VS rankings capture was canceled.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "VS rankings capture failed.");
                return false;
            }
        }

        public bool ProcessVSRankingsTableDirectoryToFile(string sourceDir, string outputPath)
        {
            var vSMemberRanking = new MemberVSRankParser();
            using var imgProcessor = new ImageProcessing();
            var rankings = imgProcessor.ProcessTableImageDirectoryByTextModel(sourceDir, Path.Combine(sourceDir, "debug"), vSMemberRanking);
            var validRanks = vSMemberRanking.Cleanup(rankings).ToList();
            Helpers.ToCsvFile(outputPath, vSMemberRanking.GetCsvHeaders(), vSMemberRanking.ToStringTable(validRanks));
            return true;
        }
        public async Task<bool> CaptureVSDayRankingTable(int? _dayIndex, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string dayName;
                if (_dayIndex.HasValue)
                {
                    var clickResult = await ClickRankingDay(_dayIndex.Value, cancellationToken);
                    if (!clickResult)
                    {
                        throw new InvalidOperationException($"{nameof(ClickRankingDay)} failed click for day index: {_dayIndex.Value}");
                    }

                    await Task.Delay(ConfigSettings.MouseActionTypeDetail, cancellationToken);

                    dayName = String.Join("", ((System.DayOfWeek)(_dayIndex.Value + 1)).ToString().Take(3));
                }
                else
                {
                    dayName = DateTime.Today.ToString("yyyyMMdd");
                }

                _logger.Trace($"Capturing VS daily ranking for {dayName}");
                await _gameService.ScrollAndScreenshotTableAsync(_vsElements.Rankings.DailyRankingsTable, $"{dayName} Table", cancellationToken);
                return true;
            }
            catch (OperationCanceledException)
            {
                _logger.Error("The operation was aborted");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Reading VS Rankings Table Failed");
                return false;
            }
        }

        public async Task<bool> ClickRankingTypeTab(int index, CancellationToken cancellationToken = default)
        {
            var tabClickLocation = _vsElements.Rankings.GetTabElement(index);
            if (!await _gameService.ActivateGameWindow())
            {
                return false;
            }
            var result = await _gameService.ClickPositionAndForgetAsync(
                tabClickLocation, cancellationToken);
            return result;
        }

        public async Task<bool> ClickRankingDay(int dayIndex, CancellationToken cancellationToken = default)
        {
            var dayButton = _vsElements.Rankings.GetDayElement(dayIndex);
            if (!await _gameService.ActivateGameWindow())
            {
                return false;
            }
            var result = await _gameService.ClickPositionAndForgetAsync(
                dayButton, cancellationToken);
            return result;
        }
        public async Task<bool> ClickVSRankingsTable(CancellationToken cancellationToken = default)
        {
            if (!await _gameService.ActivateGameWindow())
            {
                return false;
            }
            var result = await _gameService.ClickPositionAndForgetAsync(
                _vsElements.RankingsButton, cancellationToken);
            return result;
        }

        public async Task<bool> ClickAllianceCheckbox(CancellationToken cancellationToken = default)
        {
            if (!await _gameService.ActivateGameWindow())
            {
                return false;
            }

            var result = await _gameService.ClickPositionAndForgetAsync(
                _vsElements.Rankings.AllianceCheckbox, cancellationToken);
            return result;
        }

        public async Task<bool> OpenVSScreen(CancellationToken cancellationToken = default)
        {
            if(!await _gameService.ActivateGameWindow())
            { 
                return false;
            }
            var clickResult = await _gameService.ClickElementAsync(element: GameElements.Main.VSButton
                , retryTimes: 3, retryDelay: 1000, waitUntil: async () =>
                {
                    await Task.Delay(3000, cancellationToken);
                    return true;
                }, cancellationToken);
            return clickResult;
        }
    }
}
