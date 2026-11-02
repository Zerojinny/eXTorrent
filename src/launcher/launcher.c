// eXTorrent 런처
// 자기 자신(exe) 끝에 붙은 데이터(torrent, aria2.conf.*, settings.ini, lang.ini, engine.exe)를 읽어
// 함께 들어 있는 다운로드 엔진(aria2c 또는 aria2-next)으로 다운로드하고, 진행 상황을 창으로 보여준다.
// 화면 문자열은 lang.ini(빌더의 lang\*.ini [launcher] 섹션)에서 읽고, 없으면 코드의 한국어를 쓴다.
// 배치 파일, PowerShell, wmic, 관리자 권한, 방화벽 변경을 쓰지 않는다.
//
// 덧붙은 데이터 형식 (리틀 엔디언):
//   [항목]* = [u16 이름 길이][이름 UTF-8][u32 데이터 길이][데이터]
//   [푸터]  = "EXTR" [u32 항목 시작 오프셋] [u32 항목 전체 크기] [u32 항목 CRC32]
//
// 종료 코드: 0 = 완료, 1 = 오류, 2 = 사용자가 취소

#define WIN32_LEAN_AND_MEAN
#define _WIN32_WINNT 0x0601
#include <winsock2.h>
#include <ws2ipdef.h>
#include <windows.h>
#include <iphlpapi.h>
#include <commctrl.h>
#include <shlobj.h>
#include <shellapi.h>
#include <stdarg.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>
#include "resource.h"

#pragma comment(lib, "iphlpapi.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "uuid.lib")
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "gdi32.lib")
#pragma comment(lib, "comctl32.lib")

#define PATH_CAP    1024
#define MAX_ENTRIES 16

#pragma pack(push, 1)
typedef struct {
    char     magic[4];
    uint32_t offset;
    uint32_t size;
    uint32_t crc;
} Footer;
#pragma pack(pop)

typedef struct {
    wchar_t        name[MAX_PATH];
    const uint8_t *data;
    uint32_t       size;
} Entry;

static Entry    g_entry[MAX_ENTRIES];
static int      g_entryCount;
static uint8_t *g_payload;
static uint32_t g_payloadCrc;
static wchar_t  g_settings[8192]; // settings.ini 내용

static const wchar_t *T(const wchar_t *key, const wchar_t *def);
static void Fmt(wchar_t *out, size_t cap, const wchar_t *tmpl, int argc, ...);

// --- 0. 진행 상태 (작업 스레드 → 화면) ---

enum { PH_PREPARE, PH_CONNECT, PH_CHECK, PH_DOWNLOAD, PH_RUNAFTER, PH_DONE, PH_FAIL, PH_CANCEL };
enum { RES_OK = 0, RES_FAIL = 1, RES_CANCEL = 2 };

typedef struct {
    int     phase;
    double  done, total; // 바이트
    double  speed;       // 바이트/초
    long    eta;         // 초, 모르면 -1
    int     conns;
    wchar_t message[600];
} Status;

#define WM_APP_STATUS (WM_APP + 1)
#define WM_APP_LOG    (WM_APP + 2)
#define WM_APP_DONE   (WM_APP + 3)
#define TIMER_KILL    1

enum { ID_TITLE = 100, ID_PATH, ID_BAR, ID_STATUS, ID_DETAIL, ID_MORE, ID_ACTION, ID_CLOSE, ID_LOG };

static CRITICAL_SECTION g_lock;
static Status           g_st;
static volatile LONG    g_cancel;
static HANDLE           g_ariaProc;
static DWORD            g_ariaPid;

// 작업 설정 (작업 스레드 시작 전에 정해짐)
static const Entry *g_torrent;
static wchar_t      g_title[256], g_savePath[PATH_CAP], g_runAfter[PATH_CAP], g_name[MAX_PATH];
static const wchar_t *g_engine = L"aria2c"; // settings.ini Engine: aria2c / aria2-next
static BOOL         g_openFolder;
static double       g_totalSize;
static int          g_fileCount;

// 화면
static HINSTANCE       g_inst;
static HWND            g_wnd, g_lblTitle, g_lblPath, g_bar, g_lblStatus, g_lblDetail, g_btnMore, g_btnAction, g_btnClose, g_txtLog;
static HFONT           g_font, g_titleFont;
static UINT            g_dpi = 96;
static BOOL            g_expanded, g_running = TRUE, g_closeAfter;
static int             g_result = RES_FAIL;
static COLORREF        g_statusColor;
static ITaskbarList3  *g_taskbar;

// --- 1. 공통 도우미 ---

static uint32_t Crc32(const void *buf, size_t len)
{
    static uint32_t table[256];
    const uint8_t *p = buf;
    uint32_t c = 0xFFFFFFFF;
    if (!table[1]) {
        for (uint32_t i = 0; i < 256; i++) {
            uint32_t v = i;
            for (int k = 0; k < 8; k++) v = (v & 1) ? 0xEDB88320 ^ (v >> 1) : v >> 1;
            table[i] = v;
        }
    }
    while (len--) c = table[(c ^ *p++) & 0xFF] ^ (c >> 8);
    return c ^ 0xFFFFFFFF;
}

static int Utf8ToWide(const char *s, int len, wchar_t *out, int cap)
{
    int n = len > 0 ? MultiByteToWideChar(CP_UTF8, 0, s, len, out, cap - 1) : 0;
    out[n] = 0;
    return n;
}

static void JoinPath(wchar_t *out, size_t cap, const wchar_t *dir, const wchar_t *name)
{
    size_t n = wcslen(dir);
    swprintf_s(out, cap, (n && dir[n - 1] == L'\\') ? L"%s%s" : L"%s\\%s", dir, name);
}

static BOOL IsDir(const wchar_t *path)
{
    DWORD a = GetFileAttributesW(path);
    return a != INVALID_FILE_ATTRIBUTES && (a & FILE_ATTRIBUTE_DIRECTORY);
}

static BOOL WriteAll(const wchar_t *path, const void *data, DWORD size)
{
    DWORD written;
    BOOL ok;
    HANDLE f = CreateFileW(path, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (f == INVALID_HANDLE_VALUE) return FALSE;
    ok = WriteFile(f, data, size, &written, NULL) && written == size;
    CloseHandle(f);
    return ok;
}

// 파일 내용이 data와 똑같은지 확인
static BOOL SameFile(const wchar_t *path, const void *data, DWORD size)
{
    WIN32_FILE_ATTRIBUTE_DATA a;
    HANDLE f;
    uint8_t *buf;
    DWORD rd;
    BOOL same = FALSE;
    if (!GetFileAttributesExW(path, GetFileExInfoStandard, &a) || a.nFileSizeHigh || a.nFileSizeLow != size) return FALSE;
    f = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, 0, NULL);
    if (f == INVALID_HANDLE_VALUE) return FALSE;
    buf = HeapAlloc(GetProcessHeap(), 0, size ? size : 1);
    if (buf && ReadFile(f, buf, size, &rd, NULL) && rd == size) same = memcmp(buf, data, size) == 0;
    if (buf) HeapFree(GetProcessHeap(), 0, buf);
    CloseHandle(f);
    return same;
}

// 환경 변수 확장 + 전체 경로화 + 끝의 역슬래시 제거 (드라이브 루트는 유지)
static void ExpandPath(const wchar_t *in, wchar_t *out, DWORD cap)
{
    wchar_t tmp[PATH_CAP];
    size_t n;
    if (!ExpandEnvironmentStringsW(in, tmp, PATH_CAP)) wcscpy_s(tmp, PATH_CAP, in);
    if (!GetFullPathNameW(tmp, cap, out, NULL)) wcscpy_s(out, cap, tmp);
    n = wcslen(out);
    if (n > 3 && out[n - 1] == L'\\') out[n - 1] = 0;
}

// CommandLineToArgvW 규칙에 맞게 인수 하나를 따옴표로 감싸 붙인다
static void AppendArg(wchar_t *cmd, size_t cap, const wchar_t *arg)
{
    size_t n = wcslen(cmd);
    const wchar_t *p = arg;
#define PUT(c) do { if (n + 2 < cap) cmd[n++] = (c); } while (0)
    if (n) PUT(L' ');
    PUT(L'"');
    for (;;) {
        size_t bs = 0;
        while (*p == L'\\') { bs++; p++; }
        if (*p == 0) {
            while (bs--) { PUT(L'\\'); PUT(L'\\'); }
            break;
        }
        if (*p == L'"') {
            while (bs--) { PUT(L'\\'); PUT(L'\\'); }
            PUT(L'\\');
        } else {
            while (bs--) PUT(L'\\');
        }
        PUT(*p++);
    }
    PUT(L'"');
#undef PUT
    cmd[n] = 0;
}

