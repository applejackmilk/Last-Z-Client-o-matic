using System.CommandLine;
using WindowsMCP_Integrated.Commands;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;

namespace WindowsMCP_Integrated;



internal partial class Program
{

    private static GameService _gameService;

    private const string _screenshotOutputPath = @"D:\Development\WindowsMCP Integrated\Screenshots";

    static async Task<int> Main(string[] args)
    {
        _gameService = await GameServiceFactory.CreateForCurrentGameAsync(
            _screenshotOutputPath,
            screenshotElementWhenClicking: true);

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.AddNLog("nlog.config");
        });
        var rootCommand = BuildRootCommand(loggerFactory);

        if (args.Length == 0)
        {
            var helpResult = rootCommand.Parse("-h");
            helpResult.Invoke();
        }

        while (true)
        {
            var input = Console.ReadLine();
            if (input == null || input.Trim().Equals("exit", StringComparison.CurrentCultureIgnoreCase))
            {
                break;
            }
            var commandResult = await rootCommand.Parse(input).InvokeAsync();

            Console.WriteLine($"Command executed with result code: {commandResult}");
        }
        return 0;

    }

    private static RootCommand BuildRootCommand(ILoggerFactory loggerFactory)
    {
        RootCommand rootCommand = new("WindowsMCP Integrated command-line interface");
        GameCommand.Register(rootCommand, _gameService, loggerFactory.CreateLogger<GameCommand>());
        UtilityCommand.Register(rootCommand, _gameService, loggerFactory.CreateLogger<UtilityCommand>());
        VSCommand.Register(rootCommand, _gameService, loggerFactory.CreateLogger<VSCommand>());
        ProcessCommand.Register(rootCommand, _gameService.GetTesseractService(), loggerFactory.CreateLogger<ProcessCommand>());
        TestCommand.Register(rootCommand, _gameService, loggerFactory.CreateLogger<TestCommand>());
        return rootCommand;
    }
}
