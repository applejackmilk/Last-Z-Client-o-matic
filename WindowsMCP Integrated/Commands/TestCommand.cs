using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.Diagnostics;
using WindowsMCP_Integrated.Core;
using WindowsMCP_Integrated.VSDuel;

namespace WindowsMCP_Integrated
{
    namespace Commands
    {
        public class TestCommand : CommandAbstract
        {
            private readonly Command _test1Command = new("test1", "Test command 1");
            private readonly Argument<string?> _test1Argument = new("arg1-string");
            private async Task<int> DoTest1(ParseResult parseResult, CancellationToken token)
            {
                var arg1 = parseResult.GetValue(_test1Argument);
                var vsElements = new VSElements();
                var result = await _gameService.GetScreenshot(CancellationToken.None);

                var ip = new ImageProcessing();
                var debugImage = ip.DetectTextDebugImage(arg1, Helpers.ModifyFileName(arg1, "", " debug"));
                Console.WriteLine($"screenshot output to: {result.FilePath}");
                Console.WriteLine($"debug output to: {debugImage}");
                return 0;
            }

            private readonly Command _test2Command = new("test2", "Test command 2");
            private readonly Argument<string?> _test2Arg1Argument = new("arg1-string");
            private readonly Argument<string?> _test2Arg2Argument = new("arg2-string");
            private async Task<int> DoTest2(ParseResult parseResult, CancellationToken token)
            {
                var input1 = parseResult.GetValue(_test2Arg1Argument);
                var input2 = parseResult.GetValue(_test2Arg2Argument);
                var vsElements = new VSDuel.VSElements();
                OpenCvSharp.Rect rect = new(vsElements.Rankings.AllianceCheckbox.X, vsElements.Rankings.AllianceCheckbox.Y, vsElements.Rankings.AllianceCheckbox.Size.Width, vsElements.Rankings.AllianceCheckbox.Size.Height);
                ImageProcessing.ExtractRegionFromImageFile(input1, input2, rect);
                return 0;
            }

            private readonly Command _readHeaderText = new("read-header-text", "Read the overlay header text");
            private async Task<int> DoReadHeaderText(ParseResult parseResult, CancellationToken token)
            {
                var result = await _gameService.ReadHeaderText();
                return result ? 1 : 0;

            }
            private readonly Argument<int> _screenshotX = new("--x");
            private readonly Argument<int> _screenshotY = new("--y");
            private readonly Argument<int> _screenshotWidth = new("--width");
            private readonly Argument<int> _screenshotHeight = new("--height");
            private readonly Command _screenshot = new("screenshot", "Take a screenshot based on params provided");

            private async Task<int> DoScreenshot(ParseResult parseResult, CancellationToken token)
            {
                var x = parseResult.GetValue(_screenshotX);
                var y = parseResult.GetValue(_screenshotY);
                var width = parseResult.GetValue(_screenshotWidth);
                var height = parseResult.GetValue(_screenshotHeight);

                var result = await _gameService.GetScreenshotAsync("Test Screenshot", x, y, width, height);
                if (!(!result.Success || string.IsNullOrEmpty(result.FilePath)))
                {
                    _gameService.PreprocessImageForReading(result.FilePath);

                }
                return result.Success ? 1 : 0;

            }
            private readonly Option<string?> _readTableFilePrefix = new("--file-prefix");

            private readonly Command _readSvSPointsRanking = new("read-svs-points-rankings", "Scroll down list");
            private async Task<int> DoReadSvSPointsRanking(ParseResult pr, CancellationToken token)
            {
                _gameService.IncludeTimestampInScreenshotFile = false;
                _ = await _gameService.ScrollAndScreenshotTableAsync(GameElements.SvSPointsRankingTable, $"{(pr.GetValue(_readTableFilePrefix) ?? "SvS Points")} Table", token);
                return 0;

            }

            private readonly Command _testGameCommand = new("game", "Game-specific test commands");
            private readonly ImageProcessing _imageProcessing;

            public TestCommand(GameService gameService, ILogger logger) : base(gameService, "test", "Test command for various operations", logger)
            {
                _imageProcessing = new ImageProcessing();

                _test1Command.Arguments.Add(_test1Argument);
                _test1Command.SetAction(DoTest1);
                _testGameCommand.Subcommands.Add(_test1Command);

                _test2Command.Arguments.Add(_test2Arg1Argument);
                _test2Command.Arguments.Add(_test2Arg2Argument);
                _test2Command.SetAction(DoTest2);
                _testGameCommand.Subcommands.Add(_test2Command);

                _readHeaderText.SetAction(DoReadHeaderText);
                _testGameCommand.Subcommands.Add(_readHeaderText);

                _screenshot.Arguments.Add(_screenshotX);
                _screenshot.Arguments.Add(_screenshotY);
                _screenshot.Arguments.Add(_screenshotWidth);
                _screenshot.Arguments.Add(_screenshotHeight);
                _screenshot.SetAction(DoScreenshot);
                _testGameCommand.Subcommands.Add(_screenshot);

                _readSvSPointsRanking.SetAction(DoReadSvSPointsRanking);
                _readSvSPointsRanking.Options.Add(_readTableFilePrefix);
                _testGameCommand.Subcommands.Add(_readSvSPointsRanking);

                Subcommands.Add(_testGameCommand);
            }

            public static void Register(RootCommand root, GameService _gameService, ILogger logger)
            {
                var testCommand = new TestCommand(_gameService, logger);
                root.Subcommands.Add(testCommand);
            }
        }
    }

}
