---
title: Builder and launcher share one version and releases continue from 3.1.0
author: Zerojinny
date: 2026-11-04
branch: main
cost: { minutes: 4, tokens: 311259 }
id: l-790eeca6
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
  - src/builder/Program.cs
  - src/launcher/launcher.c
---

## Decided
Releases continue the 3.x line after 3.1.0 (this one is 3.2.0), and the builder and the launcher always carry the same version. Tag format is vX.Y.Z; the GitHub release zip is eXTorrent-vX.Y.Z.zip.

Why: 3.1.0 is already published on GitHub. A lower number such as 0.4.1 was first proposed, then corrected by the owner, because it would sort below the existing release. The launcher had been left at 3.0.0 while the builder was 3.1.0, which made the version info in generated executables misleading.

## Tried and ruled out
- 0.4.1: lower than the published 3.1.0.
- Renaming or deleting the 3.1.0 release/tag: breaks links that may already be shared; not needed once 3.x continues.

## Watch out
- Bump both places together, then rebuild; generated exes take their version info from the launcher.
- No gh CLI on this machine: push the tag with git, then create the release in the browser and attach the zip.
- Release zip contains eXTorrent.exe, bin\launcher.exe, bin\aria2.conf.100/.1G, lang\*.ini, LICENSE, README.md, README.ko.md (no engine exe; the builder downloads it).

In the code:
- src/builder/Program.cs: AssemblyVersion / AssemblyFileVersion
- src/launcher/launcher.rc: FILEVERSION, PRODUCTVERSION, "FileVersion", "ProductVersion"
- tags: v2-autoit (old AutoIt version), v3.1.0, v3.2.0
