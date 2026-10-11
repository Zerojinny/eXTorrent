#include <AutoItConstants.au3>
#include <GUIConstantsEx.au3>
#include <WindowsConstants.au3>
#include <EditConstants.au3>
#include <File.au3>
#include <StaticConstants.au3>

; --- 1. Load settings (INI file handling) ---
Local $iniPath = @ScriptDir & "\config.ini"

Global $g_bSilent = False ; If True, no MsgBox is shown (CLI mode)
Global $g_sLastErr = ""   ; Last "!!" error log

; Check command-line arguments (launched by drag and drop: 1 argument)
Local $draggedFile = ""
If $CmdLine[0] > 0 Then
    $draggedFile = $CmdLine[1]
    If FileExists($draggedFile) Then
        Local $ext = StringLower(StringRight($draggedFile, 8))
        If Not StringInStr($ext, ".torrent") Then
            $draggedFile = "" ; Ignore if not a torrent file
        EndIf
    Else
        $draggedFile = ""
    EndIf
EndIf

; INI file for default values (default.ini)
Local $defaultIniPath = @ScriptDir & "\default.ini"

Local $rawDefaultConfig = IniRead($defaultIniPath, "config", "content", "")

; start.bat is not stored in the INI; bin\start.bat is loaded directly for editing/saving.
Global $g_sStartBatPath = @ScriptDir & "\bin\start.bat"
Global $g_sSavedBat = "" ; Content last loaded or saved (for change detection)
Local $sStartLoadMsg = ""
Local $defaultStart = ""
If FileExists($g_sStartBatPath) Then
    $defaultStart = _ReadTextFile($g_sStartBatPath)
    $g_sSavedBat = $defaultStart
    $sStartLoadMsg = "> Loaded start.bat: " & $g_sStartBatPath
    ; Clean up start.bat entries left in config.ini by older versions (only when the file exists)
    IniDelete($iniPath, "start")
    IniDelete($iniPath, "startbat")
Else
    $sStartLoadMsg = "!! start.bat not found. Enter the content and press 'Save start.bat' to create it: " & $g_sStartBatPath
EndIf

Local $defaultConfig = IniRead($iniPath, "config", "content", $rawDefaultConfig)
$defaultConfig = StringReplace($defaultConfig, "\n", @CRLF)

Local $defaultSavePath = IniRead($iniPath, "savepath", "path", @ScriptDir)
If Not FileExists($defaultSavePath) Then $defaultSavePath = @ScriptDir

; --- 1-1. Command-line mode: eXtorrent.exe "torrentfile" "savepath" [/q] ---
; With 2 or more arguments, convert immediately without a GUI and exit.
If $CmdLine[0] >= 2 Then
    Local $bCliQuiet = ($CmdLine[0] >= 3 And StringLower($CmdLine[3]) = "/q")
    Exit _RunCli($CmdLine[1], $CmdLine[2], $defaultStart, $defaultConfig, $bCliQuiet)
EndIf

; --- 2. Create GUI ---
; Monospaced font for the edit boxes (GulimChe: Korean characters take two columns, so indentation/alignment lines up)
; If you have D2Coding or similar installed, just change the name
Local $sEditFont = "GulimChe"
$defaultStart = StringRegExpReplace($defaultStart, "\r?\n", @CRLF)
$defaultConfig = StringRegExpReplace($defaultConfig, "\r?\n", @CRLF)

Local $hGUI = GUICreate("eXtorrent", 850, 720, -1, -1, -1, $WS_EX_ACCEPTFILES)

GUICtrlCreateGroup("1. Select target file (drag and drop supported)", 10, 10, 830, 60)
Local $idInput = GUICtrlCreateInput("", 20, 35, 710, 20)
GUICtrlSetState(-1, $GUI_DROPACCEPTED)
Local $idBtnSelect = GUICtrlCreateButton("Select File", 740, 33, 90, 25)
GUICtrlCreateGroup("", -99, -99, 1, 1)

If $draggedFile <> "" Then
    GUICtrlSetData($idInput, $draggedFile)
EndIf

GUICtrlCreateGroup("2. Executable save path", 10, 80, 830, 60)
Local $idSavePathInput = GUICtrlCreateInput($defaultSavePath, 20, 105, 710, 20)
Local $idBtnSelectPath = GUICtrlCreateButton("Select Path", 740, 103, 90, 25)
GUICtrlCreateGroup("", -99, -99, 1, 1)

