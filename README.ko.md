# eXTorrent

**한국어** | [English](README.md)

eXTorrent는 `.torrent` 파일을 **하나의 자체 압축 해제 `.exe` 파일**로 만들어 주는 도구입니다. 생성된 exe를 실행하는 사람은 토렌트 클라이언트가 필요 없습니다. exe가 `aria2c`를 풀고, PC의 네트워크 속도에 맞는 설정을 고른 뒤, 내용을 다운로드하고 스스로 정리합니다.

## 동작 방식

```
 .torrent 파일 ──► eXTorrent (AutoIt GUI / CLI) ──► <토렌트 이름>.exe
                                                        │  (대상 PC에서 실행)
                                                        ▼
                      %temp%에 압축 해제 ─► start.bat ─► aria2c ─► SAVEPATH에 파일 저장
```

생성되는 exe는 `7zS.sfx` + `config.txt` + `files.7z` 구조이며, 7z 안에는 다음 파일이 들어 있습니다.

| 파일 | 역할 |
|---|---|
| `<이름>.torrent` | 다운로드할 토렌트 |
| `tfile.txt` | 토렌트 파일명 (`start.bat`이 읽음) |
| `aria2c.exe` | 다운로드 엔진 (aria2) |
| `aria2.conf.100` | 10/100 Mbps 또는 속도 불명일 때 쓰는 aria2 설정 |
| `aria2.conf.1G` | 1 Gbps 이상일 때 쓰는 aria2 설정 |
| `getlinkspeed.bat` | 활성 NIC의 링크 속도 감지 |
| `start.bat` | 압축 해제 후 실행되는 메인 스크립트 |

## 생성된 exe의 동작 (`start.bat`)

1. 관리자 권한이 아니면 **관리자 권한으로 재실행**합니다 (방화벽 규칙 때문에 필요).
2. `tfile.txt`를 읽고 토렌트 파일 존재를 확인한 뒤 `SAVEPATH`(기본 `C:\Temp`)를 만듭니다.
3. `getlinkspeed.bat`으로 링크 속도를 감지합니다 (WMIC 우선, 실패 시 인라인 PowerShell. 연결된 물리 어댑터 중 가장 빠른 속도 사용).
4. 프로필 선택: **Gbps → `aria2.conf.1G`**, 그 외(100/10 Mbps, 감지 실패) → **`aria2.conf.100`**.
5. `aria2c.exe`에 대한 임시 방화벽 인바운드/아웃바운드 규칙(`eXTorrent_Temp`)을 추가합니다.
6. `aria2c.exe -T <토렌트> --dir=<SAVEPATH> --conf-path=<프로필>`을 실행합니다.
7. 방화벽 규칙을 삭제합니다 (실패했을 때도 삭제).
8. 성공 시: `RunFirst`가 비어 있으면 탐색기에서 다운로드된 항목을 선택해 열고, 지정되어 있으면 해당 스크립트를 후처리로 호출합니다.

`start.bat` 상단에서 수정할 수 있는 변수:

```bat
set "SAVEPATH=C:\Temp"
set "RunFirst="
set "FWRULE=eXTorrent_Temp"
```

### aria2 프로필

| 설정 | `aria2.conf.100` | `aria2.conf.1G` |
|---|---|---|
| `bt-max-peers` | 30 | 100 |
| `max-upload-limit` | 8M | 0 (무제한) |
| `disk-cache` | (기본값) | 128M |
| `bt-tracker-interval` | 60 | 30 |

공통: `file-allocation=none`, `seed-time=0`(완료 후 시딩 안 함), `enable-dht=false`, `enable-peer-exchange=false`, `bt-enable-lpd=false`, `max-overall-download-limit=0`(무제한).

## 빌더 사용법 (`eXTorrent.au3`)

### 필요한 폴더 구조

```
eXTorrent.exe (또는 eXTorrent.au3)
default.ini
config.ini            (설정을 저장하면 생성됨)
bin\
 ├─ 7z.exe
 ├─ 7zS.sfx
 ├─ aria2c.exe
 ├─ aria2.conf.100
 ├─ aria2.conf.1G
 ├─ getlinkspeed.bat
 └─ start.bat
```

### GUI 모드

`eXTorrent.exe`를 실행하거나 `.torrent` 파일을 exe 위로 드래그해서 실행합니다.

1. 대상 `.torrent` 파일을 선택합니다 (드래그 앤 드롭 가능).
2. 생성된 exe를 저장할 폴더를 선택합니다.
3. 필요하면 `bin\start.bat`을 직접 편집하고 **start.bat 저장**을 누릅니다. 저장하지 않은 변경이 있으면 종료 시 저장 여부를 묻습니다.
4. SFX 설정(`config.txt`)을 편집합니다.
5. **실행 파일 생성 시작**을 누르면 선택한 폴더에 `<토렌트 이름>.exe`가 만들어집니다.

보조 버튼: **외부 INI 불러오기**, **현재 설정을 INI에 저장** (config 내용과 저장 경로를 `config.ini`에 저장).

### 명령줄 모드

```
eXTorrent.exe "C:\경로\파일.torrent" "D:\출력폴더" [/q]
```

* 창 없이 바로 변환하며, 종료 코드는 `0`=성공, `1`=실패입니다.
* `bin\start.bat`과 `config.ini`(없으면 `default.ini`)의 설정을 사용합니다.
* `/q`를 붙이면 결과 메시지 창도 표시하지 않습니다.

## 설정 파일

`default.ini` / `config.ini`:

```ini
[config]
content=;!@Install@!UTF-8!\nTitle="eXTorrent"\nInstallPath="%temp%"\nRunProgram="start.bat"\n;!@InstallEnd@!
[savepath]
path=C:\Users\OWNER\Sync\eXTorrent3
```

* `[config] content`는 7-Zip SFX 설정입니다 (`\n`은 줄바꿈). `%temp%`에 풀고 `start.bat`을 실행합니다.
* `[savepath] path`는 빌더의 기본 출력 폴더입니다. **`OWNER`를 본인 경로로 바꾸세요.**

## 참고 및 제한 사항

* Windows 전용입니다. 생성된 exe는 관리자 권한으로 실행되어야 합니다 (UAC 창이 표시됨).
* `start.bat`의 "탐색기에서 선택" 단계는 `tfile.txt`에서 첫 번째 `.` 앞까지를 항목 이름으로 사용하므로, 이름에 점이 여러 개 있는 토렌트는 올바르게 선택되지 않을 수 있습니다.
* 토렌트 이름에 배치 파일 특수문자(`&`, `%`, `^` 등)가 있으면 주의가 필요할 수 있습니다.
* 다운로드 및 배포 권한이 있는 콘텐츠에만 사용하세요.

## 라이선스

이 프로젝트에는 다음 서드파티 바이너리가 포함됩니다.

* `7z.dll`: GNU LGPL (일부 코드는 unRAR 라이선스 제한, BSD 3-clause, BSD 2-clause)
* `7z.exe`, `7zS.sfx`: GNU LGPL
* `aria2c.exe`: GNU GPL

자세한 내용은 `license.txt`를 참고하세요.
