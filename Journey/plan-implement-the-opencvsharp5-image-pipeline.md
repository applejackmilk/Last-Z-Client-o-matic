# Implement the OpenCvSharp5 Image Pipeline
## Understanding
Implement a fluent image preprocessing pipeline in `ImageProcessing.cs` with named stages, JSON stage metadata, optional per-stage output, generated stage filenames, and debug logging through the user-selected local callback. Include the requested read, grayscale normalization, denoise, mask, mask cleanup, and annotated object extraction/filtering stages.
## Assumptions
- Keep the implementation within the existing `WindowsMCP Integrated` project and use OpenCvSharp5 already referenced by that project.
- Expose algorithm settings as method parameters with sensible defaults because thresholds, morphology, and object filter constraints were unspecified.
- The pipeline owns intermediate `Mat` instances and returns itself from each stage to support chaining; the caller controls disposal of the pipeline/result.
- `LogDebug` is backed by an optional local callback; absent a callback, it is a no-op.
- Store the latest post-processing output path as a property and return the extracted-object annotation output from the final stage.
## Approach
Replace the placeholder class with a disposable, stateful fluent pipeline. Each stage will validate its prerequisites, build an operation metadata object serialized with `System.Text.Json`, write a useful debug message through the callback, optionally persist the resulting image using `GetStageFileName(originalPath, stageName)`, and record the generated output path. Use contour area bounds to filter connected objects and draw accepted contours/rectangles onto a color annotation image.
## Key Files
- `WindowsMCP Integrated/ImageProcessing.cs` - existing placeholder; contains all requested preprocessing pipeline functionality.
- `WindowsMCP Integrated/WindowsMCP Integrated.csproj` - confirms target framework and existing OpenCvSharp5 dependency.
## Risks & Open Questions
- No concrete segmentation thresholds or object criteria were supplied; provide overridable parameters/default values.
- OpenCvSharp5 API compatibility needs build validation, especially enum names and morphology/contour overloads.
## Steps
1. Implement the disposable fluent processing pipeline in `ImageProcessing.cs`.
2. Validate compilation and resolve any OpenCvSharp5 API mismatches.