GUICtrlCreateLabel("3. bin\start.bat (edit directly)", 10, 155, 300, 15)
Local $idBtnBatSave = GUICtrlCreateButton("Save start.bat", 320, 150, 100, 22)
Local $idStartBatText = GUICtrlCreateEdit($defaultStart, 10, 175, 410, 250, BitOR($WS_VSCROLL, $WS_HSCROLL, $ES_WANTRETURN, $ES_AUTOHSCROLL))
GUICtrlSetFont(-1, 9, 400, 0, $sEditFont)

GUICtrlCreateLabel("4. Enter config.txt settings", 430, 155, 400, 15)
Local $idConfigText = GUICtrlCreateEdit($defaultConfig, 430, 175, 410, 250, BitOR($WS_VSCROLL, $WS_HSCROLL, $ES_WANTRETURN, $ES_AUTOHSCROLL))
GUICtrlSetFont(-1, 9, 400, 0, $sEditFont)

GUICtrlCreateLabel("5. Work log", 10, 435, 400, 15)
Local $idLog = GUICtrlCreateEdit("", 10, 455, 600, 200, BitOR($ES_READONLY, $WS_VSCROLL, $WS_HSCROLL, $ES_AUTOHSCROLL))
GUICtrlSetFont(-1, 9, 400, 0, $sEditFont)
GUICtrlSetBkColor(-1, 0xF0F0F0)

Local $idBtnStart = GUICtrlCreateButton("Start Creating Executable", 620, 455, 220, 100)
GUICtrlSetFont(-1, 12, 800)

Local $idBtnLoadIni = GUICtrlCreateButton("Load External INI", 620, 565, 220, 40)
Local $idBtnSaveIni = GUICtrlCreateButton("Save Current Settings to INI", 620, 615, 220, 40)

If $sStartLoadMsg <> "" Then GUICtrlSetData($idLog, $sStartLoadMsg & @CRLF, 1)

GUISetState(@SW_SHOW)

; --- 3. Main event loop ---
While 1
    Switch GUIGetMsg()
        Case $GUI_EVENT_CLOSE
            If _ConfirmSaveBat($idStartBatText) Then Exit

        Case $idBtnBatSave
            _SaveStartBat(GUICtrlRead($idStartBatText), $idLog)

        Case $idBtnSelect
            Local $sFile = FileOpenDialog("Select target file", @WorkingDir, "Torrent files (*.torrent)|All files (*.*)")
            If Not @error Then GUICtrlSetData($idInput, $sFile)

        Case $idBtnSelectPath
            Local $sPath = FileSelectFolder("Select executable save path", "", 1, GUICtrlRead($idSavePathInput))
            If Not @error And $sPath <> "" Then
                GUICtrlSetData($idSavePathInput, $sPath)
                Local $writeResult = IniWrite($iniPath, "savepath", "path", $sPath)
                If $writeResult Then
                    GUICtrlSetData($idLog, "> Save path changed and saved to INI: " & $sPath & @CRLF, 1)
                Else
                    GUICtrlSetData($idLog, "!! Failed to save INI. Path: " & $iniPath & @CRLF, 1)
                    MsgBox(48, "Warning", "Failed to save the INI file." & @CRLF & "Path: " & $iniPath)
                EndIf
            EndIf

        Case $idBtnLoadIni
            Local $sExtIni = FileOpenDialog("Select INI file to load", @ScriptDir, "INI files (*.ini)|All files (*.*)")
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
                        GUICtrlSetData($idLog, "> Loaded external INI file: " & $sExtIni & @CRLF, 1)
                        MsgBox(64, "Done", "Loaded the external INI file." & @CRLF & "Click 'Save Current Settings to INI' to save it.")
                    Else
                        GUICtrlSetData($idLog, "!! No valid data found in the selected INI file: " & $sExtIni & @CRLF, 1)
                        MsgBox(48, "Notice", "The selected file has no [config] or [savepath] section.")
                    EndIf
                Else
                    MsgBox(16, "Error", "File not found.")
                EndIf
            EndIf

        Case $idBtnSaveIni
            Local $sCfgSave = StringReplace(GUICtrlRead($idConfigText), @CRLF, "\n")
            Local $sPathSave = GUICtrlRead($idSavePathInput)

            Local $r1 = IniWrite($iniPath, "config", "content", $sCfgSave)
            Local $r2 = IniWrite($iniPath, "savepath", "path", $sPathSave)

            If $r1 And $r2 Then
                MsgBox(64, "Notice", "The config.txt content and save path were saved to config.ini." & @CRLF & "(start.bat is saved directly to bin\start.bat with the 'Save start.bat' button.)" & @CRLF & "Path: " & $iniPath)
                GUICtrlSetData($idLog, "> The config.txt content and save path were saved to the INI." & @CRLF, 1)
            Else
                MsgBox(48, "Warning", "Failed to save some settings." & @CRLF & "Config: " & $r1 & " / SavePath: " & $r2)
            EndIf

        Case $idBtnStart
            Local $sTarget = GUICtrlRead($idInput)
            Local $sSavePath = GUICtrlRead($idSavePathInput)

            If $sTarget = "" Or Not FileExists($sTarget) Then
                MsgBox(16, "Error", "Please select a target file first.")
            ElseIf $sSavePath = "" Or Not FileExists($sSavePath) Then
                MsgBox(16, "Error", "Please select a valid save path.")
            Else
                ProcessPackaging($sTarget, $sSavePath, $idLog, GUICtrlRead($idStartBatText), GUICtrlRead($idConfigText))
            EndIf

        Case $GUI_EVENT_DROPPED
            GUICtrlSetData($idInput, @GUI_DRAGFILE)
    EndSwitch