static void FormatSize(double b, wchar_t *out, size_t cap)
{
    static const wchar_t *units[] = { L"B", L"KB", L"MB", L"GB", L"TB" };
    int u = 0;
    while (b >= 1024 && u < 4) { b /= 1024; u++; }
    swprintf_s(out, cap, (u == 0 || b >= 100) ? L"%.0f %s" : L"%.1f %s", b, units[u]);
}

static void FormatEta(long s, wchar_t *out, size_t cap)
{
    wchar_t a[16], b[16];
    if (s >= 3600) {
        swprintf_s(a, 16, L"%ld", s / 3600);
        swprintf_s(b, 16, L"%ld", s % 3600 / 60);
        Fmt(out, cap, T(L"eta_h", L"{0}시간 {1}분"), 2, a, b);
    } else if (s >= 60) {
        swprintf_s(a, 16, L"%ld", s / 60);
        swprintf_s(b, 16, L"%ld", s % 60);
        Fmt(out, cap, T(L"eta_m", L"{0}분 {1}초"), 2, a, b);
    } else {
        swprintf_s(a, 16, L"%ld", s);
        Fmt(out, cap, T(L"eta_s", L"{0}초"), 1, a);
    }
}

// --- 2. 덧붙은 데이터 읽기 ---

static BOOL LoadPayload(wchar_t *err, size_t errCap)
{
    wchar_t self[PATH_CAP];
    Footer ft = { 0 };
    LARGE_INTEGER size, pos;
    DWORD rd;
    uint32_t at = 0;
    BOOL ok = FALSE;
    HANDLE f;

    GetModuleFileNameW(NULL, self, PATH_CAP);
    f = CreateFileW(self, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, 0, NULL);
    // 언어 정보는 이 데이터 안에 있으므로, 여기서 나는 오류는 한국어와 영어를 함께 보여준다
    if (f == INVALID_HANDLE_VALUE) {
        Fmt(err, errCap, L"실행파일을 읽을 수 없습니다.\nCannot read the executable.\n\n{0}", 1, self);
        return FALSE;
    }
    if (GetFileSizeEx(f, &size) && size.QuadPart >= (LONGLONG)sizeof ft) {
        pos.QuadPart = size.QuadPart - sizeof ft;
        if (SetFilePointerEx(f, pos, NULL, FILE_BEGIN) && ReadFile(f, &ft, sizeof ft, &rd, NULL) && rd == sizeof ft &&
            memcmp(ft.magic, "EXTR", 4) == 0 && (LONGLONG)ft.offset + ft.size + (LONGLONG)sizeof ft == size.QuadPart) {
            g_payload = HeapAlloc(GetProcessHeap(), 0, ft.size ? ft.size : 1);
            pos.QuadPart = ft.offset;
            ok = g_payload && SetFilePointerEx(f, pos, NULL, FILE_BEGIN) &&
                 ReadFile(f, g_payload, ft.size, &rd, NULL) && rd == ft.size;
        }
    }
    CloseHandle(f);
    if (!ok) {
        wcscpy_s(err, errCap, L"다운로드 정보가 들어 있지 않습니다.\neXTorrent 빌더로 만든 파일인지 확인해주세요.\n\n"
                              L"No download information found.\nMake sure this file was created with the eXTorrent builder.");
        return FALSE;
    }
    if (Crc32(g_payload, ft.size) != ft.crc) {
        wcscpy_s(err, errCap, L"파일이 손상되었습니다 (CRC 불일치). 파일을 다시 받아주세요.\n\n"
                              L"The file is corrupted (CRC mismatch). Please download it again.");
        return FALSE;
    }
    g_payloadCrc = ft.crc;

    while (at < ft.size) {
        uint16_t nameLen;
        uint32_t len;
        Entry *e;
        if (g_entryCount == MAX_ENTRIES || ft.size - at < 2) goto corrupt;
        memcpy(&nameLen, g_payload + at, 2);
        at += 2;
        if (nameLen == 0 || ft.size - at < (uint32_t)nameLen + 4) goto corrupt;
        e = &g_entry[g_entryCount++];
        if (!Utf8ToWide((const char *)g_payload + at, nameLen, e->name, MAX_PATH)) goto corrupt;
        at += nameLen;
        memcpy(&len, g_payload + at, 4);
        at += 4;
        if (ft.size - at < len) goto corrupt;
        e->data = g_payload + at;
        e->size = len;
        at += len;
        // 파일 이름만 허용 (경로 구분자, 상위 폴더 금지)
        if (wcspbrk(e->name, L"\\/:") || wcscmp(e->name, L".") == 0 || wcscmp(e->name, L"..") == 0) goto corrupt;
    }
    return TRUE;

corrupt:
    wcscpy_s(err, errCap, L"다운로드 정보 형식이 올바르지 않습니다.\n\nThe download information is malformed.");
    return FALSE;
}

static const Entry *FindEntry(const wchar_t *name)
{
    for (int i = 0; i < g_entryCount; i++)
        if (_wcsicmp(g_entry[i].name, name) == 0) return &g_entry[i];
    return NULL;
}

static const Entry *FindTorrent(void)
{
    for (int i = 0; i < g_entryCount; i++) {
        size_t n = wcslen(g_entry[i].name);
        if (n > 8 && _wcsicmp(g_entry[i].name + n - 8, L".torrent") == 0) return &g_entry[i];
    }
    return NULL;
}

// settings.ini에서 "키=값" 읽기 (섹션은 하나뿐이므로 무시)
static void GetSetting(const wchar_t *key, const wchar_t *def, wchar_t *out, size_t cap)
{
    const wchar_t *p = g_settings;
    size_t keyLen = wcslen(key);
    while (*p) {
        const wchar_t *eol = wcschr(p, L'\n');
        size_t lineLen = eol ? (size_t)(eol - p) : wcslen(p);
        if (lineLen > keyLen && _wcsnicmp(p, key, keyLen) == 0 && p[keyLen] == L'=') {
            size_t n = lineLen - keyLen - 1;
            if (n && p[keyLen + n] == L'\r') n--;
            if (n >= cap) n = cap - 1;
            wmemcpy(out, p + keyLen + 1, n);
            out[n] = 0;
            return;
        }
        if (!eol) break;
        p = eol + 1;
    }
    wcscpy_s(out, cap, def);
}

// --- 2-1. 화면 문자열 (lang.ini) ---

#define MAX_LANG 256

static wchar_t        g_langBuf[32768];
static const wchar_t *g_langKey[MAX_LANG], *g_langVal[MAX_LANG];
static int            g_langCount;

// lang.ini의 "키=값" 줄을 읽어 둔다. 값의 \n은 줄바꿈으로 바꾼다
static void LoadLang(void)
{
    const Entry *e = FindEntry(L"lang.ini");
    wchar_t *p, *line, *eq, *r, *w;
    if (!e || !Utf8ToWide((const char *)e->data, (int)e->size, g_langBuf, _countof(g_langBuf))) return;
    for (p = g_langBuf; *p && g_langCount < MAX_LANG; ) {
        line = p;
        while (*p && *p != L'\n') p++;
        if (*p) *p++ = 0;
        if ((r = wcschr(line, L'\r')) != NULL) *r = 0;
        if (line[0] == L';' || line[0] == L'[' || (eq = wcschr(line, L'=')) == NULL || eq == line) continue;
        *eq = 0;
        for (r = w = eq + 1; *r; r++) {
            if (r[0] == L'\\' && r[1] == L'n') { *w++ = L'\n'; r++; }
            else *w++ = *r;
        }
        *w = 0;
        g_langKey[g_langCount] = line;
        g_langVal[g_langCount++] = eq + 1;
    }
}

static const wchar_t *T(const wchar_t *key, const wchar_t *def)
{
    for (int i = 0; i < g_langCount; i++)
        if (wcscmp(g_langKey[i], key) == 0) return g_langVal[i];
    return def;
}

