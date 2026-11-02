@echo off
rem ============================================================
rem  eXTorrent build
rem   - bin\launcher.exe : C launcher (MSVC, x64)
rem   - eXTorrent.exe    : C# builder (.NET Framework 4.x, csc.exe built into Windows)
rem  The download engine (bin\aria2c.exe or bin\aria2-next.exe) is appended by the
rem  builder, which downloads it from GitHub if missing.
rem  Note: ASCII only to avoid codepage problems.
rem ============================================================
setlocal
cd /d "%~dp0"

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
set "VSDIR="
if exist "%VSWHERE%" for /f "usebackq delims=" %%I in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSDIR=%%I"
if not defined VSDIR goto :err_vs

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" goto :err_csc

call "%VSDIR%\VC\Auxiliary\Build\vcvars64.bat" >nul 2>&1 || goto :err_vs
if not exist build mkdir build

echo [1/2] launcher.exe
rc /nologo /fo build\launcher.res src\launcher\launcher.rc || goto :fail
cl /nologo /utf-8 /O1 /MT /W4 /DUNICODE /D_UNICODE /Fobuild\ /Febin\launcher.exe ^
   src\launcher\launcher.c build\launcher.res ^
   /link /SUBSYSTEM:WINDOWS /MANIFEST:NO /DYNAMICBASE /NXCOMPAT /OPT:REF /OPT:ICF || goto :fail

echo [2/2] eXTorrent.exe
set "B=src\builder"
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /utf8output ^
   /win32icon:extorrent.ico /win32manifest:%B%\builder.manifest /out:eXTorrent.exe ^
   /lib:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF" ^
   /r:System.dll /r:System.Core.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Xaml.dll /r:WindowsBase.dll /r:PresentationCore.dll /r:PresentationFramework.dll ^
   /resource:%B%\Styles.xaml,EXTorrent.Styles.xaml ^
   /resource:%B%\MainWindow.xaml,EXTorrent.MainWindow.xaml ^
   /resource:%B%\Dialog.xaml,EXTorrent.Dialog.xaml ^
   /resource:%B%\FilePicker.xaml,EXTorrent.FilePicker.xaml ^
   /resource:extorrent.ico,EXTorrent.app.ico ^
   /resource:lang\ko.ini,EXTorrent.ko.ini ^
   %B%\*.cs || goto :fail

echo.
echo Build OK
exit /b 0

:err_vs
echo [ERROR] Visual Studio C++ build tools not found.
exit /b 1
:err_csc
echo [ERROR] csc.exe not found: %CSC%
exit /b 1
:fail
echo [ERROR] Build failed.
exit /b 1
