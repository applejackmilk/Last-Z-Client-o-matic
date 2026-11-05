using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;

namespace WindowsMCP_Integrated.Commands
{
    public sealed class ProcessCommand : Command
    {
        private readonly ILogger _logger;
        private readonly TesseractService _tesseractService;
        public ProcessCommand(TesseractService tesseractService, ILogger logger) : base("process", "Image processing commands")
        {
            _tesseractService = tesseractService;
            _logger = logger;

            var imageCommand = new Command("image", "Process image files");
            var transformationOption = new Option<string?>("--transformation")
            {
                Description = "ColorConversionCodes value to use for normalization."
            };
            imageCommand.Subcommands.Add(CreateImageOperationCommand(
                "normalize",
                "Normalize an image using the specified color conversion",
                (processor, saveOutput, parseResult) =>
                {
                    var transformationName = parseResult.GetValue(transformationOption);
                    var transformation = ColorConversionCodes.BGR2GRAY;
                    if (transformationName is not null &&
                        (!Enum.TryParse(transformationName, ignoreCase: true, out transformation) ||
                         !Enum.IsDefined(typeof(ColorConversionCodes), transformation)))
                    {
                        throw new ArgumentException($"'{transformationName}' is not a valid ColorConversionCodes value.");
                    }

                    processor.NormalizeCore(transformation, saveOutput);
                },
                transformationOption));
            imageCommand.Subcommands.Add(CreateImageOperationCommand(
                "create-binary-mask",
                "Create a binary mask",
                (processor, saveOutput, _) => processor.CreateBinaryMaskCore(saveOutput: saveOutput)));
            imageCommand.Subcommands.Add(CreateImageOperationCommand(
                "upscale",
                "Upscale an image",
                (processor, saveOutput, _) => processor.Upscale(saveOutput: saveOutput)));
            imageCommand.Subcommands.Add(CreateImageOperationCommand(
                "reduce-noise",
                "Reduce image noise",
                (processor, saveOutput, _) => processor.ReduceNoise(saveOutput: saveOutput)));
            imageCommand.Subcommands.Add(CreateImageOperationCommand(
                "pad-edges",
                "Add padding to image edges",
                (processor, saveOutput, _) => processor.PadEdges(saveOutput)));

            Subcommands.Add(imageCommand);

            var ocrCommand = new Command("ocr", "Perform OCR on image files");

            readImageCommand.Options.Add(imageFilePathOption);
            ocrCommand.Subcommands.Add(readImageCommand);
            readImageCommand.SetAction(DoRunTesseract);
            Subcommands.Add(ocrCommand);
        }

        public static void Register(RootCommand root, TesseractService tesseractService, ILogger logger)
        {
            
            root.Subcommands.Add(new ProcessCommand(tesseractService, logger));
        }
        Command readImageCommand = new Command("read-image", "Read text from an image using Tesseract OCR");
        Option<string?> imageFilePathOption = new Option<string?>("--file-path") { AllowMultipleArgumentsPerToken = true }
           .AcceptLegalFilePathsOnly();
        private async Task<int> DoRunTesseract(ParseResult parseResult, CancellationToken token)
        {
            if (parseResult.GetValue(imageFilePathOption) is null)
            {
                _logger.LogError("File path is required for OCR operation.");
                return 1;
            }
            var imagePath = parseResult.GetValue(imageFilePathOption);
            _tesseractService.ProcessTablesDirectory(imagePath, Path.Combine(Path.GetDirectoryName(imagePath), $"{DateTime.Now.ToString("yyyyMMdd")} table.csv"));
            return 0;
        }

        private static Command CreateImageOperationCommand(
            string name,
            string description,
            Action<ImageProcessing, bool, ParseResult> operation,
            Option<string?>? option = null)
        {
            var command = new Command(name, description);
            var newFilePathArgument = new Argument<string>("file-path");
            var savedFileNameArgument = new Argument<string?>("saved-filename");
            command.Arguments.Add(newFilePathArgument);
            command.Arguments.Add(savedFileNameArgument);
            if (option is not null)
            {
                command.Options.Add(option);
            }
            command.SetAction((parseResult, _) => RunImageOperation(
                parseResult,
                newFilePathArgument,
                savedFileNameArgument,
                operation));
            return command;
        }

        private static Task<int> RunImageOperation(
            ParseResult parseResult,
            Argument<string> filePathArgument,
            Argument<string?> savedFileNameArgument,
            Action<ImageProcessing, bool, ParseResult> operation)
        {
            var filePath = parseResult.GetValue(filePathArgument);
            var savedFileName = parseResult.GetValue(savedFileNameArgument);
            using var processor = new ImageProcessing(Console.WriteLine);
            processor.ReadFile(filePath, ImreadModes.Color);
            operation(processor, savedFileName is null, parseResult);

            if (savedFileName is not null)
            {
                processor.SaveFileAs(savedFileName);
            }

            return Task.FromResult(0);
        }
    }
}