// {0}..{9}를 문자열 인수로 바꾼다 (언어 파일 문자열을 printf 형식으로 쓰지 않기 위해)
static void Fmt(wchar_t *out, size_t cap, const wchar_t *tmpl, int argc, ...)
{
    const wchar_t *args[10] = { 0 };
    size_t n = 0;
    va_list ap;
    va_start(ap, argc);
    for (int i = 0; i < argc && i < 10; i++) args[i] = va_arg(ap, const wchar_t *);
    va_end(ap);
    for (const wchar_t *p = tmpl; *p && n + 1 < cap; p++) {
        if (p[0] == L'{' && p[1] >= L'0' && p[1] <= L'9' && p[2] == L'}' && p[1] - L'0' < argc) {
            const wchar_t *a = args[p[1] - L'0'];
            while (a && *a && n + 1 < cap) out[n++] = *a++;
            p += 2;
        } else {
            out[n++] = *p;
        }
    }
    out[n] = 0;
}

// --- 3. torrent 정보 (이름, 전체 크기, 파일 수) ---

static const char *BStr(const char *p, const char *e, const char **s, size_t *n)
{
    size_t len = 0;
    if (p >= e || *p < '0' || *p > '9') return NULL;
    while (p < e && *p >= '0' && *p <= '9') {
        len = len * 10 + (*p++ - '0');
        if (len > (size_t)(e - p)) return NULL;
    }
    if (p >= e || *p != ':') return NULL;
    p++;
    if (len > (size_t)(e - p)) return NULL;
    *s = p;
    *n = len;
    return p + len;
}

static const char *BSkip(const char *p, const char *e, int depth)
{
    const char *s;
    size_t n;
    if (p >= e || depth > 64) return NULL;
    if (*p == 'i') {
        while (p < e && *p != 'e') p++;
        return p < e ? p + 1 : NULL;
    }
    if (*p == 'l' || *p == 'd') {
        p++;
        while (p && p < e && *p != 'e') p = BSkip(p, e, depth + 1);
        return (p && p < e) ? p + 1 : NULL;
    }
    return BStr(p, e, &s, &n);
}

// 사전(d...e)에서 key를 찾아 값의 시작 위치를 돌려준다
static const char *BFind(const char *p, const char *e, const char *key)
{
    const char *k;
    size_t kn, keyLen = strlen(key);
    if (!p || p >= e || *p != 'd') return NULL;
    p++;
    while (p < e && *p != 'e') {
        p = BStr(p, e, &k, &kn);
        if (!p) return NULL;
        if (kn == keyLen && memcmp(k, key, kn) == 0) return p;
        p = BSkip(p, e, 1);
        if (!p) return NULL;
    }
    return NULL;
}

static double BInt(const char *p, const char *e)
{
    double v = 0;
    if (!p || p >= e || *p != 'i') return 0;
    for (p++; p < e && *p >= '0' && *p <= '9'; p++) v = v * 10 + (*p - '0');
    return v;
}

static void ReadTorrentInfo(const Entry *t)
{
    const char *b = (const char *)t->data, *end = b + t->size, *info, *v, *s;
    size_t n;
    g_name[0] = 0;
    g_totalSize = 0;
    g_fileCount = 0;
    info = BFind(b, end, "info");
    v = BFind(info, end, "name.utf-8");
    if (!v) v = BFind(info, end, "name");
    if (v && BStr(v, end, &s, &n) && n > 0 && n < 4096) {
        Utf8ToWide(s, (int)n, g_name, MAX_PATH);
        if (wcspbrk(g_name, L"\\/:*?\"<>|")) g_name[0] = 0;
    }
    v = BFind(info, end, "files");
    if (v && *v == 'l') {
        for (v++; v && v < end && *v != 'e'; v = BSkip(v, end, 1)) {
            g_totalSize += BInt(BFind(v, end, "length"), end);
            g_fileCount++;
        }
    } else {
        g_totalSize = BInt(BFind(info, end, "length"), end);
        g_fileCount = 1;
    }
}

// --- 4. aria2c 준비 / 링크 속도 / 완료 후 처리 ---

// 덧붙은 engine.exe를 baseDir에 <엔진>-<CRC>.exe로 꺼낸다. 같은 내용이 이미 있으면 재사용
static BOOL ExtractEngine(const Entry *engine, const wchar_t *baseDir, wchar_t *out, size_t cap)
{
    const void *data = engine->data;
    DWORD size = engine->size;
    wchar_t tmp[PATH_CAP];
    // 내용이 바뀌면 이름도 바뀌므로, 다른 버전이 실행 중이어도 충돌하지 않는다
    swprintf_s(out, cap, L"%s\\%s-%08X.exe", baseDir, g_engine, Crc32(data, size));
    if (SameFile(out, data, size)) return TRUE;
    swprintf_s(tmp, PATH_CAP, L"%s.%lu.tmp", out, GetCurrentProcessId());
    if (!WriteAll(tmp, data, size)) return FALSE;
    if (!MoveFileExW(tmp, out, MOVEFILE_REPLACE_EXISTING)) {
        DeleteFileW(tmp);
        return SameFile(out, data, size); // 다른 인스턴스가 먼저 꺼낸 경우
    }
    return TRUE;
}

// 연결된 물리 이더넷/무선 어댑터 중 가장 빠른 수신 속도 (bps, 실패 시 0)
static ULONG64 LinkSpeed(void)
{
    PMIB_IF_TABLE2 t = NULL;
    ULONG64 best = 0;
    if (GetIfTable2(&t) != NO_ERROR) return 0;
    for (ULONG i = 0; i < t->NumEntries; i++) {
        MIB_IF_ROW2 *row = &t->Table[i];
        if (!row->InterfaceAndOperStatusFlags.HardwareInterface || row->OperStatus != IfOperStatusUp) continue;
        if (row->Type != IF_TYPE_ETHERNET_CSMACD && row->Type != IF_TYPE_IEEE80211) continue;
        if (row->ReceiveLinkSpeed != (ULONG64)-1 && row->ReceiveLinkSpeed > best) best = row->ReceiveLinkSpeed;
    }
    FreeMibTable(t);
    return best;
}

