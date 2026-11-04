// config.ini (빌더 설정)
// AutoIt 버전이 만든 ANSI 파일도 그대로 읽을 수 있도록 Win32 프로필 API를 쓴다.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace EXTorrent
{
    class Config
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder ret, int size, string file);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool WritePrivateProfileString(string section, string key, string value, string file);

        public string OutputDir = "";
        public DownloadOptions Download = new DownloadOptions();

        // section 중 하나라도 들어 있었는지 (외부 INI 불러오기 확인용)
        public bool Found;

        public static Config Load(string iniPath, string defaultOutputDir)
        {
            var c = new Config();
            var d = c.Download;
            c.OutputDir = Read(iniPath, "savepath", "path", null);
            d.SavePath = Read(iniPath, "download", "SavePath", null);
            d.Title = Read(iniPath, "download", "Title", null);
            d.RunAfter = Read(iniPath, "download", "RunAfter", null);
            string open = Read(iniPath, "download", "OpenFolder", null);
            string ask = Read(iniPath, "download", "AskSavePath", null);
            string lang = Read(iniPath, "download", "Language", null);
            string engine = Read(iniPath, "download", "Engine", null);
            c.Found = c.OutputDir != null || d.SavePath != null || d.Title != null || d.RunAfter != null || open != null || ask != null ||
                      engine != null;

            var def = new DownloadOptions();
            if (string.IsNullOrEmpty(c.OutputDir) || !Directory.Exists(c.OutputDir)) c.OutputDir = defaultOutputDir;
            if (d.SavePath == null) d.SavePath = def.SavePath;
            if (string.IsNullOrEmpty(d.Title)) d.Title = def.Title;
            if (d.RunAfter == null) d.RunAfter = "";
            d.OpenFolder = open != "0";
            d.AskSavePath = ask == "1";
            d.Engine = Engines.Normalize(engine);
            // 진행 창 언어를 정한 적이 없으면 빌더 언어를 따른다
            d.Language = !string.IsNullOrEmpty(lang) ? lang : LoadLanguage(iniPath);
            return c;
        }

        public bool Save(string iniPath)
        {
            try
            {
                // 새 파일은 UTF-16 BOM으로 만들어 두면 이후 쓰기도 유니코드로 저장된다
                if (!File.Exists(iniPath)) File.WriteAllBytes(iniPath, new byte[] { 0xFF, 0xFE });
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }

            bool ok = WritePrivateProfileString("savepath", "path", OutputDir, iniPath);
            ok &= WritePrivateProfileString("download", "SavePath", Download.SavePath, iniPath);
            ok &= WritePrivateProfileString("download", "AskSavePath", Download.AskSavePath ? "1" : "0", iniPath);
            ok &= WritePrivateProfileString("download", "Title", Download.Title, iniPath);
            ok &= WritePrivateProfileString("download", "RunAfter", KeepQuotes(Download.RunAfter), iniPath);
            ok &= WritePrivateProfileString("download", "OpenFolder", Download.OpenFolder ? "1" : "0", iniPath);
            ok &= WritePrivateProfileString("download", "Language", Download.Language, iniPath);
            ok &= WritePrivateProfileString("download", "Engine", Engines.Normalize(Download.Engine), iniPath);
            return ok;
        }

        // 화면 테마: "light", "dark", 없으면 Windows 설정을 따른다
        public static string LoadTheme(string iniPath)
        {
            return Read(iniPath, "ui", "Theme", "");
        }

        public static void SaveTheme(string iniPath, string theme)
        {
            SaveUi(iniPath, "Theme", theme);
        }

        // 빌더 화면 언어 (lang 폴더의 파일 이름), 없으면 한국어
        public static string LoadLanguage(string iniPath)
        {
            string v = Read(iniPath, "ui", "Language", "");
            return v != "" ? v : Lang.Default;
        }

        public static void SaveLanguage(string iniPath, string code)
        {
            SaveUi(iniPath, "Language", code);
        }

        static void SaveUi(string iniPath, string key, string value)
        {
            try
            {
                if (!File.Exists(iniPath)) File.WriteAllBytes(iniPath, new byte[] { 0xFF, 0xFE });
                WritePrivateProfileString("ui", key, value, iniPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public static bool SaveOutputDir(string iniPath, string dir)
        {
            var c = Load(iniPath, dir);
            c.OutputDir = dir;
            return c.Save(iniPath);
        }

        // GetPrivateProfileString은 값이 따옴표로 시작하고 끝나면 바깥 한 쌍을 벗기므로
        // ("경로" "인수" 같은 완료 후 실행 값) 저장할 때 한 겹 더 감싼다
        static string KeepQuotes(string v)
        {
            return v != null && v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"' ? "\"" + v + "\"" : v;
        }

        // 키가 없으면 null
        static string Read(string file, string section, string key, string def)
        {
            const string missing = "\u0001missing\u0001";
            var sb = new StringBuilder(4096);
            GetPrivateProfileString(section, key, missing, sb, sb.Capacity, file);
            string v = sb.ToString();
            return v == missing ? def : v;
        }
    }
}
