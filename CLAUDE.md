# eXTorrent

.torrent 파일을 **단일 실행파일**로 만들어, 받는 사람이 토렌트 클라이언트 없이 더블클릭만으로 내려받게 하는 도구.
백신 오탐을 최소화하는 것이 핵심 설계 목표다. (예전 AutoIt + 7zS.sfx 방식에서 옮겨 왔다)

## 구성

| 구성 요소 | 언어 | 소스 | 결과물 |
|---|---|---|---|
| 빌더 (GUI + 명령줄) | C# 5 / WPF, .NET Framework 4.x | `src/builder/` | `eXTorrent.exe` |
| 런처 (받는 사람이 실행) | C, MSVC x64 | `src/launcher/` | `bin/launcher.exe` |

- 만든 실행파일 = `bin/launcher.exe` + 덧붙인 데이터(payload) + 푸터. 런처는 모든 토렌트·엔진에서 **같은 바이너리**다.
- 다운로드 엔진은 `aria2c`(aria2/aria2) 또는 `aria2-next`(AnInsomniacy/aria2-next) 중에서 고르며, payload의 `engine.exe`로 들어간다(런처 리소스에는 없음).
  빌더(`Engines.cs`)는 `bin/<엔진>.exe`를 쓰고, 없으면 GitHub 최신 릴리스에서 공식 Windows x64 바이너리를 받아 저장한다.
  aria2c는 `*-win-64bit-build*.zip`에서 꺼내고, aria2-next는 `*-windows-x86_64.exe`를 받아 `checksums.sha256`과 대조한다.
  받는 PC의 런처는 아무것도 내려받지 않는다(다운로더 동작은 오탐 원인).

## 빌드

```
build.bat
```

