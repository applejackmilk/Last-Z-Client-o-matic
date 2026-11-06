# Add image-processing CLI commands
## Understanding
Add a `process image` command group with subcommands for `NormalizeCore`, `CreateBinaryMaskCore`, `Upscale`, `ReduceNoise`, and `PadEdges`. Each operation accepts a required source path and optional destination filename, and each invocation creates and disposes its own `ImageProcessing` instance.
## Assumptions
- Command paths use kebab-case: `process image create-binary-mask`, `reduce-noise`, and `pad-edges`.
- Existing processing defaults are used, with `NormalizeCore` using `BGR2GRAY` because it requires a conversion argument.
- A supplied destination filename is written with `SaveFileAs`; if omitted, the operation saves its standard stage output.
- The image CLI command is independent of `GameService`, so it derives directly from `System.CommandLine.Command` rather than the existing `CommandAbstract` that requires a game service.
## Approach
Create a command class that builds the `process`/`image` hierarchy, registers each operation, reads the source image, executes one processing method in a scoped disposable instance, and saves the result. Add this command to the root command construction in `Program.cs`. Preserve the existing game-service command base and registration behavior.
## Key Files
- `WindowsMCP Integrated/Commands/ImageProcessingCommand.cs` - new CLI command tree and operation handlers.
- `WindowsMCP Integrated/Program.cs` - register the new root-level command.
## Risks & Open Questions
- Commands with no explicit output filename will create the normal stage output in the configured post-processing directory.
- Current interactive input parsing splits on spaces, so paths containing spaces may require quoting support that is outside this change.
## Steps
1. Create the process/image command hierarchy and operation handlers.
2. Register the new command with the root command.
3. Build the solution to verify the command API and implementation compile.