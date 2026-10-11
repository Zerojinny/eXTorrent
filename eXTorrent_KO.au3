#include <AutoItConstants.au3>
#include <GUIConstantsEx.au3>
#include <WindowsConstants.au3>
#include <EditConstants.au3>
#include <File.au3>
#include <StaticConstants.au3>

; --- 1. 설정값 불러오기 (INI 파일 처리) ---
Local $iniPath = @ScriptDir & "\config.ini"

Global $g_bSilent = False ; True면 MsgBox를 띄우지 않음 (CLI 모드)
Global $g_sLastErr = ""   ; 마지막 "!!" 에러 로그

; 명령줄 인수 확인 (드래그 앤 드롭으로 실행된 경우: 인수 1개)
Local $draggedFile = ""
If $CmdLine[0] > 0 Then
    $draggedFile = $CmdLine[1]
    If FileExists($draggedFile) Then
        Local $ext = StringLower(StringRight($draggedFile, 8))
        If Not StringInStr($ext, ".torrent") Then
            $draggedFile = "" ; torrent 파일이 아니면 무시
        EndIf
    Else
        $draggedFile = ""
    EndIf
EndIf

; 기본값 전용 INI 파일 (default.ini)
Local $defaultIniPath = @ScriptDir & "\default.ini"

Local $rawDefaultConfig = IniRead($defaultIniPath, "config", "content", "")

; start.bat은 INI가 아니라 bin\start.bat 파일을 직접 불러와 편집/저장한다.
Global $g_sStartBatPath = @ScriptDir & "\bin\start.bat"
Global $g_sSavedBat = "" ; 마지막으로 불러오거나 저장한 내용 (변경 감지용)
Local $sStartLoadMsg = ""
Local $defaultStart = ""
If FileExists($g_sStartBatPath) Then
    $defaultStart = _ReadTextFile($g_sStartBatPath)
    $g_sSavedBat = $defaultStart
    $sStartLoadMsg = "> start.bat을 불러왔습니다: " & $g_sStartBatPath
    ; 예전 버전이 config.ini에 남긴 start.bat 관련 항목 정리 (파일이 있을 때만)
    IniDelete($iniPath, "start")
    IniDelete($iniPath, "startbat")
Else
    $sStartLoadMsg = "!! start.bat 파일이 없습니다. 내용을 입력하고 '저장'을 누르면 생성됩니다: " & $g_sStartBatPath
EndIf

Local $defaultConfig = IniRead($iniPath, "config", "content", $rawDefaultConfig)
$defaultConfig = StringReplace($defaultConfig, "\n", @CRLF)

Local $defaultSavePath = IniRead($iniPath, "savepath", "path", @ScriptDir)
If Not FileExists($defaultSavePath) Then $defaultSavePath = @ScriptDir

; --- 1-1. 명령줄 모드: eXtorrent.exe "토렌트파일" "저장경로" [/q] ---
; 인수가 2개 이상이면 GUI 없이 바로 변환하고 종료한다.
If $CmdLine[0] >= 2 Then
    Local $bCliQuiet = ($CmdLine[0] >= 3 And StringLower($CmdLine[3]) = "/q")
    Exit _RunCli($CmdLine[1], $CmdLine[2], $defaultStart, $defaultConfig, $bCliQuiet)
EndIf

; --- 2. GUI 생성 ---
; 편집창용 고정폭 글꼴 (굴림체: 한글이 영문 2칸 폭이라 들여쓰기/정렬이 맞음)
; D2Coding 등을 설치해 두었다면 이름만 바꾸면 됨
Local $sEditFont = "GulimChe"
$defaultStart = StringRegExpReplace($defaultStart, "\r?\n", @CRLF)
$defaultConfig = StringRegExpReplace($defaultConfig, "\r?\n", @CRLF)

Local $hGUI = GUICreate("eXtorrent", 850, 720, -1, -1, -1, $WS_EX_ACCEPTFILES)

GUICtrlCreateGroup("1. 대상 파일 선택 (드래그 가능)", 10, 10, 830, 60)
Local $idInput = GUICtrlCreateInput("", 20, 35, 710, 20)
GUICtrlSetState(-1, $GUI_DROPACCEPTED)
Local $idBtnSelect = GUICtrlCreateButton("파일 선택", 740, 33, 90, 25)
GUICtrlCreateGroup("", -99, -99, 1, 1)

If $draggedFile <> "" Then
    GUICtrlSetData($idInput, $draggedFile)
