using System.CommandLine;
using Microsoft.Extensions.Logging;

namespace WindowsMCP_Integrated
{
    namespace Commands
    {
        public abstract class CommandAbstract : Command
        {
            protected readonly GameService _gameService;
            protected readonly ILogger _logger;
            protected CommandAbstract(GameService gameService, string name, string? description, ILogger logger) : base(name, description)
            {
                _gameService = gameService;
                _logger = logger;

                string message = $"Command {name} initialized with description: {description}";
                _logger.LogTrace(message);
            }
        }
    }

}
