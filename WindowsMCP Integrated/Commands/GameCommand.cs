using System.CommandLine;
using Microsoft.Extensions.Logging;

namespace WindowsMCP_Integrated
{
    namespace Commands
    {
        public class GameCommand : CommandAbstract
        {
            private readonly Command _readVSRankingTable = new("read-rankings", "Scroll down list");
            private readonly Option<string?> _readTableFilePrefix = new("--file-prefix");
            private readonly Option<int?> _readVSRankingDay = new("--day");
            private async Task<int> DoReadVSRankingTable(ParseResult pr, CancellationToken token)
            {

                if( pr.GetValue(_readVSRankingDay) is int dayIndex)
                {
                    var service = new VSDuel.VSService(_gameService);
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
                _readVSRankingTable.Options.Add(_readTableFilePrefix);
                _readVSRankingTable.Options.Add(_readVSRankingDay);
                _readVSRankingTable.SetAction(DoReadVSRankingTable);
                Subcommands.Add(_readVSRankingTable);
            }

            public GameCommand(GameService gameService, ILogger logger) : base(gameService, "game", "Game operations", logger) => ConfigureReadVSRankingTableCommand();

            public static void Register(RootCommand root, GameService _gameService, ILogger logger)
            {
                var gameCommand = new GameCommand(_gameService, logger);
                root.Subcommands.Add(gameCommand);
            }
        }
    }

}
