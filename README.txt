UE Packaging Automation

Start UE-Packaging-Automation.exe or Launch GUI.lnk. No console window is shown.

1. Select a .uproject and its Unreal Engine installation.
2. Check platforms. Drag rows or use the right-side arrows to set their build order.
3. Select the destination (default: <project>/Builds). Missing folders are created automatically.
4. Save and close Unreal Editor, then choose Check setup or Start packaging.

Checked platforms run from top to bottom. Moving rows preserves checkboxes and status.
Outputs: <destination>/<platform>; Windows builds use the Windows folder.
Logs: <destination>/Logs. Settings.json remembers your choices.
Rows show Waiting, Running, Completed, Failed, Not built, Stopped or Setup checked.
A failed platform stops the remaining queue. Stop terminates the current build process tree.
Existing outputs are updated. Keep complete platform output folders.

Platform availability follows the selected engine. Additional SDKs, signing, licensed
platform extensions or a remote Mac may be required. Check setup does not perform a build.
Android formats: ASTC, ETC2, DXT and Multi. Other packaging options follow project settings.
When Saved/AndroidToolchain exists, the tool uses its verified local SDK/NDK/JDK layout;
otherwise it uses the installed environment. Unreal's RunUAT performs the actual packaging.

Maintenance: Source contains the application sources. Run Source/Build.cmd to rebuild;
the executable is generated in Source. Copy it to the tool folder after closing the app.