EndIf

GUICtrlCreateGroup("2. 실행파일 저장 경로", 10, 80, 830, 60)
Local $idSavePathInput = GUICtrlCreateInput($defaultSavePath, 20, 105, 710, 20)
Local $idBtnSelectPath = GUICtrlCreateButton("경로 선택", 740, 103, 90, 25)
GUICtrlCreateGroup("", -99, -99, 1, 1)

GUICtrlCreateLabel("3. bin\start.bat (직접 편집)", 10, 155, 300, 15)
Local $idBtnBatSave = GUICtrlCreateButton("start.bat 저장", 320, 150, 100, 22)
Local $idStartBatText = GUICtrlCreateEdit($defaultStart, 10, 175, 410, 250, BitOR($WS_VSCROLL, $WS_HSCROLL, $ES_WANTRETURN, $ES_AUTOHSCROLL))
GUICtrlSetFont(-1, 9, 400, 0, $sEditFont)

GUICtrlCreateLabel("4. config.txt 설정 입력", 430, 155, 400, 15)
Local $idConfigText = GUICtrlCreateEdit($defaultConfig, 430, 175, 410, 250, BitOR($WS_VSCROLL, $WS_HSCROLL, $ES_WANTRETURN, $ES_AUTOHSCROLL))
GUICtrlSetFont(-1, 9, 400, 0, $sEditFont)

GUICtrlCreateLabel("5. 작업 로그", 10, 435, 400, 15)
Local $idLog = GUICtrlCreateEdit("", 10, 455, 600, 200, BitOR($ES_READONLY, $WS_VSCROLL, $WS_HSCROLL, $ES_AUTOHSCROLL))
GUICtrlSetFont(-1, 9, 400, 0, $sEditFont)
GUICtrlSetBkColor(-1, 0xF0F0F0)

Local $idBtnStart = GUICtrlCreateButton("실행 파일 생성 시작", 620, 455, 220, 100)
GUICtrlSetFont(-1, 12, 800)

Local $idBtnLoadIni = GUICtrlCreateButton("외부 INI 불러오기", 620, 565, 220, 40)
Local $idBtnSaveIni = GUICtrlCreateButton("현재 설정을 INI에 저장", 620, 615, 220, 40)

If $sStartLoadMsg <> "" Then GUICtrlSetData($idLog, $sStartLoadMsg & @CRLF, 1)

GUISetState(@SW_SHOW)

