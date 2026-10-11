@echo off
setlocal enabledelayedexpansion

rem ============================================================
rem  Detect active NIC link speed WITHOUT creating any .ps1 file
rem  and set "speed" / "linkspeed" variables for the caller.
rem   - 100 Mbps        -> speed=80
rem   - 1 Gbps or above -> speed=27
rem   - 10 Mbps         -> speed=40
rem   - other/unknown   -> speed=0
rem
rem  Method 1: wmic (fast; deprecated, may be missing on new Windows 11)
rem  Method 2: powershell -Command "..." (inline command, no script file)
rem  Only connected physical adapters are considered; the fastest
rem  one is used. linkspeed is a string such as "1 Gbps".
rem  Note: ASCII only to avoid codepage problems.
rem ============================================================

set "best=-1"
set "linkspeed="
set "speed=0"
set "rc=0"
set "src="

rem ---- Method 1: wmic (Speed is in bits per second) ----
set "src=wmic"
where wmic >nul 2>&1
if errorlevel 1 goto :try_ps
for /f "tokens=2 delims==" %%A in ('wmic nic where "PhysicalAdapter=true and NetConnectionStatus=2" get Speed /value 2^>nul ^| find "="') do call :proc %%A
if not "%best%"=="-1" goto :detected

rem ---- Method 2: inline PowerShell command (not a .ps1 script) ----
:try_ps
set "src=powershell"
for /f "usebackq delims=" %%L in (`powershell -NoProfile -Command "Get-NetAdapter | Where-Object { $_.Status -eq 'Up' -and $_.Virtual -eq $false } | Sort-Object ReceiveLinkSpeed -Descending | Select-Object -First 1 -ExpandProperty ReceiveLinkSpeed" 2^>nul`) do call :proc %%L
if not "%best%"=="-1" goto :detected

echo [ERROR] Could not detect active NIC link speed.
goto :end

:detected
if %best% GEQ 1000 (
    set /a g=best/1000
    set "linkspeed=!g! Gbps"
) else (
    set "linkspeed=%best% Mbps"
)

echo Detected link speed: %linkspeed% [%src%]

rem 1 Gbps or above (also covers 2.5/5/10/25/40 Gbps etc.)
if %best% GEQ 1000 (
    set "speed=27"
    goto :end
)

rem 100 Mbps
if %best% EQU 100 (
    set "speed=80"
    goto :end
)

rem 10 Mbps
if %best% EQU 10 (
    set "speed=40"
    goto :end
)

rem Unknown speed range
echo [WARNING] Link speed does not match known ranges: %linkspeed%
set "speed=0"
set "rc=1"

:end
echo speed=%speed%
endlocal & set "speed=%speed%" & set "linkspeed=%linkspeed%" & exit /b %rc%

rem ---- subroutine: bps -> Mbps by cutting 6 digits (avoids 32-bit overflow) ----
:proc
set "val=%~1"
echo %val%| findstr /r "^[0-9][0-9]*$" >nul || exit /b
set "mbps=0"
if not "%val:~6%"=="" set "mbps=%val:~0,-6%"
if %mbps% GTR %best% set "best=%mbps%"
exit /b
