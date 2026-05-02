# ScrollShot

ScrollShot is a lightweight Windows desktop app that captures scrolling screenshots of a selected window and stitches the captured viewports into one high-resolution image.

## Build

This repo is set up to keep tooling local. The portable SDK lives in `.dotnet\` and is intentionally ignored by Git.

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path .).Path
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
.\.dotnet\dotnet.exe restore ScrollShot.sln --configfile NuGet.Config
.\.dotnet\dotnet.exe build ScrollShot.sln --no-restore
.\.dotnet\dotnet.exe publish ScrollShot\ScrollShot.csproj -c Release --no-restore
```

On a machine with a normal .NET 8 SDK installed, the same commands work with `dotnet` instead of `.\.dotnet\dotnet.exe`.

## Quick Run

From the repo root:

- `run.cmd` (double-click), or
- PowerShell: `./run.ps1`

This publishes a self-contained Release build and launches `ScrollShot.exe`.

## Usage

1. Launch ScrollShot.
2. Press **Pick window** and click the window you want to capture.
3. Pick a scroll speed. Medium is the default and waits 150ms between scroll steps.
4. Press Capture and choose the PNG or JPEG output path.
5. Keep the target window visible and unchanged while capture runs.

The app waits 500ms before capturing, scrolls the target, detects when the bottom is reached, stitches matching overlaps, and then shows a thumbnail with dimensions and file size.

The main window is resizable; the preview card updates after each capture.

## Known Limitations

- Hardware-accelerated, protected, or DRM content may capture as black or blank.
- Minimized windows cannot be captured.
- Windows that move or resize during capture are aborted to avoid malformed output.
- Pages with sticky headers, animations, lazy loading, or custom scroll containers can reduce stitch quality.
- Some applications reject synthetic scroll input or use nonstandard scrolling surfaces.
- Browser capture uses mouse wheel simulation at the window center, so the intended scrollable area must be under that point.

ScrollShot is fully offline. It does not make network calls, collect telemetry, or run in the background.

## Icon

ScrollShot currently uses a built-in vector icon for the window/taskbar.
If you want the exported `ScrollShot.exe` file to have a custom Explorer icon too, add a `.ico` file and set `ApplicationIcon` in `ScrollShot/ScrollShot.csproj`.
