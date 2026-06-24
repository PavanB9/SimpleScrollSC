# SimpleScrollSC

Capture a scrolling screenshot of any window and save it as a single tall image — no browser extension, no subscriptions, no internet connection required.

## Download

**[⬇ Download SimpleScrollSC.exe](https://github.com/PavanB9/SimpleScrollSC/releases/latest/download/SimpleScrollSC.exe)**

No installation needed. Just download and run. Windows only.

> The file is ~69 MB because it bundles the .NET runtime so you don't need to install anything separately. Windows may show a SmartScreen warning on first run — click **More info → Run anyway**.

---

## How to use

1. **Launch** `SimpleScrollSC.exe`.
2. Click **Pick window** and then click the window you want to capture.
3. Choose a scroll speed (Medium works well for most things).
4. Click **Capture** and pick where to save the image.
5. Keep the target window visible while it runs — it captures automatically.
6. Press **Esc** at any time to stop early.

The app minimizes itself during capture so it stays out of the way. When it's done, a preview appears with the image dimensions and file size.

### Area mode

Check **Select area + click-to-start/stop** to capture just a portion of the window instead of the whole thing. After picking a window, drag to select the region you want, then use **Start** / **Esc** to control the capture manually.

---

## Tips

- **Browsers (Chrome, Edge, Firefox)** — works great for capturing long web pages.
- **Slow speed** — use this for content that loads as you scroll (social feeds, infinite scroll).
- **Fast speed** — use this for simple documents or apps that scroll smoothly.
- If the output looks repeated or jumbled, try a slower speed.
- The window being captured must stay visible and not be minimized while running.

---

## Known limitations

- Hardware-accelerated, DRM-protected, or fully GPU-rendered content may capture as black.
- Minimized windows cannot be captured.
- Pages with sticky headers, animations, or custom scroll containers may stitch imperfectly.
- Some apps ignore simulated scroll input — try bringing the window to the foreground first.

---

## Build from source

Requires Windows and a .NET 8 SDK. If you don't have the local SDK, bootstrap it once (stays inside the repo, not committed):

```powershell
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1
./dotnet-install.ps1 -Channel 8.0 -InstallDir .dotnet
```

Then build and run:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path .).Path
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
.\.dotnet\dotnet.exe publish SimpleScrollSC\SimpleScrollSC.csproj -c Release --self-contained true
```

Or just double-click `run.cmd`.

---

## Icon

Icon by [Icons8](https://icons8.com).
