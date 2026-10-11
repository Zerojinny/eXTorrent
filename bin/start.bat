@echo off
chcp 65001 >nul
setlocal

rem ===== 사용자 설정 =====
set "SAVEPATH=C:\Temp"
set "RunFirst="
set "FWRULE=eXTorrent_Temp"

rem ===== 관리자 권한 확인 =====
reg.exe query "HKU\S-1-5-20" >nul 2>&1
if errorlevel 1 goto :elevate

pushd "%~dp0"
set "OK="

rem ===== 토렌트 파일명 읽기 =====
if not exist "tfile.txt" goto :err_tfile
set "TFILE="
set /p TFILE=<tfile.txt
if not defined TFILE goto :err_tfile_empty
if not exist "%TFILE%" goto :err_torrent

rem ===== 저장 경로 =====
if not exist "%SAVEPATH%" mkdir "%SAVEPATH%" 2>nul
if not exist "%SAVEPATH%" goto :err_mkdir

rem ===== 링크 속도 확인 =====
rem getlinkspeed.bat는 setlocal을 쓰지 않으므로 linkspeed("1 Gbps" 등)와
rem speed(27/80/40/0) 변수가 이 스크립트(및 이후 call하는 %RunFirst%)에 그대로 남는다.
set "linkspeed="
set "speed=0"
if exist "getlinkspeed.bat" call getlinkspeed.bat

rem Gbps 단위면 1G 설정, 그 외(100/10 Mbps, 감지 실패)는 100 설정
set "PROFILE=100"
echo %linkspeed%| findstr /i /c:"Gbps" >nul && set "PROFILE=1G"
if not exist "aria2.conf.%PROFILE%" goto :err_conf

echo ========================================
echo eXTorrent 다운로더
echo ========================================
echo 파일     : %TFILE%
echo 저장 위치: %SAVEPATH%
echo 링크 속도: %linkspeed%  ^>  aria2.conf.%PROFILE%
echo ========================================
echo.

rem ===== 방화벽 규칙 추가 (이전 규칙이 남아 있으면 먼저 삭제) =====
echo [1/3] 방화벽 규칙 추가 중...
netsh advfirewall firewall delete rule name="%FWRULE%" >nul 2>&1
netsh advfirewall firewall add rule name="%FWRULE%" dir=in program="%~dp0aria2c.exe" action=allow >nul 2>&1
netsh advfirewall firewall add rule name="%FWRULE%" dir=out program="%~dp0aria2c.exe" action=allow >nul 2>&1

rem ===== 다운로드 =====
echo [2/3] 다운로드 시작...
aria2c.exe -T "%TFILE%" --dir="%SAVEPATH%" --conf-path="aria2.conf.%PROFILE%"
set "RC=%ERRORLEVEL%"

if not "%RC%"=="0" goto :download_failed
echo.
echo [완료] 다운로드 성공!
set "OK=1"
goto :cleanup

:download_failed
color 4F
echo.
echo [오류] 다운로드 실패 (aria2c 종료 코드 %RC%)

:cleanup
echo [3/3] 정리 중...
netsh advfirewall firewall delete rule name="%FWRULE%" >nul 2>&1

if not defined OK goto :fail

for /F %%a in ( tfile.txt ) do set tfile=%%a
set tfile=%tfile: =%
for /F "delims=." %%a in ( tfile.txt ) do set tpath=%%a
if not defined RunFirst (
	echo.
	echo 작업이 완료되었습니다.
	explorer.exe /select, "%SAVEPATH%\%tpath%"
	goto end
	)

rem ===== 후처리 (성공했을 때만) =====
if exist "%RunFirst%" (
    call "%RunFirst%"
) else (
    echo [경고] 후처리 파일이 없어 건너뜁니다: %RunFirst%
)

echo.
echo 작업이 완료되었습니다.
goto :end

rem ===== 오류 처리 =====
:err_tfile
echo [오류] tfile.txt 파일을 찾을 수 없습니다.
goto :fail
:err_tfile_empty
echo [오류] tfile.txt가 비어있습니다.
goto :fail
:err_torrent
echo [오류] 토렌트 파일을 찾을 수 없습니다: %TFILE%
goto :fail
:err_mkdir
echo [오류] 폴더 생성 실패: %SAVEPATH%
goto :fail
:err_conf
echo [오류] 설정 파일이 없습니다: aria2.conf.%PROFILE%
goto :fail

:fail
color 4F
echo.
echo 오류가 발생하여 작업을 중단합니다.

:end
pause
endlocal
exit /b

rem ===== 관리자 권한으로 재실행 =====
rem -Wait: 원래 인스턴스가 끝나지 않고 대기하므로 SFX 임시 폴더가 먼저 삭제되지 않음
:elevate
PowerShell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs -Wait"
if errorlevel 1 (
    echo [오류] 관리자 권한 실행에 실패했거나 취소되었습니다.
    pause
)
exit /b