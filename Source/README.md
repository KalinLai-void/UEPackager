# UEPackager

**Automated Multi-Platform Packaging for Unreal Engine**

UEPackager is a desktop tool for packaging Unreal Engine projects for multiple target platforms. Select your project and engine, arrange the target platforms, and start the queue. UEPackager runs Unreal AutomationTool (UAT) for each selected platform in sequence, with status updates and logs in one window.

## Download

Open this repository's **Releases** page and download the archive for the computer that will run UEPackager:

| Operating system | Architecture | Release asset |
| --- | --- | --- |
| Windows | x64 | `UEPackager-win-x64.zip` |
| Windows | ARM64 | `UEPackager-win-arm64.zip` |
| macOS | Intel | `UEPackager-osx-x64.tar.gz` |
| macOS | Apple Silicon | `UEPackager-osx-arm64.tar.gz` |
| Linux | x64 | `UEPackager-linux-x64.tar.gz` |
| Linux | ARM64 | `UEPackager-linux-arm64.tar.gz` |

Choose the archive for your **host computer**, regardless of which platforms you want to package your Unreal project for. Download a UEPackager release asset rather than GitHub's automatically generated **Source code** archives, which do not contain a ready-to-run application.

Release builds include the .NET runtime; you do not need to install .NET separately to run them. `SHA256SUMS.txt`, when included with the release, contains archive checksums for download verification.

## Requirements

- An Unreal Engine installation and an Unreal project (`.uproject`).
- The build tools, SDKs, signing credentials, and any platform extensions required by your target platforms.
- A host operating system that supports building the selected targets. Running UEPackager on Windows, macOS, or Linux does not make every target buildable from every host.
- On Linux, a desktop session with X11 or XWayland and the required native libraries.

## Launch

Extract the entire archive before launching.

- **Windows:** open `UEPackager.exe`. No console window is opened.
- **macOS:** open `UEPackager.app`. Keep the complete application bundle. Releases are unsigned and not notarized, so macOS may require approval in **Privacy & Security**.
- **Linux:** open a terminal in the extracted folder and run `./Launch.sh` or `./UEPackager`. Keep all included libraries alongside the executable.

## Package a project

1. **Select the project.** Browse to your `.uproject` file.
2. **Verify the engine.** Select the Unreal Engine installation folder containing the `Engine` directory. Registered or common engine locations are detected when possible; check that the selected version is correct.
3. **Choose target platforms.** Check the platforms you want to package. Drag platform names, use the row arrows, or select a platform and use **Move up** / **Move down** to set the order. Checked platforms run from top to bottom.
4. **Choose Android textures, if applicable.** Select ASTC (default), ETC2, DXT, or Multi.
5. **Set the destination.** The default is `<project>/Builds`. Missing folders are created automatically. Changing projects resets the destination; **Use project Builds** restores the default.
6. **Save and close Unreal Editor.** Save your project before packaging.
7. **Check setup.** Run the preflight check to catch setup issues. This does not perform a build or guarantee that all SDK and signing requirements are satisfied.
8. **Start packaging.** Watch platform statuses and the log output. Use **Open destination** to view the generated files.

### Outputs and queue behavior

- Packaged builds are saved under `<destination>/<platform>`. Windows builds use the `Windows` folder.
- Logs and per-run request snapshots are saved under `<destination>/Logs`.
- Existing outputs are updated. Keep each platform's complete output folder, including Android expansion files when generated.
- A failed platform stops the remaining queue. Review the log, fix the cause, and run the required platforms again.
- **Stop** terminates the active build process tree. Closing the application during a build asks whether to stop it first.
- Only one packaging session may use a given project/destination at a time.

Platform statuses include **Waiting**, **Running**, **Completed**, **Failed**, **Not built**, **Stopped**, and **Setup checked**. Completed requires a successful UAT exit code and non-empty archived output.

### Build configuration

Packaging uses **Development** configuration with build, cook, stage, pak, package, and archive enabled, plus `DisableUnity` and `NoUBA`. Maps, CPU architectures, signing, remote Mac configuration, and other Unreal packaging options come from your project's settings.

Available platform extensions are discovered from the selected engine. Console targets require authorized platform extensions and SDKs. Windows runs `RunUAT.bat`; macOS and Linux run `RunUAT.sh` through Bash.

When a compatible `Saved/AndroidToolchain` exists, the tool uses its local SDK, NDK 27.2.12479018, and JDK 21 layout. Otherwise, it uses the installed environment.

## Settings

Selections are saved per user. For compatibility with existing installations, the settings directory retains its original name: `UE-Packaging-Automation/Settings.json` under the operating system's application-data directory.

On Windows, the usual location is `%APPDATA%\UE-Packaging-Automation\Settings.json`. An existing `Settings.json` beside the executable is imported on first launch if no user settings exist. Settings inside a macOS bundle are not modified.

## Troubleshooting

- **A platform is unavailable:** verify the selected engine, host support, platform extensions, and SDK installation.
- **Check setup passes but packaging fails:** inspect the log under `<destination>/Logs`. Actual builds may reveal SDK, signing, compiler, or project errors that preflight cannot detect.
- **The application fails after moving files:** extract the full release archive again. Keep the macOS bundle intact and retain the libraries shipped with Linux builds.

## Build from source

The Git repository contains source code and publishing scripts. Generated executables, `Releases/`, `Packages/`, local settings, and local shortcuts are excluded by `.gitignore`. Ready-to-run archives are distributed as release assets.

Install a .NET 8 or newer SDK, then run from the repository root:

```sh
dotnet build Source/PackagingAutomation.csproj -c Release
```

To publish self-contained builds for all six host architectures:

**PowerShell:**

```powershell
./Source/Publish.ps1
```

**macOS / Linux:**

```sh
bash Source/publish.sh
```

Published application files are written to `Releases/<runtime>/` by default. To create release archives and `SHA256SUMS.txt`, run the following with PowerShell 7.4 or newer:

```powershell
./Source/PackageReleases.ps1
```

The archives are written to `Packages/` by default. Upload the generated `.zip`, `.tar.gz`, and `SHA256SUMS.txt` files as assets on the repository's Releases page. The packaging script includes `Source/README.md` in each archive; keep it synchronized with this README before generating a release.

## Validation status

Windows UI and orchestration were tested with simulated UAT builds, including platform ordering, settings, success/failure, logs, preflight, and cancellation. All six distributions were cross-compiled. macOS/Linux runtime UI, actual SDK builds, signing, and cancellation still require acceptance testing on their respective hosts.