; --- 3. 메인 이벤트 루프 ---
While 1
    Switch GUIGetMsg()
        Case $GUI_EVENT_CLOSE
            If _ConfirmSaveBat($idStartBatText) Then Exit

        Case $idBtnBatSave
            _SaveStartBat(GUICtrlRead($idStartBatText), $idLog)

        Case $idBtnSelect
            Local $sFile = FileOpenDialog("대상 파일 선택", @WorkingDir, "토렌트 파일 (*.torrent)|모든 파일 (*.*)")
            If Not @error Then GUICtrlSetData($idInput, $sFile)

        Case $idBtnSelectPath
            Local $sPath = FileSelectFolder("실행파일 저장 경로 선택", "", 1, GUICtrlRead($idSavePathInput))
            If Not @error And $sPath <> "" Then
                GUICtrlSetData($idSavePathInput, $sPath)
                Local $writeResult = IniWrite($iniPath, "savepath", "path", $sPath)
                If $writeResult Then
                    GUICtrlSetData($idLog, "> 저장 경로가 변경되고 INI에 저장되었습니다: " & $sPath & @CRLF, 1)
                Else
                    GUICtrlSetData($idLog, "!! INI 저장 실패. 경로: " & $iniPath & @CRLF, 1)
                    MsgBox(48, "경고", "INI 파일 저장에 실패했습니다." & @CRLF & "경로: " & $iniPath)
                EndIf
            EndIf

        Case $idBtnLoadIni
            Local $sExtIni = FileOpenDialog("불러올 INI 파일 선택", @ScriptDir, "INI 파일 (*.ini)|모든 파일 (*.*)")
            If Not @error Then
                If FileExists($sExtIni) Then
                    Local $sExtConfig = IniRead($sExtIni, "config", "content", "")
                    Local $sExtSavePath = IniRead($sExtIni, "savepath", "path", "")

                    $sExtConfig = StringReplace($sExtConfig, "\n", @CRLF)

                    Local $bLoaded = False
                    If $sExtConfig <> "" Then
                        GUICtrlSetData($idConfigText, $sExtConfig)
                        $bLoaded = True
                    EndIf
                    If $sExtSavePath <> "" Then
                        GUICtrlSetData($idSavePathInput, $sExtSavePath)
                        $bLoaded = True
                    EndIf

                    If $bLoaded Then
                        GUICtrlSetData($idLog, "> 외부 INI 파일을 불러왔습니다: " & $sExtIni & @CRLF, 1)
                        MsgBox(64, "완료", "외부 INI 파일을 불러왔습니다." & @CRLF & "저장하려면 '현재 설정을 INI에 저장' 버튼을 눌러주세요.")
                    Else
                        GUICtrlSetData($idLog, "!! 선택한 INI 파일에서 유효한 데이터를 찾을 수 없습니다: " & $sExtIni & @CRLF, 1)
                        MsgBox(48, "알림", "선택한 파일에 [config], [savepath] 항목이 없습니다.")
                    EndIf
                Else
                    MsgBox(16, "오류", "파일을 찾을 수 없습니다.")
                EndIf
            EndIf

        Case $idBtnSaveIni
            Local $sCfgSave = StringReplace(GUICtrlRead($idConfigText), @CRLF, "\n")
            Local $sPathSave = GUICtrlRead($idSavePathInput)

            Local $r1 = IniWrite($iniPath, "config", "content", $sCfgSave)
            Local $r2 = IniWrite($iniPath, "savepath", "path", $sPathSave)

            If $r1 And $r2 Then
                MsgBox(64, "알림", "config.txt 내용과 저장 경로가 config.ini에 저장되었습니다." & @CRLF & "(start.bat은 'start.bat 저장' 버튼으로 bin\start.bat에 직접 저장합니다.)" & @CRLF & "경로: " & $iniPath)
                GUICtrlSetData($idLog, "> config.txt 내용과 저장 경로가 INI에 저장되었습니다." & @CRLF, 1)
            Else
                MsgBox(48, "경고", "일부 설정 저장에 실패했습니다." & @CRLF & "Config: " & $r1 & " / SavePath: " & $r2)
            EndIf

        Case $idBtnStart
            Local $sTarget = GUICtrlRead($idInput)
            Local $sSavePath = GUICtrlRead($idSavePathInput)

            If $sTarget = "" Or Not FileExists($sTarget) Then
                MsgBox(16, "오류", "대상 파일을 먼저 선택해주세요.")
            ElseIf $sSavePath = "" Or Not FileExists($sSavePath) Then
                MsgBox(16, "오류", "유효한 저장 경로를 선택해주세요.")
            Else
                ProcessPackaging($sTarget, $sSavePath, $idLog, GUICtrlRead($idStartBatText), GUICtrlRead($idConfigText))
            EndIf

        Case $GUI_EVENT_DROPPED
            GUICtrlSetData($idInput, @GUI_DRAGFILE)
    EndSwitch
WEnd

; --- 4. 패키징 로직 ---
; 래퍼: 성공/실패와 관계없이 항상 temp 폴더를 정리한다.
Func ProcessPackaging($tfull, $savePath, $logCtrl, $sBat, $sCfg)
    Local $workingDir = @ScriptDir & "\temp"
    Local $bOk = _DoPackaging($tfull, $savePath, $logCtrl, $sBat, $sCfg, $workingDir)
    If FileExists($workingDir) Then DirRemove($workingDir, 1)
    Return $bOk
EndFunc

; UTF-8(BOM 없음)으로 텍스트 파일 쓰기 (줄바꿈은 CRLF로 통일)
Func _WriteUtf8($sPath, $sText)
    Local $h = FileOpen($sPath, 2 + 256) ; 2=덮어쓰기, 256=UTF-8 (BOM 없음)
    If $h = -1 Then Return False
    FileWrite($h, StringRegExpReplace($sText, "\r?\n", @CRLF)) ; LF만 있는 텍스트도 CRLF로 통일
    FileClose($h)
    Return True
EndFunc