WEnd

; --- 4. Packaging logic ---
; Wrapper: always cleans up the temp folder, whether it succeeds or fails.
Func ProcessPackaging($tfull, $savePath, $logCtrl, $sBat, $sCfg)
    Local $workingDir = @ScriptDir & "\temp"
    Local $bOk = _DoPackaging($tfull, $savePath, $logCtrl, $sBat, $sCfg, $workingDir)
    If FileExists($workingDir) Then DirRemove($workingDir, 1)
    Return $bOk
EndFunc

; Write a text file as UTF-8 (no BOM); line endings are normalized to CRLF
Func _WriteUtf8($sPath, $sText)
    Local $h = FileOpen($sPath, 2 + 256) ; 2=overwrite, 256=UTF-8 (no BOM)
    If $h = -1 Then Return False
    FileWrite($h, StringRegExpReplace($sText, "\r?\n", @CRLF)) ; Normalize LF-only text to CRLF as well
    FileClose($h)
    Return True
EndFunc

Func _DoPackaging($tfull, $savePath, $logCtrl, $sBat, $sCfg, $workingDir)
    _Log($logCtrl, "Starting work..." & @CRLF)

    ; Check that 7z.exe exists
    Local $s7zPath = @ScriptDir & '\bin\7z.exe'
    If Not FileExists($s7zPath) Then
        _Log($logCtrl, "!! Error: Cannot find 7z.exe: " & $s7zPath & @CRLF)
        _Msg(16, "Error", "7z.exe is missing." & @CRLF & "Path: " & $s7zPath)
        Return False
    EndIf

    Local $tDrive, $tDir, $tFName, $tExt
    _PathSplit($tfull, $tDrive, $tDir, $tFName, $tExt)
    Local $tfile = $tFName & $tExt
    Local $exfile = $tFName & ".exe"

    ; Create working folder
    If Not FileExists($workingDir) Then DirCreate($workingDir)
    If Not FileExists($workingDir) Then
        _Log($logCtrl, "!! Error: Failed to create temp folder" & @CRLF)
        Return False
    EndIf

    FileDelete($workingDir & "\*.*")

    ; List of required files to take from the bin folder
    ; (7zS.sfx is used only as the SFX module and is not put inside the 7z)
    Local $aBinFiles[5] = ["7zS.sfx", "aria2c.exe", "aria2.conf.100", "aria2.conf.1G", "getlinkspeed.bat"]

    _Log($logCtrl, "> Preparing required files..." & @CRLF)

    ; Check existence
    For $i = 0 To UBound($aBinFiles) - 1
        If Not FileExists(@ScriptDir & "\bin\" & $aBinFiles[$i]) Then
            _Log($logCtrl, "!! Error: " & $aBinFiles[$i] & " not found." & @CRLF)
            _Msg(16, "Error", $aBinFiles[$i] & " is not in the bin folder.")
            Return False
        EndIf
    Next

    ; Copy and verify the copy
    For $i = 0 To UBound($aBinFiles) - 1
        FileCopy(@ScriptDir & "\bin\" & $aBinFiles[$i], $workingDir & "\" & $aBinFiles[$i], 1)
        If Not FileExists($workingDir & "\" & $aBinFiles[$i]) Then
            _Log($logCtrl, "!! Error: Failed to copy " & $aBinFiles[$i] & @CRLF)
            Return False
        EndIf
    Next

    FileCopy($tfull, $workingDir & "\" & $tfile, 1)
    If Not FileExists($workingDir & "\" & $tfile) Then
        _Log($logCtrl, "!! Error: Failed to copy target file" & @CRLF)
        Return False
    EndIf

    ; Write tfile.txt, start.bat and config.txt all as UTF-8 (no BOM)
    If Not _WriteUtf8($workingDir & "\tfile.txt", $tfile) Then
        _Log($logCtrl, "!! Error: Failed to write tfile.txt" & @CRLF)
        Return False
    EndIf
    If Not _WriteUtf8($workingDir & "\start.bat", $sBat) Then
        _Log($logCtrl, "!! Error: Failed to write start.bat" & @CRLF)
        Return False
    EndIf
    If Not _WriteUtf8($workingDir & "\config.txt", $sCfg) Then
        _Log($logCtrl, "!! Error: Failed to write config.txt" & @CRLF)
        Return False
    EndIf

    _Log($logCtrl, "> Compressing..." & @CRLF)

    ; Build the 7z command: torrent, aria2c, 2 conf files, getlinkspeed.bat, start.bat, tfile.txt
    Local $cmd = '"' & $s7zPath & '" a -t7z "' & $workingDir & '\files.7z"' & _
        ' "' & $workingDir & '\' & $tfile & '"' & _
        ' "' & $workingDir & '\aria2c.exe"' & _
        ' "' & $workingDir & '\aria2.conf.100"' & _
        ' "' & $workingDir & '\aria2.conf.1G"' & _
        ' "' & $workingDir & '\getlinkspeed.bat"' & _
        ' "' & $workingDir & '\start.bat"' & _
        ' "' & $workingDir & '\tfile.txt"'

    _Log($logCtrl, "> Compression command: " & $cmd & @CRLF)

    Local $iPID = Run($cmd, $workingDir, @SW_HIDE, $STDERR_CHILD + $STDOUT_CHILD)
    If $iPID = 0 Then
        _Log($logCtrl, "!! Error: Failed to run 7z" & @CRLF)
        Return False
    EndIf

    Local $sOutput = ""
    Local $sError = ""

    ; Read process output (StdoutRead sets @error when the process ends)
    While 1
        $sOutput &= StdoutRead($iPID)
        If @error Then ExitLoop
        $sError &= StderrRead($iPID)
        Sleep(50)
    WEnd
    $sError &= StderrRead($iPID)

    ; The actual exit code is in @extended of ProcessWaitClose
    ProcessWaitClose($iPID)
    Local $iExit = @extended

    ; Check 7z result
    If $iExit > 1 Or Not FileExists($workingDir & "\files.7z") Then
        _Log($logCtrl, "!! Error: 7z compression failed" & @CRLF)
        _Log($logCtrl, "Exit code: " & $iExit & @CRLF)
        If $sOutput <> "" Then _Log($logCtrl, "Output: " & $sOutput & @CRLF)
        If $sError <> "" Then _Log($logCtrl, "Error: " & $sError & @CRLF)
        _Msg(16, "Compression failed", "7z compression failed." & @CRLF & "Exit code: " & $iExit & @CRLF & @CRLF & "Error: " & $sError)
        Return False
    EndIf

    If $iExit = 1 Then _Log($logCtrl, "> Warning: 7z finished with warnings (exit code 1)." & @CRLF)

    _Log($logCtrl, "> Creating executable..." & @CRLF)

    Local $hOutput = FileOpen($workingDir & "\" & $exfile, 17)
    If $hOutput = -1 Then
        _Log($logCtrl, "!! Error: Failed to create output file" & @CRLF)
        Return False
    EndIf

    FileWrite($hOutput, FileReadBinary($workingDir & "\7zS.sfx"))
    FileWrite($hOutput, FileReadBinary($workingDir & "\config.txt"))
    FileWrite($hOutput, FileReadBinary($workingDir & "\files.7z"))
    FileClose($hOutput)

    ; Check save path
    If Not FileExists($savePath) Then
        DirCreate($savePath)
        If Not FileExists($savePath) Then
            _Log($logCtrl, "!! Error: Failed to create save path: " & $savePath & @CRLF)
            Return False
        EndIf
    EndIf

    FileCopy($workingDir & "\" & $exfile, $savePath & "\" & $exfile, 1)

    If FileExists($savePath & "\" & $exfile) Then
        _Log($logCtrl, "> Done: " & $savePath & "\" & $exfile & @CRLF)
        _Msg(64, "Success", "Packaging complete!" & @CRLF & "Saved to: " & $savePath & "\" & $exfile)
        Return True
    EndIf

    _Log($logCtrl, "!! Error: Failed to copy final file" & @CRLF)
    Return False
EndFunc

Func FileReadBinary($sFilePath)
    Local $hFile = FileOpen($sFilePath, 16)
    Local $data = FileRead($hFile)
    FileClose($hFile)
    Return $data
EndFunc

; --- 5. Reading/saving the start.bat file ---
; Read a UTF-8 text file (with or without BOM); line endings are normalized to CRLF
Func _ReadTextFile($sPath)
    Local $h = FileOpen($sPath, 256) ; 256 = UTF-8
    If $h = -1 Then Return SetError(1, 0, "")
    Local $sText = FileRead($h)
    FileClose($h)
    If StringLeft($sText, 1) = ChrW(0xFEFF) Then $sText = StringTrimLeft($sText, 1)
    Return StringRegExpReplace($sText, "\r?\n", @CRLF)
EndFunc

; Save start.bat (UTF-8 without BOM, CRLF)
Func _SaveStartBat($sText, $logCtrl = 0)
    Local $sDir = StringRegExpReplace($g_sStartBatPath, "\\[^\\]*$", "")
    If Not FileExists($sDir) Then DirCreate($sDir)
    If Not _WriteUtf8($g_sStartBatPath, $sText) Then
        If $logCtrl <> 0 Then GUICtrlSetData($logCtrl, "!! Failed to save start.bat: " & $g_sStartBatPath & @CRLF, 1)
        MsgBox(48, "Warning", "Failed to save start.bat." & @CRLF & $g_sStartBatPath)
        Return False
    EndIf
    $g_sSavedBat = StringRegExpReplace($sText, "\r?\n", @CRLF)
    If $logCtrl <> 0 Then GUICtrlSetData($logCtrl, "> Saved start.bat: " & $g_sStartBatPath & @CRLF, 1)
    Return True
EndFunc

; If changes are unsaved, ask whether to save. Returns True if it is OK to continue
Func _ConfirmSaveBat($ctrl)
    Local $sCur = GUICtrlRead($ctrl)
    If $sCur == $g_sSavedBat Then Return True
    Local $iAns = MsgBox(35, "start.bat changed", "The start.bat content has not been saved." & @CRLF & @CRLF & _
            "Yes: Save and continue" & @CRLF & "No: Continue without saving" & @CRLF & "Cancel: Go back")
    Switch $iAns
        Case 6
            Return _SaveStartBat($sCur)
        Case 7
            Return True
    EndSwitch
    Return False
EndFunc

; --- 6. Command-line (CLI) mode ---
; Usage: eXtorrent.exe "torrent file path" "save path" [/q]
;   - Converts immediately without a window; exit code 0=success / 1=failure
;   - start.bat comes from bin\start.bat; the config.txt content comes from config.ini (or default.ini if absent)
;   - Adding /q also suppresses the result message box

; Log: write to the GUI log box (when present) and the console; keep the last error line
Func _Log($logCtrl, $sMsg)
    If $logCtrl <> 0 Then GUICtrlSetData($logCtrl, $sMsg, 1)
    ConsoleWrite($sMsg)
    If StringLeft($sMsg, 2) = "!!" Then $g_sLastErr = StringStripWS($sMsg, 3)
EndFunc

; Message box: not shown in CLI mode, only written to the console
Func _Msg($iFlag, $sTitle, $sText)
    If $g_bSilent Then
        ConsoleWrite("[" & $sTitle & "] " & $sText & @CRLF)
        Return
    EndIf
    MsgBox($iFlag, $sTitle, $sText)
EndFunc

; Remove quotes + make full path + strip trailing backslash (keep drive root)
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
        $sMsg = "Cannot find the torrent file: " & $sTorrent
    ElseIf StringLower(StringRight($sTorrent, 8)) <> ".torrent" Then
        $sMsg = "Not a .torrent file: " & $sTorrent
    ElseIf StringStripWS($sBat, 8) = "" Then
        $sMsg = "bin\start.bat is missing or empty: " & $g_sStartBatPath
    Else
        If Not FileExists($sSavePath) Then DirCreate($sSavePath)
        If Not FileExists($sSavePath) Then
            $sMsg = "Cannot create the save path: " & $sSavePath
        Else
            $bOk = ProcessPackaging($sTorrent, $sSavePath, 0, $sBat, $sCfg)
            If $bOk Then
                Local $sDrive, $sDir, $sName, $sExt
                _PathSplit($sTorrent, $sDrive, $sDir, $sName, $sExt)
                $sMsg = "Conversion complete: " & $sSavePath & "\" & $sName & ".exe"
            Else
                $sMsg = "Conversion failed: " & $g_sLastErr
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
