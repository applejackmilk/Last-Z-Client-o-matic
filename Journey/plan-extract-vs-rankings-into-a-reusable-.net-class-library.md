# 🎯 Extract VS rankings into a reusable .NET class library

## Understanding
Create a reusable class-library boundary for `WindowsMCP_Integrated.VSDuel.VSService` so the existing command-line host and the WinForms app can invoke VS-rankings functionality directly, without routing the WinForms workflow through `System.CommandLine`/`VSCommand`. This is a planning-only request; no files will be changed.

## Assumptions
- Keep the existing CLI working; `VSCommand` may remain as a thin command adapter over the reusable service.
- The new WinForms project is `LastZ Client-o-matic.UI`; it should reference the reusable library, not the CLI executable.
- The library is Windows-specific and should use the current `net10.0-windows10.0.22621.0` target compatible with the automation dependency.
- Preserve the existing capture/OCR workflow and its output behavior unless implementation reveals a deliberate API redesign is needed.

## Approach
The current files are in one executable project. `VSService` depends on `GameService`, `VSElements`, `Helpers`, and ranking/table types; it also performs output processing through `GameService`'s Tesseract service. Conversely, `GameService` constructs `VSService` and exposes `CaptureVSRankingDay`, so moving only `VSService.cs` into a class library would create a project-reference cycle or force the new library to depend on the CLI executable. Extract the reusable automation/application layer together (at minimum `GameService`, `VSService`, and their non-CLI dependencies) into a Windows class library, leaving `Program.cs` and `Commands/*` in a CLI host that references it. Move the WinForms project's project reference from the CLI executable to the shared library.

Refactor the ranking workflow so the service owns the end-to-end operation and accepts cancellation, while `VSCommand` only maps command-line arguments/results and the form invokes the same API with `await`. Resolve dependency construction/configuration and output paths at the host boundary; keep `System.CommandLine` types out of the library. Review the existing `GameService` ↔ `VSService` back-reference, blocking sleeps, discarded step results, exception handling, NLog usage, and transitive native/OCR content during the extraction. Validate both consumers build and that their outputs include required automation/OCR runtime assets.

## Key Files
- `WindowsMCP Integrated/WindowsMCP Integrated.csproj` — currently an executable containing CLI, service, and package references; split shared library from CLI host.
- `WindowsMCP Integrated/VSDuel/VSService.cs` — rankings workflow to expose from the library and make host-independent.
- `WindowsMCP Integrated/VSDuel/VSElements.cs` — VS-specific automation coordinates/types needed by the service.
- `WindowsMCP Integrated/Game/GameService.cs` — shared automation dependency and reverse reference to `VSService`; resolve layering before splitting.
- `WindowsMCP Integrated/Commands/VSCommand.cs` — retain as a thin CLI adapter, removing duplicate workflow orchestration.
- `WindowsMCP Integrated/Program.cs` — CLI composition root; keep startup and command parsing out of the class library.
- `WindowsMCP Integrated/Core/*`, `TesseractService.cs`, and `GameElements.cs` — audit and move only the shared types/services required by the reusable boundary.
- `LastZ Client-o-matic.UI/LastZ Client-o-matic.UI.csproj` and `Form1.cs` — reference the shared library and invoke the service from an asynchronous UI action.
- `Last Z Client-o-matic.slnx` — add the shared library and any separated CLI host project.
- `Sbroenne.WindowsMcp/Sbroenne.WindowsMcp.csproj` — confirm Windows target, UI automation and platform/runtime requirements.

## Risks & Open Questions
- `GameService` and `VSService` currently reference each other; determine whether to remove the convenience VS API from `GameService` or refactor around an injected lower-level automation abstraction to avoid cyclic project references.
- The CLI and WinForms hosts must each configure screenshot directory, image-processing callbacks, Tesseract data, logging, and automation services consistently; these are currently initialized by `Program`.
- `VSService` uses `Thread.Sleep` through `Helpers`, discards some action results, and does not propagate the command's `CancellationToken`; define cancellation and failure reporting without freezing the WinForms UI.
- Confirm UI Automation / input calls' required apartment or thread context before choosing `Task.Run` or other dispatching; the WinForms call path should remain responsive while respecting those constraints.
- Native OpenCV, Tesseract, and Windows automation dependencies may require runtime/content settings to flow correctly to both application outputs.

**Progress**: 100% [██████████]

**Last Updated**: 2026-11-05 19:29:51

## 📝 Plan Steps
- ✅ **Inventory source ownership and project references for the automation, OCR, core/UI, and VS-specific types; decide the shared-library boundary that breaks the `GameService`/`VSService` cycle.**
- ✅ **Create the reusable Windows class-library project with the compatible target framework and shared dependencies.**
- ✅ **Move shared automation, VS-ranking, and OCR implementation into the library and remove CLI-only dependencies from that project.**
- ✅ **Split or retain the command-line executable as a host referencing the library, and reduce `VSCommand` to argument/result adaptation around the service API.**
- ✅ **Update the WinForms project to reference the library and invoke the ranking operation asynchronously with progress/result/cancellation handling.**
- ✅ **Configure both hosts for logging, screenshot output, image processing, Tesseract assets, and automation dependencies.**
- ✅ **Build the solution and validate CLI and WinForms references, runtime asset propagation, and the complete VS capture/OCR workflow.**

