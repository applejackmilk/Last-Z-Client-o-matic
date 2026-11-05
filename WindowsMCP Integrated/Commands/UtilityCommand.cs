using Microsoft.Extensions.Logging;
using OpenCvSharp.Internal;
using Sbroenne.WindowsMcp.Input;
using Sbroenne.WindowsMcp.Models;
using Sbroenne.WindowsMcp.Tools;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Windows.UI.WindowManagement;
using WindowsMCP_Integrated.Core;
using WindowsMCP_Integrated.Core.UI;
using DrawingPoint = System.Drawing.Point;

namespace WindowsMCP_Integrated
{
    namespace Commands
    {
        public class UtilityCommand : CommandAbstract
        {
            Command clickCommand = new Command("click", "Click commands");
            Option<bool> centerOption = new Option<bool>("--center");//"Click the center of the game screen"
            Option<string> positionOption = new Option<string>("--position");//"Click a custom position in the form x y or x+y"
            Option<int> secondsOption = new Option<int>("--seconds");//"Click continuously for a number of seconds"
            Option<int> timesOption = new Option<int>("--times");//"Click a fixed number of times"
            private void RegisterClickCommand()
            {
                clickCommand.Options.Add(centerOption);
                clickCommand.Options.Add(secondsOption);
                clickCommand.Options.Add(timesOption);

                clickCommand.SetAction(DoClick);
                this.Subcommands.Add(clickCommand);
            }
            private async Task<int> DoClick(ParseResult parseResult, CancellationToken token)
            {
                Point? point = null;
                if (parseResult.GetValue(centerOption))
                {
                    point = new Point(UtilityCursorPositions.Center.ClickX, UtilityCursorPositions.Center.ClickY);
                }

                int duration = 0;
                if (parseResult.GetValue(secondsOption) is int seconds)
                {
                    duration = seconds;
                }

                int times = 1;
                if (parseResult.GetValue(timesOption) is int repeat)
                {
                    times = repeat;
                }

                if (times == 1 && point.HasValue)
                {
                    await gameService.ClickPositionAndForgetAsync(point.Value.X, point.Value.Y);
                    return 0;
                }

                var stopwatch = Stopwatch.StartNew();
                var remaining = times;
                while (stopwatch.Elapsed.TotalSeconds < seconds || remaining-- >= 0)
                {
                    await gameService.ClickPositionAndForgetAsync(point, times > 1 || seconds > 0);
                    Helpers.ThrottleFastClick((int)stopwatch.Elapsed.TotalSeconds);
                }

                return 0;


            }

            public UtilityCommand(GameService gameService, ILogger logger) : base(gameService, "utility", "Utility click operations", logger)
            {
                RegisterClickCommand();
                RegisterMouseCommand();
                RegisterCreateCommand();
            }
            private Command createCommand = new("create", "create a new sequence to program out");
            private Argument<string> sequenceNameArgument = new Argument<string>("name");
            private Argument<bool?> centerCreateArgument = new("is-center");

            private void RegisterCreateCommand()
            {
                createCommand.Arguments.Add(sequenceNameArgument);
                createCommand.Arguments.Add(centerCreateArgument);
                createCommand.SetAction(DoCreateSequence);
                this.Subcommands.Add(createCommand);
            }
            private async Task<int> DoCreateSequence(ParseResult parseResult, CancellationToken token)
            {
                bool _exitRequested = false;

                List<Point> sequence = new();
                var sequenceName = parseResult.GetValue(sequenceNameArgument);
                if (string.IsNullOrEmpty(sequenceName))
                {
                    _logger.LogError("Sequence name is required.");
                    return 1;
                }
                var isCenterReference = parseResult.GetValue(centerCreateArgument);

                using var recordPositionHotKeyManager =
                    new GlobalHotKeyManager(HotKeyModifiers.Control,
                (uint)'R',
                HotKeyModifiers.Control,
                (uint)'X');

                recordPositionHotKeyManager.ToggleRequested += () =>
                {
                    var pos = MouseControlTool.GetCurrentPosition();
                    sequence.Add(new(pos.Position[0], pos.Position[1]));
                    _logger.LogDebug("Recorded Mouse Position");
                };
                recordPositionHotKeyManager.ExitRequested += () =>
                {
                    Console.WriteLine("Exit requested.");
                    _exitRequested = true;
                };
                //using var listener = new MouseListener();

                //using var listenerHotKeyManager =
                //    new GlobalHotKeyManager(
                //        HotKeyModifiers.Control | HotKeyModifiers.Alt,
                //        (uint)'L');

                //listener.ActionMade += (x, y) =>
                //{
                //        sequence.Add(new Point(x, y));
                //};

                //listenerHotKeyManager.HotKeyPressed += () =>
                //{
                //    if (listener.IsListening)
                //    {
                //        listener.Stop();
                //        _logger.LogDebug("Listening OFF. stop now?");
                //        if(Console.ReadLine() == "y")
                //        {
                //            _exitRequested = false;
                //        }
                //    }
                //    else
                //    {
                //        listener.Start();
                //        _logger.LogDebug("Listening ON");
                //    }
                //};

                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    _exitRequested = true;
                };