// 다운로드 폴더 선택 창. initial이 있으면 그 폴더에서 시작한다. 취소하면 FALSE
static BOOL PickFolder(const wchar_t *title, const wchar_t *initial, wchar_t *out, size_t cap)
{
    IFileOpenDialog *dlg = NULL;
    IShellItem *item = NULL;
    FILEOPENDIALOGOPTIONS opt;
    wchar_t *path = NULL;
    BOOL ok = FALSE;

    if (FAILED(CoCreateInstance(&CLSID_FileOpenDialog, NULL, CLSCTX_INPROC_SERVER, &IID_IFileOpenDialog, (void **)&dlg)))
        return FALSE;
    if (SUCCEEDED(dlg->lpVtbl->GetOptions(dlg, &opt)))
        dlg->lpVtbl->SetOptions(dlg, opt | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
    dlg->lpVtbl->SetTitle(dlg, title);
    if (initial[0] && IsDir(initial) && SUCCEEDED(SHCreateItemFromParsingName(initial, NULL, &IID_IShellItem, (void **)&item))) {
        dlg->lpVtbl->SetFolder(dlg, item);
        item->lpVtbl->Release(item);
        item = NULL;
    }
    if (SUCCEEDED(dlg->lpVtbl->Show(dlg, NULL)) && SUCCEEDED(dlg->lpVtbl->GetResult(dlg, &item))) {
        if (SUCCEEDED(item->lpVtbl->GetDisplayName(item, SIGDN_FILESYSPATH, &path))) {
            wcscpy_s(out, cap, path);
            CoTaskMemFree(path);
            ok = TRUE;
        }
        item->lpVtbl->Release(item);
    }
    dlg->lpVtbl->Release(dlg);
    return ok;
}

static void OpenResult(void)
{
    wchar_t full[PATH_CAP];
    PIDLIST_ABSOLUTE pidl;
    if (g_name[0]) {
        JoinPath(full, PATH_CAP, g_savePath, g_name);
        if (GetFileAttributesW(full) != INVALID_FILE_ATTRIBUTES && (pidl = ILCreateFromPathW(full)) != NULL) {
            HRESULT hr = SHOpenFolderAndSelectItems(pidl, 0, NULL, 0);
            ILFree(pidl);
            if (SUCCEEDED(hr)) return;
        }
    }
    ShellExecuteW(NULL, L"open", g_savePath, NULL, NULL, SW_SHOWNORMAL);
}

static const wchar_t *AriaError(DWORD code)
{
    switch (code) {
    case 2:  return T(L"aria_2", L"시간이 초과되었습니다.");
    case 3:  return T(L"aria_3", L"리소스를 찾을 수 없습니다.");
    case 6:  return T(L"aria_6", L"네트워크 문제가 발생했습니다.");
    case 7:  return T(L"aria_7", L"완료되지 않은 다운로드가 남아 있습니다.");
    case 9:  return T(L"aria_9", L"디스크 공간이 부족합니다.");
    case 12: return T(L"aria_12", L"같은 토렌트를 이미 받고 있습니다.");
    case 13: return T(L"aria_13", L"같은 이름의 파일이 이미 있습니다.");
    case 15:
    case 16: return T(L"aria_15", L"파일을 열거나 만들 수 없습니다.");
    case 17: return T(L"aria_17", L"파일을 쓰는 중 오류가 발생했습니다.");
    case 18: return T(L"aria_18", L"폴더를 만들 수 없습니다.");
    case 25:
    case 26: return T(L"aria_25", L"토렌트 파일이 손상되었습니다.");
    case 32: return T(L"aria_32", L"받은 데이터의 검증에 실패했습니다.");
    default: return T(L"aria_default", L"다운로드 중 오류가 발생했습니다.");
    }
}

// --- 5. 작업 스레드 → 화면 전달 ---

static void Log(const wchar_t *fmt, ...)
{
    wchar_t buf[2048], *copy;
    size_t n;
    va_list ap;
    va_start(ap, fmt);
    _vsnwprintf_s(buf, _countof(buf), _TRUNCATE, fmt, ap);
    va_end(ap);
    n = wcslen(buf) + 1;
    copy = HeapAlloc(GetProcessHeap(), 0, n * sizeof(wchar_t));
    if (!copy) return;
    wmemcpy(copy, buf, n);
    if (!PostMessageW(g_wnd, WM_APP_LOG, 0, (LPARAM)copy)) HeapFree(GetProcessHeap(), 0, copy);
}

static void PostStatus(void)
{
    PostMessageW(g_wnd, WM_APP_STATUS, 0, 0);
}

static void SetPhase(int phase, const wchar_t *message)
{
    EnterCriticalSection(&g_lock);
    g_st.phase = phase;
    wcscpy_s(g_st.message, _countof(g_st.message), message ? message : L"");
    LeaveCriticalSection(&g_lock);
    PostStatus();
}

static void SetFail(const wchar_t *msg)
{
    wchar_t buf[700];
    Fmt(buf, _countof(buf), T(L"log_error", L"[오류] {0}"), 1, msg);
    Log(L"%s", buf);
    SetPhase(PH_FAIL, msg);
}

// 언어 문자열 key의 {0}에 arg를 넣어 실패로 표시한다
static void FailWith(const wchar_t *key, const wchar_t *def, const wchar_t *arg)
{
    wchar_t buf[600];
    Fmt(buf, _countof(buf), T(key, def), 1, arg);
    SetFail(buf);
}

static void FailWithError(const wchar_t *key, const wchar_t *def, DWORD err)
{
    wchar_t num[16];
    swprintf_s(num, 16, L"%lu", err);
    FailWith(key, def, num);
}

// --- 6. aria2c 출력 해석 ---
// 진행 줄 예:
//   [#0cdc31 51MiB/200MiB(25%) CN:1 SD:1 DL:22MiB ETA:6s]
//   [#87c4bb 0B/3.0GiB(0%) CN:0 SD:0 DL:0B] [Checksum:#87c4bb 1.0GiB/3.0GiB(35%)]
//   [#87c4bb SEED(0.0) CN:0 SD:0]

static double ParseSize(const char **pp)
{
    const char *p = *pp;
    char *end;
    double v = strtod(p, &end), mul = 1;
    if (end == p) return -1;
    p = end;
    if (p[0] && p[1] == 'i') {
        switch (p[0]) {
        case 'K': mul = 1024.0; break;
        case 'M': mul = 1024.0 * 1024; break;
        case 'G': mul = 1024.0 * 1024 * 1024; break;
        case 'T': mul = 1024.0 * 1024 * 1024 * 1024; break;
        }
        p += 2;
    }
    if (*p == 'B') p++;
    *pp = p;
    return v * mul;
}

static long ParseEta(const char *p)
{
    long total = 0, v = 0;
    for (; *p && *p != ' ' && *p != ']'; p++) {
        if (*p >= '0' && *p <= '9') { v = v * 10 + (*p - '0'); continue; }
        if (*p == 'h') total += v * 3600;
        else if (*p == 'm') total += v * 60;
        else if (*p == 's') total += v;
        v = 0;
    }
    return total;
}

static void ParseReadout(const char *s)
{
    const char *chk = strstr(s, "[Checksum:"), *p;
    double done = -1, total = -1;

    EnterCriticalSection(&g_lock);
    if (chk) {
        p = strchr(chk, ' ');
        if (p) {
            p++;
            done = ParseSize(&p);
            if (*p == '/') { p++; total = ParseSize(&p); }
        }
        g_st.phase = PH_CHECK;
        g_st.speed = 0;
        g_st.eta = -1;
    } else if (strstr(s, "SEED(")) {
        if (g_st.total > 0) g_st.done = g_st.total;
        g_st.phase = PH_DOWNLOAD;
        g_st.speed = 0;
        g_st.eta = -1;
    } else if ((p = strchr(s, ' ')) != NULL) {
        p++;
        done = ParseSize(&p);
        if (*p == '/') { p++; total = ParseSize(&p); }
        p = strstr(s, " CN:");
        g_st.conns = p ? atoi(p + 4) : 0;
        p = strstr(s, " DL:");
        if (p) { p += 4; g_st.speed = ParseSize(&p); }
        p = strstr(s, " ETA:");
        g_st.eta = p ? ParseEta(p + 5) : -1;
        g_st.phase = PH_DOWNLOAD;
    }
    if (done >= 0) g_st.done = done;
    if (total > 0) g_st.total = total;
    LeaveCriticalSection(&g_lock);
    PostStatus();
}

static void HandleLine(char *line, size_t n)
{
    wchar_t w[2048];
    while (n && (line[n - 1] == ' ' || line[n - 1] == '\t')) line[--n] = 0;
    if (!n) return;
    if (line[0] == '[' && line[1] == '#') {
        ParseReadout(line);
        return;
    }
    Utf8ToWide(line, (int)n, w, _countof(w));
    Log(L"%s", w);
}

static void ReadOutput(HANDLE rd)
{
    char buf[4096], line[2048];
    size_t n = 0;
    DWORD got;
    while (ReadFile(rd, buf, sizeof buf, &got, NULL) && got) {
        for (DWORD i = 0; i < got; i++) {
            char c = buf[i];
            if (c == '\r' || c == '\n') {
                if (n) { line[n] = 0; HandleLine(line, n); n = 0; }
            } else if (n < sizeof line - 1) {
                line[n++] = c;
            }
        }
    }
    if (n) { line[n] = 0; HandleLine(line, n); }
}

// --- 7. 작업 스레드 ---

// 완료 후 실행 (상대 경로는 다운로드 폴더 기준). 끝날 때까지 기다린다. 문제가 있으면 note에 기록
static void RunAfter(wchar_t *note, size_t noteCap)
{
    wchar_t exp[PATH_CAP], joined[PATH_CAP], path[PATH_CAP], dir[PATH_CAP], msg[PATH_CAP + 100], num[16], *slash;
    SHELLEXECUTEINFOW sei = { sizeof sei };
    if (!ExpandEnvironmentStringsW(g_runAfter, exp, PATH_CAP)) wcscpy_s(exp, PATH_CAP, g_runAfter);
    if ((exp[0] && exp[1] == L':') || (exp[0] == L'\\' && exp[1] == L'\\'))
        wcscpy_s(joined, PATH_CAP, exp);
    else
        JoinPath(joined, PATH_CAP, g_savePath, exp);
    if (!GetFullPathNameW(joined, PATH_CAP, path, NULL)) wcscpy_s(path, PATH_CAP, joined);

    if (GetFileAttributesW(path) == INVALID_FILE_ATTRIBUTES) {
        Fmt(note, noteCap, T(L"runafter_missing", L"완료 후 실행할 파일이 없어 건너뛰었습니다: {0}"), 1, path);
        Fmt(msg, _countof(msg), T(L"log_warning", L"[경고] {0}"), 1, note);
        Log(L"%s", msg);
        return;
    }
    wcscpy_s(dir, PATH_CAP, path);
    slash = wcsrchr(dir, L'\\');
    if (slash) *slash = 0;

    SetPhase(PH_RUNAFTER, wcsrchr(path, L'\\') ? wcsrchr(path, L'\\') + 1 : path);
    Fmt(msg, _countof(msg), T(L"runafter_log", L"완료 후 실행: {0}"), 1, path);
    Log(L"%s", msg);
    sei.fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_NOASYNC;
    sei.hwnd = g_wnd;
    sei.lpVerb = L"open";
    sei.lpFile = path;
    sei.lpDirectory = dir;
    sei.nShow = SW_SHOWNORMAL;
    if (!ShellExecuteExW(&sei)) {
        swprintf_s(num, 16, L"%lu", GetLastError());
        Fmt(note, noteCap, T(L"runafter_fail", L"완료 후 실행에 실패했습니다 (오류 {0}): {1}"), 2, num, path);
        Fmt(msg, _countof(msg), T(L"log_warning", L"[경고] {0}"), 1, note);
        Log(L"%s", msg);
        return;
    }
    if (sei.hProcess) {
        WaitForSingleObject(sei.hProcess, INFINITE);
        CloseHandle(sei.hProcess);
    }
}

static DWORD WINAPI Worker(LPVOID arg)
{
    wchar_t tmp[PATH_CAP], baseDir[PATH_CAP], jobDir[PATH_CAP], aria[PATH_CAP], torrentPath[PATH_CAP], confPath[PATH_CAP];
    wchar_t confName[32], speedText[32], note[600] = L"", cmd[8192] = L"";
    const Entry *conf, *engine;
    SECURITY_ATTRIBUTES sa = { sizeof sa, NULL, TRUE };
    STARTUPINFOW si = { sizeof si };
    PROCESS_INFORMATION pi = { 0 };
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION limit = { 0 };
    HANDLE rd = NULL, wr = NULL, job = NULL;
    ULONG64 speed;
    DWORD code = 1;
    int result = RES_FAIL;
    (void)arg;

    CoInitializeEx(NULL, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE);
    SetPhase(PH_PREPARE, NULL);

    if (!IsDir(g_savePath)) SHCreateDirectoryExW(NULL, g_savePath, NULL);
    if (!IsDir(g_savePath)) {
        FailWith(L"err_mkdir", L"다운로드 폴더를 만들 수 없습니다: {0}", g_savePath);
        goto done;
    }

    // 작업 폴더: %LOCALAPPDATA%\eXTorrent (임시 폴더 대신)
    if (FAILED(SHGetFolderPathW(NULL, CSIDL_LOCAL_APPDATA | CSIDL_FLAG_CREATE, NULL, 0, tmp))) {
        SetFail(T(L"err_localappdata", L"LocalAppData 폴더를 찾을 수 없습니다."));
        goto done;
    }
    JoinPath(baseDir, PATH_CAP, tmp, L"eXTorrent");
    CreateDirectoryW(baseDir, NULL);
    swprintf_s(jobDir, PATH_CAP, L"%s\\job-%08X", baseDir, g_payloadCrc);
    CreateDirectoryW(jobDir, NULL);

    engine = FindEntry(L"engine.exe");
    if (!engine) {
        SetFail(T(L"err_no_engine", L"다운로드 엔진이 들어 있지 않습니다."));
        goto done;
    }
    if (!ExtractEngine(engine, baseDir, aria, PATH_CAP)) {
        FailWith(L"err_aria_extract", L"다운로드 엔진을 준비하지 못했습니다: {0}", baseDir);
        goto done;
    }
    Fmt(note, _countof(note), T(L"log_engine", L"다운로드 엔진: {0}"), 1, g_engine);
    Log(L"%s", note);

    // 링크 속도: 1Gbps 이상이면 1G 설정, 그 외(100/10Mbps, 감지 실패)는 100 설정
    speed = LinkSpeed();
    swprintf_s(confName, _countof(confName), L"aria2.conf.%s", speed >= 1000000000ULL ? L"1G" : L"100");
    if (speed == 0)
        wcscpy_s(speedText, _countof(speedText), T(L"speed_unknown", L"감지 실패"));
    else if (speed >= 1000000000ULL)
        swprintf_s(speedText, _countof(speedText), speed % 1000000000ULL ? L"%.1f Gbps" : L"%.0f Gbps", speed / 1e9);
    else
        swprintf_s(speedText, _countof(speedText), L"%llu Mbps", speed / 1000000ULL);
    Fmt(note, _countof(note), T(L"log_speed", L"링크 속도: {0} → {1}"), 2, speedText, confName);
    Log(L"%s", note);
    note[0] = 0;

    conf = FindEntry(confName);
    if (!conf) {
        FailWith(L"err_conf", L"설정 파일이 없습니다: {0}", confName);
        goto done;
    }
    JoinPath(torrentPath, PATH_CAP, jobDir, g_torrent->name);
    JoinPath(confPath, PATH_CAP, jobDir, L"aria2.conf");
    if (!WriteAll(torrentPath, g_torrent->data, g_torrent->size) || !WriteAll(confPath, conf->data, conf->size)) {
        FailWith(L"err_jobfiles", L"작업 파일을 쓸 수 없습니다: {0}", jobDir);
        goto done;
    }

    AppendArg(cmd, _countof(cmd), aria);
    AppendArg(cmd, _countof(cmd), L"-T");
    AppendArg(cmd, _countof(cmd), torrentPath);
    swprintf_s(tmp, PATH_CAP, L"--dir=%s", g_savePath);
    AppendArg(cmd, _countof(cmd), tmp);
    swprintf_s(tmp, PATH_CAP, L"--conf-path=%s", confPath);
    AppendArg(cmd, _countof(cmd), tmp);
    // 진행 줄을 해석하기 쉽도록 색 코드와 주기적 요약을 끈다
    AppendArg(cmd, _countof(cmd), L"--enable-color=false");
    AppendArg(cmd, _countof(cmd), L"--summary-interval=0");
    // aria2-next는 이어받기 정보를 .aria2 파일 대신 상태 폴더에 둔다 (기본값 %LOCALAPPDATA%\aria2-next 대신 작업 폴더 아래)
    if (wcscmp(g_engine, L"aria2-next") == 0) {
        swprintf_s(tmp, PATH_CAP, L"--state-dir=%s\\aria2-next-state", baseDir);
        AppendArg(cmd, _countof(cmd), tmp);
    }

    // aria2c 출력은 파이프로 받는다. 작업 개체에 넣어 런처가 끝나면 aria2c도 함께 끝나게 한다
    if (!CreatePipe(&rd, &wr, &sa, 0)) {
        FailWithError(L"err_pipe", L"파이프를 만들 수 없습니다 (오류 {0})", GetLastError());
        goto cleanup;
    }
    SetHandleInformation(rd, HANDLE_FLAG_INHERIT, 0);
    si.dwFlags = STARTF_USESTDHANDLES;
    si.hStdOutput = wr;
    si.hStdError = wr;
    job = CreateJobObjectW(NULL, NULL);
    if (job) {
        limit.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        SetInformationJobObject(job, JobObjectExtendedLimitInformation, &limit, sizeof limit);
    }
    if (g_cancel) goto cleanup;
    // CREATE_NO_WINDOW: 창 없는 콘솔을 준다 (취소할 때 Ctrl+C를 보내기 위해 필요)
    if (!CreateProcessW(aria, cmd, NULL, NULL, TRUE, CREATE_SUSPENDED | CREATE_NO_WINDOW, NULL, jobDir, &si, &pi)) {
        FailWithError(L"err_spawn", L"다운로드 엔진을 실행하지 못했습니다 (오류 {0})", GetLastError());
        goto cleanup;
    }
    CloseHandle(wr);
    wr = NULL;
    if (job) AssignProcessToJobObject(job, pi.hProcess);
    EnterCriticalSection(&g_lock);
    g_ariaProc = pi.hProcess;
    g_ariaPid = pi.dwProcessId;
    LeaveCriticalSection(&g_lock);
    ResumeThread(pi.hThread);
    SetPhase(PH_CONNECT, NULL);

    ReadOutput(rd);
    WaitForSingleObject(pi.hProcess, INFINITE);
    GetExitCodeProcess(pi.hProcess, &code);
    EnterCriticalSection(&g_lock);
    g_ariaProc = NULL;
    g_ariaPid = 0;
    LeaveCriticalSection(&g_lock);
    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);

cleanup:
    DeleteFileW(torrentPath);
    DeleteFileW(confPath);
    RemoveDirectoryW(jobDir);
    if (g_cancel) {
        Log(L"%s", T(L"log_cancelled", L"사용자가 다운로드를 중단했습니다."));
        result = RES_CANCEL;
        goto done;
    }
    if (pi.hProcess == NULL) goto done; // 실행 전에 실패 (SetFail에서 이미 기록)
    if (code != 0) {
        swprintf_s(tmp, PATH_CAP, L"%lu", code);
        Fmt(note, _countof(note), T(L"err_exit", L"{0} (종료 코드 {1})"), 2, AriaError(code), tmp);
        SetFail(note);
        goto done;
    }

    EnterCriticalSection(&g_lock);
    if (g_st.total > 0) g_st.done = g_st.total;
    LeaveCriticalSection(&g_lock);

    // 완료 후 처리: 실행할 파일이 지정되어 있으면 실행, 아니면 폴더 열기
    if (g_runAfter[0])
        RunAfter(note, _countof(note));
    else if (g_openFolder)
        OpenResult();
    SetPhase(PH_DONE, note);
    result = RES_OK;

done:
    if (wr) CloseHandle(wr);
    if (rd) CloseHandle(rd);
    if (job) CloseHandle(job);
    CoUninitialize();
    PostMessageW(g_wnd, WM_APP_DONE, result, 0);
    return 0;
}

