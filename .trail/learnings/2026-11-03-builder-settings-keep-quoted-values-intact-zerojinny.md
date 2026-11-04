---
title: Builder settings keep quoted values intact across save and load
author: Zerojinny
date: 2026-11-03
branch: main
id: l-93e38ce8
scope: repo
status: active
src: agent
touches:
  - CLAUDE.md
  - README.ko.md
  - README.md
  - lang/en.ini
  - lang/ko.ini
  - src/builder/Config.cs
  - src/builder/MainWindow.cs
  - src/builder/MainWindow.xaml
  - src/launcher/launcher.c
---

## Decided
When saving builder settings, a value that both starts and ends with a double quote is wrapped in one extra pair of quotes.

Why: the Win32 profile API strips one outer pair of quotes on read. A run-after value like `"folder\my tool.bat" one "two three"` came back as `folder\my tool.bat" one "two three` after "Save settings" or in command-line mode, so the file was "not found". Building straight from the GUI was unaffected, which hid it.

## Watch out
- Any new config.ini value that can legitimately start and end with quotes needs the same treatment.
- The launcher's settings.ini reader is our own parser and does not strip quotes; only config.ini (builder side) has this behaviour.

In the code:
- src/builder/Config.cs: KeepQuotes() used in Save() for RunAfter; Read() uses GetPrivateProfileString
