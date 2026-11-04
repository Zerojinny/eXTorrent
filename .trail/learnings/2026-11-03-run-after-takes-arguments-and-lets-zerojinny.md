---
title: Run-after takes arguments and lets Windows prompt for admin; failures are reported
author: Zerojinny
date: 2026-11-03
branch: main
cost: { minutes: 6, tokens: 47548 }
id: l-afdcef32
scope: repo
status: active
replaces: l-884d4e1a
src: agent
touches:
  - src/launcher/launcher.c
  - lang/ko.ini
  - lang/en.ini
  - src/builder/MainWindow.xaml
  - src/builder/MainWindow.cs
  - CLAUDE.md
  - README.ko.md
  - README.md
  - src/builder/Config.cs
---

## Decided
"Run when done" accepts arguments and is started with ShellExecuteEx, so an installer that needs administrator rights is pointed to directly (e.g. `folder\setup.exe -inputFile "input.txt"`) and Windows shows the UAC prompt from the exe's manifest. The launcher itself never elevates. A non-zero exit code, or a declined UAC prompt, is shown as a warning in the progress window instead of a silent "complete".

Why: inside a .bat, cmd starts a requireAdministrator exe with CreateProcess, which cannot show UAC and fails with error 740; the console flashed and closed and the launcher still said "complete" (seen with MATLAB R2024a setup.exe via auto_install.bat; cmd reports exit code 1).

## Tried and ruled out
- Launcher runs the target with the "runas" verb: breaks the no-elevation rule that keeps antivirus false positives down.
- Only telling users to write `start "" /wait setup.exe ...` in their batch: works, but needs a new torrent and leaves the silent failure in place.

## Watch out
- Splitting path from arguments: `"path with spaces" args`, or unquoted, where each prefix ending at a space is tried and the first existing file wins (same rule as CreateProcess). If nothing matches, the whole value is treated as the path.
- Some installers return non-zero on success (e.g. 3010 = reboot needed); that is shown as a warning, not a failure.

In the code:
- src/launcher/launcher.c: SplitRunAfter(), RunAfterPath(), RunAfter() (lpParameters, ERROR_CANCELLED, GetExitCodeProcess)
- lang/*.ini [launcher]: runafter_cancelled, runafter_exit; [builder] dl.runAfter.hint
- src/builder/MainWindow.cs PickRunAfter(): quotes a picked path that contains spaces