// aria2c의 (창 없는) 콘솔에 Ctrl+C를 보내 이어받기 정보를 저장하고 끝내게 한다.
// 15초 안에 끝나지 않으면 TIMER_KILL에서 강제 종료
static void StopAria(void)
{
    DWORD pid;
    EnterCriticalSection(&g_lock);
    pid = g_ariaPid;
    LeaveCriticalSection(&g_lock);
    if (!pid) return;
    if (AttachConsole(pid)) {
        SetConsoleCtrlHandler(NULL, TRUE); // 런처 자신은 Ctrl+C를 무시
        GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0);
        FreeConsole();
    }
    SetTimer(g_wnd, TIMER_KILL, 15000, NULL);
}

// --- 8. 화면 ---

typedef UINT (WINAPI *GetDpiForWindowFn)(HWND);

static int S(int v)
{
    return MulDiv(v, (int)g_dpi, 96);
}

static UINT DpiOf(HWND h)
{
    static GetDpiForWindowFn fn;
    static BOOL looked;
    HDC dc;
    UINT dpi;
    if (!looked) {
        fn = (GetDpiForWindowFn)GetProcAddress(GetModuleHandleW(L"user32.dll"), "GetDpiForWindow");
        looked = TRUE;
    }
    if (fn) return fn(h);
    dc = GetDC(NULL);
    dpi = (UINT)GetDeviceCaps(dc, LOGPIXELSY);
    ReleaseDC(NULL, dc);
    return dpi;
}

