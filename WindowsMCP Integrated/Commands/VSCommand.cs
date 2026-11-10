using System.CommandLine;
using Microsoft.Extensions.Logging;
using WindowsMCP_Integrated.Core;

namespace WindowsMCP_Integrated
{
    namespace Commands
    {
        public class VSCommand : CommandAbstract
        {
            private Command rankingCommand = new Command("rankings", "VS Rankings commands");
            private Argument<int?> rankingOption = new Argument<int?>("day-index");// The day index for the VS rankings (0 for today, 1 for yesterday, etc.)
            private Option<bool?> yesterdayRankingOption = new ("--get-yesterday-rankings");
            private async Task<int> DoRankingCapture(ParseResult parseResult, CancellationToken token)
            {
                int? dayIndex = parseResult.GetValue(rankingOption);
                var vsService = new VSDuel.VSService(_gameService);
                var result = await vsService.CaptureDailyVSRanking(dayIndex, token);
                return result ? 0 : 1;
            }

            private Option<string?> readTableFilePrefix = new Option<string?>("--file-prefix");

            private Command readSvSPointsRanking = new Command("read-svs-points-rankings", "Scroll down list");
            private async Task<int> DoReadSvSPointsRanking(ParseResult pr, CancellationToken token)
            {
                _gameService.IncludeTimestampInScreenshotFile = false;

                var result = await _gameService.ScrollAndScreenshotTableAsync(GameElements.SvSPointsRankingTable, $"{(pr.GetValue(readTableFilePrefix) ?? "SvS Points")} Table");
                return 0;

            }

            public VSCommand(GameService gameService, ILogger logger) : base(gameService, "vs", "VS-related commands", logger)
            {

                rankingCommand.Arguments.Add(rankingOption);
                rankingCommand.SetAction(DoRankingCapture);
                this.Subcommands.Add(rankingCommand);

                readSvSPointsRanking.SetAction(DoReadSvSPointsRanking);
                readSvSPointsRanking.Options.Add(readTableFilePrefix);
                this.Subcommands.Add(readSvSPointsRanking);
            }

            public static void Register(RootCommand root, GameService _gameService, ILogger logger)
            {
                var DailyCommand = new VSCommand(_gameService, logger);
                root.Subcommands.Add(DailyCommand);
            }
        }

    }

}
