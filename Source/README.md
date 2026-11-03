# UEPackager

**Automated Multi-Platform Packaging for Unreal Engine**

UEPackager is a desktop tool that automates Unreal Engine packaging for multiple target platforms. Select your project and engine, choose and order your target platforms, then start the queue. UEPackager runs Unreal AutomationTool (UAT) for each selected platform in sequence and displays progress and logs.

## Launch

- **Windows:** extract the archive matching your system (x64 or ARM64), then open `UEPackager.exe`. In the project folder, you can also use `Launch GUI.lnk`; if you moved or renamed the folder and the shortcut no longer works, launch `UEPackager.exe` directly. No console window is opened.
- **macOS:** extract the archive matching your Mac (Apple Silicon: arm64; Intel: x64), then open `UEPackager.app`. Keep the complete application bundle.
- **Linux:** extract the matching archive, then run `./UEPackager` or `./Launch.sh` inside the extracted folder. Keep all included libraries alongside the executable. A desktop session with X11 or XWayland and its native libraries is required.

The distributions include the .NET runtime. Unreal Engine and the platform SDKs must be installed separately. macOS distributions are unsigned and not notarized; macOS may require approval in Privacy & Security. For organizational distribution, sign and notarize the application on a Mac using your own Developer ID.

## Packaging

1. Browse to a `.uproject`. Registered/common engine locations are detected when possible; verify the engine path.
2. Check the target platforms. Drag a platform name, use its right-side ↑ / ↓ buttons, or select it and use Move up / Move down. Checked platforms build from top to bottom. Reordering preserves selections and status.
3. Choose a destination. The default is `<project>/Builds`; changing projects resets this default. Missing folders are created automatically. **Use project Builds** restores the default.
4. Save and close Unreal Editor. Use **Check setup** for preflight or **Start packaging** to build.

Outputs go to `<destination>/<platform>`; Win64 uses `Windows`. Logs and per-run request snapshots go to `<destination>/Logs`. Existing output folders are updated rather than deleted. Keep complete output folders, including Android expansion files when generated.

Each platform displays Waiting, Running, Completed, Failed, Not built, Stopped or Setup checked. A failed platform stops the queue. Completed requires UAT exit code 0 and non-empty archived output. Stop terminates the active process tree. Closing during a build asks whether to stop it first. Only one packaging session may use a project/destination at a time.

Packaging uses Development configuration, build/cook/stage/pak/package/archive, DisableUnity and NoUBA. Maps, CPU architectures, signing, remote Mac configuration and other Unreal packaging settings come from the selected project. Android texture options are ASTC (default), ETC2, DXT and Multi.

Platform extensions are discovered from the selected engine's Config, Platforms and Restricted directories. Windows, Mac and Linux host restrictions are read from `DataDrivenPlatformInfo.ini`. Console targets need authorized platform extensions and SDKs. The tool running on all three systems does not make every target buildable from every host.

Windows uses `RunUAT.bat`; macOS and Linux use `/bin/bash RunUAT.sh`. No application PowerShell or VBScript launcher is required. A compatible `Saved/AndroidToolchain` uses the existing SDK, NDK 27.2.12479018 and JDK 21 layout; otherwise non-Windows hosts use their installed SDK environment. Check setup does not prove SDK/signing compatibility; an actual build does.

## Settings and maintenance

For compatibility with existing installations, UEPackager retains the original settings folder name. Settings are saved per user using the operating system's application-data directory, under `UE-Packaging-Automation/Settings.json`. Existing `Settings.json` beside the executable is imported on first launch. Settings inside a macOS bundle are not modified. On Windows the usual location is `%APPDATA%/UE-Packaging-Automation/Settings.json`.

`Source` contains the maintained project, icon and publishing scripts. From the `Source` directory, build with a .NET 8 or newer SDK:

```text
dotnet build PackagingAutomation.csproj -c Release
```

Use `Publish.ps1` on Windows or `publish.sh` on macOS/Linux to produce self-contained binaries for win-x64, win-arm64, osx-x64, osx-arm64, linux-x64 and linux-arm64. Windows publishing embeds managed and native dependencies in the executable; native dependencies are extracted by .NET at runtime. Unix publishing preserves native libraries in the output directory.

Windows UI and orchestration were tested with simulated UAT builds, including pointer drag/drop, row arrows, settings, success/failure, logs, preflight and cancellation. All six distributions were cross-compiled. macOS/Linux runtime UI, SDK builds, signing and cancellation need acceptance testing on actual hosts; they have not been represented as tested on Windows.

Framework platform requirements: https://docs.avaloniaui.net/docs/supported-platforms