static void CreateFonts(void)
{
    NONCLIENTMETRICSW ncm = { sizeof ncm };
    LOGFONTW lf;
    if (g_font) DeleteObject(g_font);
    if (g_titleFont) DeleteObject(g_titleFont);
    SystemParametersInfoW(SPI_GETNONCLIENTMETRICS, sizeof ncm, &ncm, 0);
    lf = ncm.lfMessageFont;
    lf.lfHeight = -MulDiv(9, (int)g_dpi, 72);
    lf.lfQuality = CLEARTYPE_QUALITY;
    g_font = CreateFontIndirectW(&lf);
    lf.lfHeight = -MulDiv(13, (int)g_dpi, 72);
    lf.lfWeight = FW_SEMIBOLD;
    g_titleFont = CreateFontIndirectW(&lf);
}

static void ApplyFonts(void)
{
    HWND all[] = { g_lblPath, g_lblStatus, g_lblDetail, g_btnMore, g_btnAction, g_btnClose, g_txtLog };
    for (int i = 0; i < (int)_countof(all); i++) SendMessageW(all[i], WM_SETFONT, (WPARAM)g_font, TRUE);
    SendMessageW(g_lblTitle, WM_SETFONT, (WPARAM)g_titleFont, TRUE);
}

#define CLIENT_W     480
#define CLIENT_H     214
#define LOG_H        170

// 창 크기를 DPI와 펼침 상태에 맞추고 컨트롤을 배치한다
static void Layout(BOOL resizeWindow)
{
    int w = S(CLIENT_W), m = S(20), inner = w - 2 * m;
    int h = S(CLIENT_H) + (g_expanded ? S(LOG_H + 10) : 0);
    int bw = S(100), bh = S(30), by = S(166);
    if (resizeWindow) {
        RECT r = { 0, 0, w, h }, wr;
        AdjustWindowRectEx(&r, (DWORD)GetWindowLongW(g_wnd, GWL_STYLE), FALSE, (DWORD)GetWindowLongW(g_wnd, GWL_EXSTYLE));
        GetWindowRect(g_wnd, &wr);
        SetWindowPos(g_wnd, NULL, wr.left, wr.top, r.right - r.left, r.bottom - r.top, SWP_NOZORDER | SWP_NOACTIVATE);
    }
    MoveWindow(g_lblTitle, m, S(16), inner, S(30), TRUE);
    MoveWindow(g_lblPath, m, S(48), inner, S(20), TRUE);
    MoveWindow(g_bar, m, S(80), inner, S(16), TRUE);
    MoveWindow(g_lblStatus, m, S(106), inner, S(22), TRUE);
    MoveWindow(g_lblDetail, m, S(130), inner, S(20), TRUE);
    MoveWindow(g_btnMore, m, by, bw, bh, TRUE);
    MoveWindow(g_btnAction, w - m - 2 * bw - S(8), by, bw, bh, TRUE);
    MoveWindow(g_btnClose, w - m - bw, by, bw, bh, TRUE);
    MoveWindow(g_txtLog, m, S(CLIENT_H), inner, S(LOG_H - 10), TRUE);
    ShowWindow(g_txtLog, g_expanded ? SW_SHOW : SW_HIDE);
}

static void SetExpanded(BOOL on)
{
    g_expanded = on;
    SetWindowTextW(g_btnMore, on ? T(L"btn_less", L"간단히 ▴") : T(L"btn_more", L"자세히 ▾"));
    Layout(TRUE);
}

static void SetMarquee(BOOL on)
{
    LONG st = GetWindowLongW(g_bar, GWL_STYLE);
    if (on && !(st & PBS_MARQUEE)) {
        SetWindowLongW(g_bar, GWL_STYLE, st | PBS_MARQUEE);
        SendMessageW(g_bar, PBM_SETMARQUEE, TRUE, 30);
    } else if (!on && (st & PBS_MARQUEE)) {
        SendMessageW(g_bar, PBM_SETMARQUEE, FALSE, 0);
        SetWindowLongW(g_bar, GWL_STYLE, st & ~PBS_MARQUEE);
    }
}

static void SetTaskbar(TBPFLAG state, double done, double total)
{
    if (!g_taskbar) return;
    g_taskbar->lpVtbl->SetProgressState(g_taskbar, g_wnd, state);
    if (state == TBPF_NORMAL || state == TBPF_ERROR || state == TBPF_PAUSED)
        g_taskbar->lpVtbl->SetProgressValue(g_taskbar, g_wnd, (ULONGLONG)(total > 0 ? done / total * 1000 : 1000), 1000);
}