Func _DoPackaging($tfull, $savePath, $logCtrl, $sBat, $sCfg, $workingDir)
    _Log($logCtrl, "작업 시작..." & @CRLF)

    ; 7z.exe 존재 확인
    Local $s7zPath = @ScriptDir & '\bin\7z.exe'
    If Not FileExists($s7zPath) Then
        _Log($logCtrl, "!! 에러: 7z.exe를 찾을 수 없습니다: " & $s7zPath & @CRLF)
        _Msg(16, "오류", "7z.exe 파일이 없습니다." & @CRLF & "경로: " & $s7zPath)
        Return False
    EndIf

    Local $tDrive, $tDir, $tFName, $tExt
    _PathSplit($tfull, $tDrive, $tDir, $tFName, $tExt)
    Local $tfile = $tFName & $tExt
    Local $exfile = $tFName & ".exe"

    ; 작업 폴더 생성
    If Not FileExists($workingDir) Then DirCreate($workingDir)
    If Not FileExists($workingDir) Then
        _Log($logCtrl, "!! 에러: temp 폴더 생성 실패" & @CRLF)
        Return False
    EndIf

    FileDelete($workingDir & "\*.*")

    ; bin 폴더에서 가져올 필수 파일 목록
    ; (7zS.sfx는 SFX 모듈로만 쓰이고 7z 안에는 넣지 않음)
    Local $aBinFiles[5] = ["7zS.sfx", "aria2c.exe", "aria2.conf.100", "aria2.conf.1G", "getlinkspeed.bat"]

    _Log($logCtrl, "> 필수 파일 준비 중..." & @CRLF)

    ; 존재 확인
    For $i = 0 To UBound($aBinFiles) - 1
        If Not FileExists(@ScriptDir & "\bin\" & $aBinFiles[$i]) Then
            _Log($logCtrl, "!! 에러: " & $aBinFiles[$i] & " 파일이 없습니다." & @CRLF)
            _Msg(16, "오류", $aBinFiles[$i] & " 파일이 bin 폴더에 없습니다.")
            Return False
        EndIf
    Next

    ; 복사 및 복사 결과 확인
    For $i = 0 To UBound($aBinFiles) - 1
        FileCopy(@ScriptDir & "\bin\" & $aBinFiles[$i], $workingDir & "\" & $aBinFiles[$i], 1)
        If Not FileExists($workingDir & "\" & $aBinFiles[$i]) Then
            _Log($logCtrl, "!! 에러: " & $aBinFiles[$i] & " 복사 실패" & @CRLF)
            Return False
        EndIf
    Next

    FileCopy($tfull, $workingDir & "\" & $tfile, 1)
    If Not FileExists($workingDir & "\" & $tfile) Then
        _Log($logCtrl, "!! 에러: 대상 파일 복사 실패" & @CRLF)
        Return False
    EndIf

    ; tfile.txt, start.bat, config.txt 모두 UTF-8(BOM 없음)로 작성
    If Not _WriteUtf8($workingDir & "\tfile.txt", $tfile) Then
        _Log($logCtrl, "!! 에러: tfile.txt 작성 실패" & @CRLF)
        Return False
    EndIf
    If Not _WriteUtf8($workingDir & "\start.bat", $sBat) Then
        _Log($logCtrl, "!! 에러: start.bat 작성 실패" & @CRLF)
        Return False
    EndIf
    If Not _WriteUtf8($workingDir & "\config.txt", $sCfg) Then
        _Log($logCtrl, "!! 에러: config.txt 작성 실패" & @CRLF)
        Return False
    EndIf

    _Log($logCtrl, "> 압축 진행 중..." & @CRLF)

    ; 7z 명령 구성: 토렌트, aria2c, conf 2종, getlinkspeed.bat, start.bat, tfile.txt
    Local $cmd = '"' & $s7zPath & '" a -t7z "' & $workingDir & '\files.7z"' & _
        ' "' & $workingDir & '\' & $tfile & '"' & _
        ' "' & $workingDir & '\aria2c.exe"' & _
        ' "' & $workingDir & '\aria2.conf.100"' & _
        ' "' & $workingDir & '\aria2.conf.1G"' & _
        ' "' & $workingDir & '\getlinkspeed.bat"' & _
        ' "' & $workingDir & '\start.bat"' & _
        ' "' & $workingDir & '\tfile.txt"'

    _Log($logCtrl, "> 압축 명령: " & $cmd & @CRLF)

    Local $iPID = Run($cmd, $workingDir, @SW_HIDE, $STDERR_CHILD + $STDOUT_CHILD)
    If $iPID = 0 Then
        _Log($logCtrl, "!! 에러: 7z 실행 실패" & @CRLF)
        Return False
    EndIf

    Local $sOutput = ""
    Local $sError = ""

    ; 프로세스 출력 읽기 (프로세스가 종료되면 StdoutRead가 @error를 설정)
    While 1
        $sOutput &= StdoutRead($iPID)
        If @error Then ExitLoop
        $sError &= StderrRead($iPID)
        Sleep(50)
    WEnd
    $sError &= StderrRead($iPID)

    ; 실제 종료 코드는 ProcessWaitClose의 @extended
    ProcessWaitClose($iPID)
    Local $iExit = @extended

    ; 7z 결과 확인
    If $iExit > 1 Or Not FileExists($workingDir & "\files.7z") Then
        _Log($logCtrl, "!! 에러: 7z 압축 실패" & @CRLF)
        _Log($logCtrl, "종료 코드: " & $iExit & @CRLF)
        If $sOutput <> "" Then _Log($logCtrl, "출력: " & $sOutput & @CRLF)
        If $sError <> "" Then _Log($logCtrl, "에러: " & $sError & @CRLF)
        _Msg(16, "압축 실패", "7z 압축에 실패했습니다." & @CRLF & "종료 코드: " & $iExit & @CRLF & @CRLF & "에러: " & $sError)
        Return False
    EndIf

    If $iExit = 1 Then _Log($logCtrl, "> 경고: 7z가 경고와 함께 종료되었습니다 (종료 코드 1)." & @CRLF)

    _Log($logCtrl, "> 실행파일 생성 중..." & @CRLF)

    Local $hOutput = FileOpen($workingDir & "\" & $exfile, 17)
    If $hOutput = -1 Then
        _Log($logCtrl, "!! 에러: 출력 파일 생성 실패" & @CRLF)
        Return False
    EndIf

    FileWrite($hOutput, FileReadBinary($workingDir & "\7zS.sfx"))
    FileWrite($hOutput, FileReadBinary($workingDir & "\config.txt"))
    FileWrite($hOutput, FileReadBinary($workingDir & "\files.7z"))
    FileClose($hOutput)

    ; 저장 경로 확인
    If Not FileExists($savePath) Then
        DirCreate($savePath)
        If Not FileExists($savePath) Then
            _Log($logCtrl, "!! 에러: 저장 경로 생성 실패: " & $savePath & @CRLF)
            Return False
        EndIf
    EndIf

    FileCopy($workingDir & "\" & $exfile, $savePath & "\" & $exfile, 1)

    If FileExists($savePath & "\" & $exfile) Then
        _Log($logCtrl, "▶ 완료: " & $savePath & "\" & $exfile & @CRLF)
        _Msg(64, "성공", "패키징 완료!" & @CRLF & "저장 위치: " & $savePath & "\" & $exfile)
        Return True
    EndIf

    _Log($logCtrl, "!! 에러: 최종 파일 복사 실패" & @CRLF)
    Return False
EndFunc

Func FileReadBinary($sFilePath)
    Local $hFile = FileOpen($sFilePath, 16)
    Local $data = FileRead($hFile)
    FileClose($hFile)
    Return $data
EndFunc

; --- 5. start.bat 파일 읽기/저장 ---
; UTF-8 텍스트 파일 읽기 (BOM 유무와 관계없이), 줄바꿈은 CRLF로 통일
Func _ReadTextFile($sPath)
    Local $h = FileOpen($sPath, 256) ; 256 = UTF-8
    If $h = -1 Then Return SetError(1, 0, "")
    Local $sText = FileRead($h)
    FileClose($h)
    If StringLeft($sText, 1) = ChrW(0xFEFF) Then $sText = StringTrimLeft($sText, 1)
    Return StringRegExpReplace($sText, "\r?\n", @CRLF)
EndFunc

; start.bat 저장 (UTF-8 BOM 없음, CRLF)
Func _SaveStartBat($sText, $logCtrl = 0)
    Local $sDir = StringRegExpReplace($g_sStartBatPath, "\\[^\\]*$", "")
    If Not FileExists($sDir) Then DirCreate($sDir)
    If Not _WriteUtf8($g_sStartBatPath, $sText) Then
        If $logCtrl <> 0 Then GUICtrlSetData($logCtrl, "!! start.bat 저장 실패: " & $g_sStartBatPath & @CRLF, 1)
        MsgBox(48, "경고", "start.bat 저장에 실패했습니다." & @CRLF & $g_sStartBatPath)
        Return False
    EndIf
    $g_sSavedBat = StringRegExpReplace($sText, "\r?\n", @CRLF)
    If $logCtrl <> 0 Then GUICtrlSetData($logCtrl, "> start.bat을 저장했습니다: " & $g_sStartBatPath & @CRLF, 1)
    Return True
EndFunc

; 변경 내용이 저장되지 않았으면 저장 여부를 묻는다. 계속 진행해도 되면 True
Func _ConfirmSaveBat($ctrl)
    Local $sCur = GUICtrlRead($ctrl)
    If $sCur == $g_sSavedBat Then Return True
    Local $iAns = MsgBox(35, "start.bat 변경됨", "start.bat 내용이 저장되지 않았습니다." & @CRLF & @CRLF & _
            "예: 저장 후 계속" & @CRLF & "아니오: 저장하지 않고 계속" & @CRLF & "취소: 돌아가기")
    Switch $iAns
        Case 6
            Return _SaveStartBat($sCur)
        Case 7
            Return True
    EndSwitch
    Return False
EndFunc

; --- 6. 명령줄(CLI) 모드 ---
; 사용법: eXtorrent.exe "토렌트파일경로" "저장경로" [/q]
;   - 창 없이 바로 변환하고, 종료 코드 0=성공 / 1=실패
;   - start.bat은 bin\start.bat, config.txt 내용은 config.ini(없으면 default.ini)의 값을 사용
;   - /q 를 붙이면 결과 메시지 창도 띄우지 않음

; 로그: GUI 로그창(있을 때)과 콘솔에 기록, 마지막 에러 줄은 보관
Func _Log($logCtrl, $sMsg)
    If $logCtrl <> 0 Then GUICtrlSetData($logCtrl, $sMsg, 1)
    ConsoleWrite($sMsg)
    If StringLeft($sMsg, 2) = "!!" Then $g_sLastErr = StringStripWS($sMsg, 3)
EndFunc

; 메시지 박스: CLI 모드에서는 띄우지 않고 콘솔에만 기록
Func _Msg($iFlag, $sTitle, $sText)
    If $g_bSilent Then
        ConsoleWrite("[" & $sTitle & "] " & $sText & @CRLF)
        Return
    EndIf
    MsgBox($iFlag, $sTitle, $sText)
EndFunc

; 따옴표 제거 + 전체 경로화 + 끝의 역슬래시 제거 (드라이브 루트는 유지)
Func _CleanPath($sPath)
    $sPath = StringReplace($sPath, '"', "")
    $sPath = _PathFull($sPath)
    If StringLen($sPath) > 3 And StringRight($sPath, 1) = "\" Then $sPath = StringTrimRight($sPath, 1)
    Return $sPath
EndFunc

Func _RunCli($sTorrent, $sSavePath, $sBat, $sCfg, $bQuiet)
    $g_bSilent = True
    $sTorrent = _CleanPath($sTorrent)
    $sSavePath = _CleanPath($sSavePath)

    Local $bOk = False
    Local $sMsg = ""

    If Not FileExists($sTorrent) Then
        $sMsg = "토렌트 파일을 찾을 수 없습니다: " & $sTorrent
    ElseIf StringLower(StringRight($sTorrent, 8)) <> ".torrent" Then
        $sMsg = ".torrent 파일이 아닙니다: " & $sTorrent
    ElseIf StringStripWS($sBat, 8) = "" Then
        $sMsg = "bin\start.bat 파일이 없거나 비어 있습니다: " & $g_sStartBatPath
    Else
        If Not FileExists($sSavePath) Then DirCreate($sSavePath)
        If Not FileExists($sSavePath) Then
            $sMsg = "저장 경로를 만들 수 없습니다: " & $sSavePath
        Else
            $bOk = ProcessPackaging($sTorrent, $sSavePath, 0, $sBat, $sCfg)
            If $bOk Then
                Local $sDrive, $sDir, $sName, $sExt
                _PathSplit($sTorrent, $sDrive, $sDir, $sName, $sExt)
                $sMsg = "변환 완료: " & $sSavePath & "\" & $sName & ".exe"
            Else
                $sMsg = "변환 실패: " & $g_sLastErr
            EndIf
        EndIf
    EndIf

    ConsoleWrite($sMsg & @CRLF)
    If Not $bQuiet Then
        If $bOk Then
            MsgBox(64, "eXtorrent", $sMsg, 10)
        Else
            MsgBox(16, "eXtorrent", $sMsg, 10)
        EndIf
    EndIf
    If $bOk Then Return 0
    Return 1
EndFunc
