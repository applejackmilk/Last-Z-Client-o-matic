using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using OpenCvSharp;
using Sbroenne.WindowsMcp.Input;
using Sbroenne.WindowsMcp.Tools;
using System.CommandLine;
using System.Drawing;
using System.Windows.Forms;
using Windows.Globalization;
using WindowsMCP_Integrated.Core;
using WindowsMCP_Integrated.Core.UI;
using WindowsMCP_Integrated.VSDuel;

namespace WindowsMCP_Integrated
{
    namespace Commands
    {
        public class TestCommand : CommandAbstract
        {
            private Command test1Command = new Command("test1", "Test command 1");
            private Argument<int?> test1IntArgument = new Argument<int?>("arg1-int");
            private async Task<int> DoTest1(ParseResult parseResult, CancellationToken token)
            {
                var vsService = new VSService(gameService, new VSElements());
                var result = await vsService.CaptureDailyVSRanking(null, token);
                return result ? 0 : 1;
            }

            private Command test2Command = new Command("test2", "Test command 2");
            private Argument<string?> test2Arg1Argument = new Argument<string?>("arg1-string");
            private Argument<string?> test2Arg2Argument = new Argument<string?>("arg2-string");
            private async Task<int> DoTest2(ParseResult parseResult, CancellationToken token)
            {
                var input1 = parseResult.GetValue(test2Arg1Argument);
                var input2 = parseResult.GetValue(test2Arg2Argument);
                var vsElements = new VSDuel.VSElements();
                Rect rect = new Rect(vsElements.Rankings.AllianceCheckbox.X, vsElements.Rankings.AllianceCheckbox.Y, vsElements.Rankings.AllianceCheckbox.Size.Width, vsElements.Rankings.AllianceCheckbox.Size.Height);
                ImageProcessing.ExtractRegionFromImageFile(input1, input2, rect);
                return 0;
            }

            private Command readHeaderText = new Command("read-header-text", "Read the overlay header text");
            private async Task<int> DoReadHeaderText(ParseResult parseResult, CancellationToken token)
            {
                var result = await gameService.ReadHeaderText();
                return result ? 1 : 0;

            }
            private Argument<int> screenshotX = new Argument<int>("--x");
            private Argument<int> screenshotY = new Argument<int>("--y");
            private Argument<int> screenshotWidth = new Argument<int>("--width");
            private Argument<int> screenshotHeight = new Argument<int>("--height");
            private Command screenshot = new Command("screenshot", "Take a screenshot based on params provided");

            private async Task<int> DoScreenshot(ParseResult parseResult, CancellationToken token)
            {
                var x = parseResult.GetValue(screenshotX);
                var y = parseResult.GetValue(screenshotY);
                var width = parseResult.GetValue(screenshotWidth);
                var height = parseResult.GetValue(screenshotHeight);

                var result = await gameService.GetScreenshotAsync("Test Screenshot", x, y, width, height);
                if (result.Success && !String.IsNullOrEmpty(result.FilePath))
                {
                    gameService.PreprocessImageForReading(result.FilePath);

                }
                return result.Success ? 1 : 0;

            }
            private Option<string?> readTableFilePrefix = new Option<string?>("--file-prefix");

            private Command readSvSPointsRanking = new Command("read-svs-points-rankings", "Scroll down list");
            private async Task<int> DoReadSvSPointsRanking(ParseResult pr, CancellationToken token)
            {
                gameService.IncludeTimestampInScreenshotFile = false;

                var result = await gameService.ScrollAndScreenshotTableAsync(GameElements.SvSPointsRankingTable, $"{(pr.GetValue(readTableFilePrefix) ?? "SvS Points")} Table");
                return 0;

            }

            Command testGameCommand = new Command("game", "Game-specific test commands");
            private ImageProcessing _imageProcessing;

            public TestCommand(GameService gameService, ILogger logger) : base(gameService, "test", "Test command for various operations", logger)
            {
                _imageProcessing = new ImageProcessing(c => logger.LogDebug(c));

                test1Command.Arguments.Add(test1IntArgument);
                test1Command.SetAction(DoTest1);
                testGameCommand.Subcommands.Add(test1Command);

                test2Command.Arguments.Add(test2Arg1Argument);
                test2Command.Arguments.Add(test2Arg2Argument);
                test2Command.SetAction(DoTest2);
                testGameCommand.Subcommands.Add(test2Command);

                readHeaderText.SetAction(DoReadHeaderText);
                testGameCommand.Subcommands.Add(readHeaderText);

                screenshot.Arguments.Add(screenshotX);
                screenshot.Arguments.Add(screenshotY);
                screenshot.Arguments.Add(screenshotWidth);
                screenshot.Arguments.Add(screenshotHeight);
                screenshot.SetAction(DoScreenshot);
                testGameCommand.Subcommands.Add(screenshot);

                readSvSPointsRanking.SetAction(DoReadSvSPointsRanking);
                readSvSPointsRanking.Options.Add(readTableFilePrefix);
                testGameCommand.Subcommands.Add(readSvSPointsRanking);

                this.Subcommands.Add(testGameCommand);
            }

            public static void Register(RootCommand root, GameService _gameService, ILogger logger)
            {
                var testCommand = new TestCommand(_gameService, logger);
                root.Subcommands.Add(testCommand);
            }
        }
    }

}