static void UpdateUi(void)
{
    Status st;
    wchar_t status[700], detail[700], part[200], a[32], b[32], c[32], d[48], n[16], p[16];
    double pct;
    EnterCriticalSection(&g_lock);
    st = g_st;
    LeaveCriticalSection(&g_lock);
    if (st.total <= 0) st.total = g_totalSize;
    pct = st.total > 0 ? st.done / st.total * 100 : 0;
    if (pct > 100) pct = 100;
    FormatSize(st.done, a, _countof(a));
    FormatSize(st.total, b, _countof(b));
    swprintf_s(n, _countof(n), L"%d", g_fileCount);
    detail[0] = 0;
    g_statusColor = GetSysColor(COLOR_WINDOWTEXT);

    switch (st.phase) {
    case PH_PREPARE:
    case PH_CONNECT:
        wcscpy_s(status, _countof(status), st.phase == PH_PREPARE ? T(L"st_prepare", L"준비 중...") : T(L"st_connect", L"연결하는 중..."));
        Fmt(detail, _countof(detail), T(L"detail_size", L"{0} · 파일 {1}개"), 2, b, n);
        SetMarquee(TRUE);
        SetTaskbar(TBPF_INDETERMINATE, 0, 0);
        break;
    case PH_CHECK:
        swprintf_s(p, _countof(p), L"%.0f", pct);
        Fmt(status, _countof(status), T(L"st_check", L"기존 파일 검사 중 · {0}%"), 1, p);
        Fmt(detail, _countof(detail), T(L"detail_check", L"{0} / {1} 확인"), 2, a, b);
        SetMarquee(FALSE);
        SendMessageW(g_bar, PBM_SETPOS, (WPARAM)(pct * 10), 0);
        SetTaskbar(TBPF_NORMAL, st.done, st.total);
        break;
    case PH_DOWNLOAD:
        swprintf_s(p, _countof(p), L"%.1f", pct);
        if (st.speed <= 0 && st.done < st.total)
            Fmt(status, _countof(status), st.conns ? T(L"st_connected", L"연결됨 · {0}%") : T(L"st_peers", L"피어를 찾는 중 · {0}%"), 1, p);
        else
            Fmt(status, _countof(status), T(L"st_download", L"다운로드 중 · {0}%"), 1, p);
        FormatSize(st.speed, c, _countof(c));
        Fmt(detail, _countof(detail), T(L"detail_dl", L"{0} / {1} · {2}/s"), 3, a, b, c);
        if (st.eta < 0 && st.speed > 0 && st.total > st.done) st.eta = (long)((st.total - st.done) / st.speed);
        if (st.eta > 0) {
            FormatEta(st.eta, d, _countof(d));
            Fmt(part, _countof(part), T(L"detail_eta", L" · 남은 시간 {0}"), 1, d);
            wcscat_s(detail, _countof(detail), part);
        }
        swprintf_s(d, _countof(d), L"%d", st.conns);
        Fmt(part, _countof(part), T(L"detail_conns", L" · 연결 {0}"), 1, d);
        wcscat_s(detail, _countof(detail), part);
        SetMarquee(FALSE);
        SendMessageW(g_bar, PBM_SETPOS, (WPARAM)(pct * 10), 0);
        SetTaskbar(TBPF_NORMAL, st.done, st.total);
        break;
    case PH_RUNAFTER:
        Fmt(status, _countof(status), T(L"st_runafter", L"완료 후 실행 중: {0}"), 1, st.message);
        wcscpy_s(detail, _countof(detail), T(L"detail_runafter", L"실행한 프로그램이 끝나면 완료됩니다."));
        SetMarquee(FALSE);
        SendMessageW(g_bar, PBM_SETPOS, 1000, 0);
        SetTaskbar(TBPF_INDETERMINATE, 0, 0);
        break;
    case PH_DONE:
        wcscpy_s(status, _countof(status), T(L"st_done", L"✔ 다운로드 완료"));
        if (st.message[0]) wcscpy_s(detail, _countof(detail), st.message);
        else Fmt(detail, _countof(detail), T(L"detail_done", L"{0} · 파일 {1}개를 받았습니다."), 2, b, n);
        g_statusColor = RGB(0x10, 0x7C, 0x10);
        SetMarquee(FALSE);
        SendMessageW(g_bar, PBM_SETPOS, 1000, 0);
        SetTaskbar(TBPF_NOPROGRESS, 0, 0);
        break;
    case PH_FAIL:
        wcscpy_s(status, _countof(status), T(L"st_fail", L"✖ 다운로드 실패"));
        wcscpy_s(detail, _countof(detail), st.message);
        g_statusColor = RGB(0xC4, 0x2B, 0x1C);
        SetMarquee(FALSE);
        SendMessageW(g_bar, PBM_SETSTATE, PBST_ERROR, 0);
        SetTaskbar(TBPF_ERROR, st.done, st.total);
        break;
    case PH_CANCEL:
        wcscpy_s(status, _countof(status), T(L"st_cancel", L"중단했습니다"));
        wcscpy_s(detail, _countof(detail), T(L"detail_cancel", L"다시 실행하면 받은 부분부터 이어서 받습니다."));
        SetMarquee(FALSE);
        SendMessageW(g_bar, PBM_SETSTATE, PBST_PAUSED, 0);
        SetTaskbar(TBPF_PAUSED, st.done, st.total);
        break;
    default:
        return;
    }
    SetWindowTextW(g_lblStatus, status);
    SetWindowTextW(g_lblDetail, detail);
}

static void AppendLog(const wchar_t *text)
{
    int len = GetWindowTextLengthW(g_txtLog);
    if (len > 200000) { // 너무 길어지면 앞부분을 버린다
        SendMessageW(g_txtLog, EM_SETSEL, 0, len / 2);
        SendMessageW(g_txtLog, EM_REPLACESEL, FALSE, (LPARAM)L"");
        len = GetWindowTextLengthW(g_txtLog);
    }
    SendMessageW(g_txtLog, EM_SETSEL, len, len);
    SendMessageW(g_txtLog, EM_REPLACESEL, FALSE, (LPARAM)text);
    SendMessageW(g_txtLog, EM_REPLACESEL, FALSE, (LPARAM)L"\r\n");
}

static void RequestCancel(void)
{
    InterlockedExchange(&g_cancel, 1);
    SetWindowTextW(g_lblStatus, T(L"st_stopping", L"중단하는 중..."));
    SetWindowTextW(g_lblDetail, T(L"detail_stopping", L"받은 부분을 저장하고 있습니다."));
    EnableWindow(g_btnClose, FALSE);
    StopAria();
}

// 다운로드는 끝나고 완료 후 실행 프로그램을 기다리는 중이면 그냥 닫아도 된다
static BOOL InRunAfter(void)
{
    BOOL r;
    EnterCriticalSection(&g_lock);
    r = g_st.phase == PH_RUNAFTER;
    LeaveCriticalSection(&g_lock);
    if (r) g_result = RES_OK;
    return r;
}

static BOOL ConfirmCancel(void)
{
    return MessageBoxW(g_wnd, T(L"confirm_cancel", L"다운로드를 중단할까요?\n다시 실행하면 받은 부분부터 이어서 받습니다."),
                       g_title, MB_YESNO | MB_ICONQUESTION | MB_DEFBUTTON2) == IDYES;
}

static HWND MakeCtrl(const wchar_t *cls, const wchar_t *text, DWORD style, int id)
{
    return CreateWindowExW(0, cls, text, WS_CHILD | WS_VISIBLE | style, 0, 0, 10, 10, g_wnd, (HMENU)(INT_PTR)id, g_inst, NULL);
}