                recordPositionHotKeyManager.Start();
                //listenerHotKeyManager.Start();

                _logger.LogDebug("Ctrl+Alt+R records a mouse position.");

                recordPositionHotKeyManager.RunMessageLoop(() => _exitRequested || getKeyAvailable());
                //listenerHotKeyManager.RunMessageLoop(() => _exitRequested);
                
                var createElementTemplate = (templateType type) => type switch
                {
                    templateType.Center => $"private static ElementLocation {sequenceName}Element = ElementLocation.Create(name: \"{sequenceName}\", centerX: {{0}}, centerY: {{1}}, width: {{2}}, height: {{3}}, container: ContainerName.Unknown);"
                    , templateType.TopLeft => $"private static ElementLocation {sequenceName}Element = ElementLocation.\"CreateFromTopLeft\"(name: \"{sequenceName}\", top: {{0}}, left: {{1}}, width: {{2}}, height: {{3}}, container: ContainerName.Unknown);"
                    , _ => String.Empty
                };


                if ((isCenterReference.HasValue && isCenterReference.Value) || sequence.Count % 2 != 0)
                {
                    for (int i = 0; i < sequence.Count; i++)
                    {
                        var currentValue = String.Format(createElementTemplate( templateType.Center), sequence[i].X, sequence[i].Y, 25, 25);
                        Console.WriteLine(currentValue);
                    }
                }
                else
                {
                    for (int i = 0; i < sequence.Count; i += 2)
                    {
                        var currentValue = String.Format(createElementTemplate( templateType.TopLeft), sequence[i].X, sequence[i].Y, sequence[i - 1].X, sequence[i - 1].Y);
                        Console.WriteLine(currentValue);
                    }
                }

                _logger.LogDebug("Finished recording sequencing");
                return 0;
            }
            enum templateType
            {
                Center,
                TopLeft
            }
            private bool getKeyAvailable()
            {
                return Console.KeyAvailable;
            }
            private Command mouseCommand = new Command("mouse", "Mouse operations");
            Command getMousePosition = new Command("get-position");
            Command setMousePosition = new Command("set-position");
            Argument<int?> mousePositionXArg = new("x");
            Argument<int?> mousePositionYArg = new("y");
            Option<string?> mousePositionName = new Option<string?>("--name");

            private void RegisterMouseCommand()
            {
                getMousePosition.SetAction(DoGetMousePosition);
                mouseCommand.Subcommands.Add(getMousePosition);

                setMousePosition.Arguments.Add(mousePositionXArg);
                setMousePosition.Arguments.Add(mousePositionYArg);
                setMousePosition.Options.Add(mousePositionName);
                setMousePosition.SetAction(DoMouseMove);
                mouseCommand.Subcommands.Add(setMousePosition);

                this.Subcommands.Add(mouseCommand);
            }
            Dictionary<string, Point> NamedPositions = new()
            {

            };
            private async Task<int> DoMouseMove(ParseResult parseResult, CancellationToken token)
            {
                int x = 0;
                int y = 0;

                if (parseResult.GetValue(mousePositionXArg) is int xArg && parseResult.GetValue(mousePositionYArg) is int yArg)
                {
                    x = xArg;
                    y = yArg;
                }

                var positionName = parseResult.GetValue(mousePositionName);
                if (!String.IsNullOrEmpty(positionName))
                {
                    if (NamedPositions.ContainsKey(positionName))
                    {
                        x = NamedPositions[positionName].X;
                        y = NamedPositions[positionName].Y;
                    }
                }

                var result = await gameService.SetCursorPositionAsync(x, y, token);

                return 0;
            }

            private async Task<int> DoGetMousePosition(ParseResult pr, CancellationToken token)
            {
                var position = MouseControlTool.GetCurrentPosition();
                _logger.LogDebug($"{nameof(position)}: {position.Position[0]}, {position.Position[1]}");
                return 0;
            }

            public static void Register(RootCommand root, GameService _gameService, ILogger logger)
            {
                var utilityCommand = new UtilityCommand(_gameService, logger);
                root.Subcommands.Add(utilityCommand);
            }
        }
    }

}
