using System.CommandLine;
using Microsoft.Extensions.Logging;

namespace WindowsMCP_Integrated
{
    namespace Commands
    {
        public abstract class CommandAbstract : Command
        {
            protected readonly GameService gameService;
            protected readonly ILogger _logger;
            protected CommandAbstract(GameService gameService, string name, string? description, ILogger logger) : base(name, description)
            {
                this.gameService = gameService;
                this._logger = logger;

                _logger.LogTrace($"Command {name} initialized with description: {description}");
            }

            protected internal bool NullGuard(object o)
            {
                //ArgumentNullException.ThrowIfNull(o); make this configurable?
                return o != null;
            }

            protected internal void CheckGameService()
            {
                this.Validators.Add(result =>
                {
                    if (!NullGuard(gameService))
                    {
                        result.AddError("Game service was not found");
                    }
                });

            }

        }
    }

}