static LRESULT CALLBACK WndProc(HWND h, UINT msg, WPARAM wp, LPARAM lp)
{
    switch (msg) {
    case WM_CTLCOLORSTATIC: {
        HDC dc = (HDC)wp;
        HWND c = (HWND)lp;
        SetBkColor(dc, GetSysColor(COLOR_WINDOW));
        if (c == g_lblStatus) SetTextColor(dc, g_statusColor);
        else if (c == g_lblPath || c == g_lblDetail) SetTextColor(dc, RGB(0x60, 0x60, 0x60));
        else SetTextColor(dc, GetSysColor(COLOR_WINDOWTEXT));
        return (LRESULT)GetSysColorBrush(COLOR_WINDOW);
    }
    case WM_COMMAND:
        switch (LOWORD(wp)) {
        case ID_MORE:
            SetExpanded(!g_expanded);
            return 0;
        case ID_ACTION:
            OpenResult();
            return 0;
        case ID_CLOSE:
            if (!g_running || InRunAfter()) DestroyWindow(h);
            else if (ConfirmCancel()) RequestCancel();
            return 0;
        }
        break;
    case WM_APP_STATUS:
        if (g_running && !g_cancel) UpdateUi();
        return 0;
    case WM_APP_LOG:
        AppendLog((const wchar_t *)lp);
        HeapFree(GetProcessHeap(), 0, (void *)lp);
        return 0;
    case WM_APP_DONE:
        g_running = FALSE;
        g_result = (int)wp;
        KillTimer(h, TIMER_KILL);
        if (g_result == RES_CANCEL) {
            EnterCriticalSection(&g_lock);
            g_st.phase = PH_CANCEL;
            LeaveCriticalSection(&g_lock);
            if (g_closeAfter) {
                DestroyWindow(h);
                return 0;
            }
        }
        UpdateUi();
        SetWindowTextW(g_btnClose, T(L"btn_close", L"닫기"));
        EnableWindow(g_btnClose, TRUE);
        ShowWindow(g_btnAction, g_result == RES_OK ? SW_SHOW : SW_HIDE);
        if (g_result == RES_FAIL && !g_expanded) SetExpanded(TRUE);
        if (GetForegroundWindow() != h) {
            FLASHWINFO fi = { sizeof fi, h, FLASHW_ALL | FLASHW_TIMERNOFG, 0, 0 };
            FlashWindowEx(&fi);
        }
        return 0;
    case WM_TIMER:
        if (wp == TIMER_KILL) {
            KillTimer(h, TIMER_KILL);
            EnterCriticalSection(&g_lock);
            if (g_ariaProc) TerminateProcess(g_ariaProc, 1);
            LeaveCriticalSection(&g_lock);
        }
        return 0;
    case WM_DPICHANGED: {
        RECT *r = (RECT *)lp;
        g_dpi = HIWORD(wp);
        CreateFonts();
        ApplyFonts();
        SetWindowPos(h, NULL, r->left, r->top, r->right - r->left, r->bottom - r->top, SWP_NOZORDER | SWP_NOACTIVATE);
        Layout(TRUE);
        return 0;
    }
    case WM_CLOSE:
        if (!g_running || InRunAfter()) {
            DestroyWindow(h);
        } else if (!g_cancel && ConfirmCancel()) {
            g_closeAfter = TRUE;
            RequestCancel();
        }
        return 0;
    case WM_DESTROY:
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(h, msg, wp, lp);
}

static void CreateMainWindow(void)
{
    WNDCLASSEXW wc = { sizeof wc };
    INITCOMMONCONTROLSEX icc = { sizeof icc, ICC_PROGRESS_CLASS | ICC_STANDARD_CLASSES };
    wchar_t text[PATH_CAP + 64], size[32], count[16];
    RECT work, wr;

    InitCommonControlsEx(&icc);
    wc.lpfnWndProc = WndProc;
    wc.hInstance = g_inst;
    wc.hIcon = LoadIconW(g_inst, MAKEINTRESOURCEW(IDI_APP));
    wc.hIconSm = (HICON)LoadImageW(g_inst, MAKEINTRESOURCEW(IDI_APP), IMAGE_ICON, GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON), 0);
    wc.hCursor = LoadCursorW(NULL, (LPCWSTR)IDC_ARROW);
    wc.hbrBackground = (HBRUSH)(COLOR_WINDOW + 1);
    wc.lpszClassName = L"eXTorrentLauncher";
    RegisterClassExW(&wc);

    g_wnd = CreateWindowExW(0, wc.lpszClassName, g_title, WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX | WS_CLIPCHILDREN,
                            CW_USEDEFAULT, CW_USEDEFAULT, 100, 100, NULL, NULL, g_inst, NULL);
    g_dpi = DpiOf(g_wnd);

    g_lblTitle = MakeCtrl(L"STATIC", g_name[0] ? g_name : g_torrent->name, SS_LEFT | SS_ENDELLIPSIS | SS_NOPREFIX, ID_TITLE);
    Fmt(text, _countof(text), T(L"save_to", L"저장 위치: {0}"), 1, g_savePath);
    g_lblPath = MakeCtrl(L"STATIC", text, SS_LEFT | SS_PATHELLIPSIS | SS_NOPREFIX, ID_PATH);
    g_bar = MakeCtrl(PROGRESS_CLASSW, L"", 0, ID_BAR);
    SendMessageW(g_bar, PBM_SETRANGE32, 0, 1000);
    g_lblStatus = MakeCtrl(L"STATIC", T(L"st_prepare", L"준비 중..."), SS_LEFT | SS_ENDELLIPSIS | SS_NOPREFIX, ID_STATUS);
    FormatSize(g_totalSize, size, _countof(size));
    swprintf_s(count, _countof(count), L"%d", g_fileCount);
    Fmt(text, _countof(text), T(L"detail_size", L"{0} · 파일 {1}개"), 2, size, count);
    g_lblDetail = MakeCtrl(L"STATIC", text, SS_LEFT | SS_ENDELLIPSIS | SS_NOPREFIX, ID_DETAIL);
    g_btnMore = MakeCtrl(L"BUTTON", T(L"btn_more", L"자세히 ▾"), BS_PUSHBUTTON | WS_TABSTOP, ID_MORE);
    g_btnAction = MakeCtrl(L"BUTTON", T(L"btn_open", L"폴더 열기"), BS_PUSHBUTTON | WS_TABSTOP, ID_ACTION);
    ShowWindow(g_btnAction, SW_HIDE);
    g_btnClose = MakeCtrl(L"BUTTON", T(L"btn_cancel", L"취소"), BS_PUSHBUTTON | WS_TABSTOP, ID_CLOSE);
    g_txtLog = CreateWindowExW(WS_EX_CLIENTEDGE, L"EDIT", L"", WS_CHILD | WS_VSCROLL | ES_MULTILINE | ES_READONLY | ES_AUTOVSCROLL,
                               0, 0, 10, 10, g_wnd, (HMENU)ID_LOG, g_inst, NULL);
    SendMessageW(g_txtLog, EM_SETLIMITTEXT, 0, 0);

    CreateFonts();
    ApplyFonts();
    Layout(TRUE);

    // 작업 영역 가운데에 놓는다
    SystemParametersInfoW(SPI_GETWORKAREA, 0, &work, 0);
    GetWindowRect(g_wnd, &wr);
    SetWindowPos(g_wnd, NULL, work.left + (work.right - work.left - (wr.right - wr.left)) / 2,
                 work.top + (work.bottom - work.top - (wr.bottom - wr.top)) / 3, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
    ShowWindow(g_wnd, SW_SHOWNORMAL);
    UpdateWindow(g_wnd);

    if (SUCCEEDED(CoCreateInstance(&CLSID_TaskbarList, NULL, CLSCTX_INPROC_SERVER, &IID_ITaskbarList3, (void **)&g_taskbar)))
        g_taskbar->lpVtbl->HrInit(g_taskbar);
}

// --- 9. 진입점 ---

int WINAPI wWinMain(HINSTANCE inst, HINSTANCE prev, LPWSTR cmdLine, int show)
{
    wchar_t err[PATH_CAP + 512], tmp[PATH_CAP], openFolder[8], askSavePath[8], engine[16];
    const Entry *settings;
    HANDLE worker;
    MSG msg;
    (void)prev; (void)cmdLine; (void)show;

    g_inst = inst;
    InitializeCriticalSection(&g_lock);
    CoInitializeEx(NULL, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE);

    if (!LoadPayload(err, _countof(err))) {
        MessageBoxW(NULL, err, L"eXTorrent", MB_OK | MB_ICONERROR);
        return RES_FAIL;
    }
    LoadLang();
    settings = FindEntry(L"settings.ini");
    if (settings) Utf8ToWide((const char *)settings->data, (int)settings->size, g_settings, _countof(g_settings));
    GetSetting(L"Title", L"eXTorrent", g_title, _countof(g_title));
    if (!g_title[0]) wcscpy_s(g_title, _countof(g_title), L"eXTorrent");
    GetSetting(L"SavePath", L"C:\\Temp", tmp, PATH_CAP);
    ExpandPath(tmp, g_savePath, PATH_CAP);
    GetSetting(L"RunAfter", L"", g_runAfter, PATH_CAP);
    GetSetting(L"OpenFolder", L"1", openFolder, _countof(openFolder));
    GetSetting(L"AskSavePath", L"0", askSavePath, _countof(askSavePath));
    GetSetting(L"Engine", L"aria2c", engine, _countof(engine));
    g_openFolder = openFolder[0] == L'1';
    if (_wcsicmp(engine, L"aria2-next") == 0) g_engine = L"aria2-next"; // 파일 이름에 쓰므로 알려진 값만 받는다

    g_torrent = FindTorrent();
    if (!g_torrent) {
        MessageBoxW(NULL, T(L"err_no_torrent", L"토렌트 데이터가 없습니다."), g_title, MB_OK | MB_ICONERROR);
        return RES_FAIL;
    }
    ReadTorrentInfo(g_torrent);

    // 실행할 때 선택: SavePath는 선택 창의 시작 위치로 쓴다
    if (askSavePath[0] == L'1') {
        Fmt(tmp, PATH_CAP, T(L"pick_title", L"{0} - 다운로드할 폴더 선택"), 1, g_title);
        if (!PickFolder(tmp, g_savePath, g_savePath, PATH_CAP)) return RES_CANCEL;
    }

    CreateMainWindow();
    g_st.eta = -1;
    worker = CreateThread(NULL, 0, Worker, NULL, 0, NULL);
    if (!worker) {
        MessageBoxW(g_wnd, T(L"err_thread", L"작업을 시작하지 못했습니다."), g_title, MB_OK | MB_ICONERROR);
        return RES_FAIL;
    }

    while (GetMessageW(&msg, NULL, 0, 0) > 0) {
        if (IsDialogMessageW(g_wnd, &msg)) continue;
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }

    // 완료 후 실행을 기다리던 작업 스레드는 기다리지 않는다 (실행한 프로그램은 그대로 계속된다)
    if (g_taskbar) g_taskbar->lpVtbl->Release(g_taskbar);
    CoUninitialize();
    return g_result;
}
