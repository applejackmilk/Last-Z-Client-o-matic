using System.CommandLine;
using Microsoft.Extensions.Logging;

namespace WindowsMCP_Integrated
{
    namespace Commands
    {
        public class GameCommand : CommandAbstract
        {
            private Command readVSRankingTable = new Command("read-rankings", "Scroll down list");
            private Option<string?> readTableFilePrefix = new Option<string?>("--file-prefix");
            private Option<int?> readVSRankingDay = new Option<int?>("--day");
            private async Task<int> DoReadVSRankingTable(ParseResult pr, CancellationToken token)
            {

                if( pr.GetValue(readVSRankingDay) is int dayIndex)
                {
                    var service = new VSDuel.VSService(gameService);
                    var result = await service.CaptureDailyVSRanking(dayIndex, token);
                    return result ? 0 : 1;
                }
                else
                {
                    _logger.LogError("Day index is required for reading VS Rankings Table");
                    return 1;
                }
            }


            private void ConfigureReadVSRankingTableCommand()
            {
                readVSRankingTable.Options.Add(readTableFilePrefix);
                readVSRankingTable.Options.Add(readVSRankingDay);
                readVSRankingTable.SetAction(DoReadVSRankingTable);
                this.Subcommands.Add(readVSRankingTable);
            }

            public GameCommand(GameService gameService, ILogger logger) : base(gameService, "game", "Game operations", logger)
            {
                ConfigureReadVSRankingTableCommand();

            }

            public static void Register(RootCommand root, GameService _gameService, ILogger logger)
            {
                var gameCommand = new GameCommand(_gameService, logger);
                root.Subcommands.Add(gameCommand);
            }
        }
    }

}
