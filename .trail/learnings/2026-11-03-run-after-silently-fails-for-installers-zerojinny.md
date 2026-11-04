---
title: Run-after silently fails for installers that need admin when started from a batch file
author: Zerojinny
date: 2026-11-03
branch: main
id: l-884d4e1a
scope: repo
status: active
src: agent
touches:
  - src/launcher/launcher.c
---

## Decided
(Diagnosis; fix not chosen yet.) "Run when done" silently does nothing when the target needs administrator rights and is started from a batch file. The launcher runs as the user (asInvoker) and must not elevate itself.

Why it fails: inside a .bat, a plain command like `setup.exe ...` is started by cmd with CreateProcess. That cannot show a UAC prompt, so a requireAdministrator exe fails with error 740. The console flashes and closes, and the launcher still reports "download complete" because it only checks that the batch started.

## Tried and ruled out
- Launcher runs the .bat with the "runas" verb: breaks the no-elevation rule that keeps antivirus false positives down.

## Watch out
- Workaround that needs no code change: in the batch, use `start "" /wait setup.exe ...`. `start` goes through ShellExecute, so Windows shows UAC and the launcher waits.
- Proposed fix: allow arguments in RunAfter so the exe is started directly with ShellExecuteEx (Windows elevates from the exe's manifest), and warn when the program exits with a non-zero code.

In the code:
- src/launcher/launcher.c RunAfter(): ShellExecuteExW verb "open", lpParameters unused, exit code not checked
- Seen with MATLAB R2024a setup.exe (requestedExecutionLevel requireAdministrator) via auto_install.bat