- vswhere로 VS C++ 도구를 찾아 `rc` + `cl`로 런처를(Windows SDK 필요: 없으면 `'rc' is not recognized`. `winget install Microsoft.WindowsSDK.10.0.26100`), Windows에 기본 포함된 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`로 빌더를 만든다.
- .NET SDK는 없다. **csc는 C# 5까지만** 지원: `$""`, `?.`, `nameof`, `=>` 멤버, 속성 초기화식 사용 금지.
- XAML 컴파일러가 없으므로 XAML은 `/resource:...,EXTorrent.<파일명>`으로 넣고 실행 시 `XamlReader.Load`로 읽는다(`Ui.Load<T>`). `x:Class`와 XAML 이벤트 속성은 쓸 수 없다. `x:Name`으로 찾아(`Ui.Find`) 코드에서 연결한다.
- 새 XAML이나 리소스를 추가하면 `build.bat`의 `/resource` 목록에도 넣어야 한다.

## 실행파일(payload) 형식 — 빌더와 런처가 공유

```
[launcher.exe]
[항목]* = [u16 이름 길이][이름 UTF-8][u32 데이터 길이][데이터]
[푸터]  = "EXTR" [u32 항목 시작 오프셋] [u32 항목 전체 크기] [u32 항목 CRC32]   (리틀 엔디언)
```

항목: `<이름>.torrent`, `aria2.conf.100`, `aria2.conf.1G`, `settings.ini`, `lang.ini`, `engine.exe`.
형식을 바꾸면 `src/builder/Packager.cs`와 `src/launcher/launcher.c`(LoadPayload)를 함께 고친다.
**압축하지 않는다**(고엔트로피 덧붙임 데이터는 패커로 의심받음).

`settings.ini` 키: `Engine`(`aria2c` / `aria2-next`), `SavePath`, `AskSavePath`(1이면 실행 시 폴더 선택 창, SavePath는 시작 위치), `Title`, `RunAfter`(다운로드 폴더 기준 상대 경로 가능, 뒤에 인수 가능: `"경로" 인수` 또는 `경로 인수`. 따옴표가 없으면 공백마다 앞부분이 파일인지 확인), `OpenFolder`.
완료 후 실행은 `ShellExecuteEx`로 하므로 관리자 권한 exe는 Windows가 UAC를 띄운다(런처는 권한을 올리지 않음). .bat 안에서 실행하면 UAC가 뜨지 않아 오류 740으로 실패하므로 exe를 직접 지정하게 안내한다. 종료 코드가 0이 아니면 진행 창에 경고를 표시한다.

## 런처 동작 (`src/launcher/launcher.c`)

- GUI 서브시스템, PerMonitorV2 DPI, 공용 컨트롤 v6, `asInvoker`.
- 작업 스레드가 `%LOCALAPPDATA%\eXTorrent\`에 `<엔진>-<CRC>.exe`를 꺼내(같으면 재사용) `job-<payloadCRC>\`에 torrent와 conf를 쓰고, 엔진을 `CREATE_NO_WINDOW`로 실행해 stdout을 파이프로 읽는다.
- aria2-next는 `.aria2` 파일 대신 상태 폴더에 이어받기 정보를 두므로 `--state-dir=%LOCALAPPDATA%\eXTorrent\aria2-next-state`를 넘긴다. aria2c에는 넘기지 않는다(모르는 옵션은 오류).
  진행 줄 형식은 aria2c와 같다(`SD:` 없음).
- 링크 속도(`GetIfTable2`, 물리 이더넷/무선 중 최대값)가 1Gbps 이상이면 `aria2.conf.1G`, 아니면 `.100`.
- aria2c는 작업 개체(KILL_ON_JOB_CLOSE)에 넣는다. 취소하면 `AttachConsole` + `GenerateConsoleCtrlEvent(CTRL_C)`로 정상 종료시켜 `.aria2` 이어받기 파일을 남기고, 15초 뒤에도 살아 있으면 강제 종료한다.
- 진행 줄 파싱용으로 `--enable-color=false --summary-interval=0`을 넘긴다. 파이프로 받는 출력 형식은 다음과 같다.
  - `[#gid 51MiB/200MiB(25%) CN:1 SD:1 DL:22MiB ETA:6s]`
  - 검사 중: 위 줄 뒤에 ` [Checksum:#gid 1.0GiB/3.0GiB(35%)]`
  - 완료 직후: `[#gid SEED(0.0) CN:0 SD:0]`
- 종료 코드: 0 완료 / 1 오류 / 2 사용자 취소.

## 오탐 방지 원칙 — 되돌리지 말 것

- 배치 파일, PowerShell, wmic, netsh(방화벽), 관리자 권한 상승을 쓰지 않는다. 필요한 일은 Win32 API로 한다.
- `%TEMP%`에 풀지 않는다(`%LOCALAPPDATA%\eXTorrent` 사용).
- UPX 등 패커 금지. 버전 정보 리소스와 매니페스트를 유지한다.
- aria2c / aria2-next는 재빌드하지 않은 공식 릴리스 바이너리를 쓴다.
- 남은 과제: `bin/launcher.exe` 하나에 코드서명. 서명을 유지하려면 단순 덧붙이기 대신 certificate tag 방식이 필요하다.

## 빌더 (`src/builder/`)

| 파일 | 역할 |
|---|---|
| `Program.cs` | 진입점. 인수 2개 이상이면 명령줄 모드: `eXTorrent.exe "토렌트" "저장경로" [/q]` (0 성공 / 1 실패) |
| `MainWindow.xaml/.cs` | 메인 창(만들기 / aria2 설정 / 기록 페이지) |
| `Styles.xaml`, `Theme.cs` | Windows 11 스타일, 라이트/다크(`*Brush` 리소스), 22H2 이상이면 Mica |
| `Ui.cs` | XAML 로더, 대화 상자, 파일 목록 선택, IFileOpenDialog 폴더 선택 |
| `Packager.cs` | 실행파일 생성 |
| `Engines.cs` | 엔진 선택값 정리, `bin/<엔진>.exe` 없으면 GitHub에서 내려받기 |
| `TorrentInfo.cs` | bencode 파싱(이름, 파일 목록, 크기) |
| `Config.cs` | `config.ini` (Win32 프로필 API, 새 파일은 UTF-16 BOM) |
| `History.cs` | `history.txt` (시각 \t 토렌트 \t exe) |
| `Lang.cs` | 언어 파일 |

- 아이콘은 Segoe Fluent Icons / MDL2 글자다. C# 코드에서는 `""`처럼 **이스케이프로 쓴다**(실제 문자로 저장되면 편집기에서 보이지 않음).
- 버튼 포커스 표시는 `FocusRing`(키보드 탐색 시에만)이다. `IsKeyboardFocused` 트리거로 테두리를 바꾸지 말 것.

## 언어 (`lang/*.ini`)

- UTF-8 `키=값`, `\n` = 줄바꿈, `{0}` = 자리 표시자. 섹션은 `[info] Name`, `[builder]`, `[launcher]`.
- 빌더: `[builder]` 값을 `L.<키>` 리소스로 넣는다. XAML은 `{DynamicResource L.키}`, 코드는 `Lang.T("키")` / `Lang.F("키", ...)`.
- 런처: 빌더가 선택된 언어의 `[launcher]` 섹션을 payload의 `lang.ini`로 넣는다. 런처는 `T(L"키", L"한국어 기본값")`과 `Fmt()`({0} 치환, printf 형식 금지)를 쓴다.
- 문자열을 추가하면 **ko.ini와 en.ini 모두에** 키를 넣는다. 런처는 코드에 한국어 기본값도 함께 둔다.
- `lang/ko.ini`는 빌더 exe에 내장되어 대체값이 된다(키 누락, 폴더 없음).
- 새 언어는 `lang/xx.ini`만 추가하면 선택 버튼에 자동으로 나타난다.

## 설정·데이터 파일 (빌더 폴더)

- `config.ini`: `[savepath] path`, `[download] SavePath/AskSavePath/Title/RunAfter/OpenFolder/Language/Engine`, `[ui] Theme/Language`
- `bin/aria2.conf.100`, `bin/aria2.conf.1G`: 빌더 "aria2 설정" 페이지에서 편집한다. 재실행 시 이어받기를 위해 `check-integrity=true`를 유지한다.
- 배포 시 `eXTorrent.exe` 옆에 `bin/`, `lang/`이 있어야 한다. 엔진 exe는 없어도 된다(처음 만들 때 내려받음).

## 테스트 방법

- 실제 토렌트 대신 **로컬 미니 트래커(파이썬 HTTP, 항상 127.0.0.1:<시더 포트> 반환) + aria2c 시더**로 끝까지 받는다. 외부 통신이 없다.
  6881은 qBittorrent 등이 이미 쓰고 있을 수 있으므로 시더는 16881 같은 다른 포트로 띄운다.
  배포용 `bin/aria2.conf.*`는 DHT/PEX가 켜져 있다(의도된 설정). 외부 통신 없이 테스트하려면 빌더를 별도 폴더에 복사하고 그 폴더의 conf에서만 `enable-dht=false`, `enable-peer-exchange=false`로 바꾼다.
  테스트 데이터는 스크래치 폴더에만 만든다. 드라이브 문자를 `subst`로 쓸 때는 먼저 비어 있는지 확인한다(X:, Y:, Z:는 네트워크 드라이브).
- GUI는 UI Automation(AutomationId = `x:Name`)으로 조작하고 `CopyFromScreen`으로 캡처해 확인한다.
- 주의: PowerShell에서 `FindWindow($null, ...)`의 `$null`은 빈 문자열로 넘어간다(`[NullString]::Value` 사용). 소유된 대화 상자는 UIA 루트 자식으로 잡히지 않는다.
- 같은 이름의 파일이 이전 테스트에서 남아 있으면 aria2가 거부한다(코드 13 / info hash mismatch). 테스트 폴더를 비우고 시작한다.

## 레거시 (사용하지 않음)

`eXTorrent.au3`, `eXTorrent_x64.exe`, `eXTorrent_x86.exe`, `default.ini`, `extorrent.7z`, `bin/7z*`, `bin/7zS*.sfx`, `bin/start.bat`, `bin/getlinkspeed.bat`
