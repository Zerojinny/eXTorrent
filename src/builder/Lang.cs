// 언어 파일 (lang\*.ini) 읽기
// [builder] 값은 화면 문자열로 쓰고, XAML에서는 {DynamicResource L.키}로 참조한다.
// [launcher] 값은 만든 실행파일 안에 lang.ini로 넣어 진행 창에서 쓴다.
// 파일에 없는 키는 exe에 들어 있는 한국어(ko.ini)로 대신한다.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;

namespace EXTorrent
{
    class LangInfo
    {
        public string Code; // 파일 이름 (ko, en ...)
        public string Name; // [info] Name
    }

    static class Lang
    {
        public const string Default = "ko";

        static Dictionary<string, string> strings = new Dictionary<string, string>();
        static Dictionary<string, string> fallback;

        public static string Code { get; private set; }

        static string Dir { get { return Path.Combine(Program.Root, "lang"); } }

        public static List<LangInfo> Available()
        {
            var list = new List<LangInfo>();
            try
            {
                if (Directory.Exists(Dir))
                {
                    foreach (string f in Directory.GetFiles(Dir, "*.ini"))
                    {
                        string code = Path.GetFileNameWithoutExtension(f);
                        string name;
                        if (!Parse(File.ReadAllText(f, Encoding.UTF8), "info").TryGetValue("Name", out name)) name = code;
                        list.Add(new LangInfo { Code = code, Name = name });
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (!list.Exists(l => l.Code == Default)) list.Insert(0, new LangInfo { Code = Default, Name = "한국어" });
            // 한국어를 맨 앞에
            list.Sort((a, b) => a.Code == Default ? -1 : b.Code == Default ? 1 : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        // 언어를 바꾸고, 앱이 떠 있으면 화면 문자열(L.*)도 바꾼다
        public static void Load(string code)
        {
            if (string.IsNullOrEmpty(code)) code = Default;
            if (fallback == null) fallback = Parse(Embedded(), "builder");
            string text = ReadFile(code);
            if (text == null)
            {
                code = Default;
                text = ReadFile(code) ?? Embedded();
            }
            Code = code;
            strings = Parse(text, "builder");
            if (Application.Current == null) return;
            var r = Application.Current.Resources;
            foreach (var kv in fallback) r["L." + kv.Key] = T(kv.Key);
        }

        public static string T(string key)
        {
            string v;
            if (strings.TryGetValue(key, out v)) return v;
            if (fallback != null && fallback.TryGetValue(key, out v)) return v;
            return key;
        }

        public static string F(string key, params object[] args)
        {
            string t = T(key);
            try { return string.Format(t, args); }
            catch (FormatException) { return t; }
        }

        // 만든 실행파일에 넣을 진행 창 문자열 (키=값 줄들)
        public static string LauncherSection(string code)
        {
            string text = ReadFile(string.IsNullOrEmpty(code) ? Default : code) ?? ReadFile(Default) ?? Embedded();
            var sb = new StringBuilder();
            foreach (var kv in Parse(text, "launcher"))
                sb.Append(kv.Key).Append('=').Append(kv.Value.Replace("\r", "").Replace("\n", "\\n")).Append("\r\n");
            return sb.ToString();
        }

        static string ReadFile(string code)
        {
            if (code.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            string path = Path.Combine(Dir, code + ".ini");
            try { return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        static string Embedded()
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("EXTorrent.ko.ini"))
            using (var r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd();
        }

        // 지정한 섹션의 "키=값"을 읽는다. 값의 \n은 줄바꿈으로 바꾼다
        static Dictionary<string, string> Parse(string text, string section)
        {
            var d = new Dictionary<string, string>();
            bool inSection = false;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == ';' || trimmed[0] == '#') continue;
                if (trimmed[0] == '[' && trimmed.EndsWith("]"))
                {
                    inSection = string.Equals(trimmed.Substring(1, trimmed.Length - 2).Trim(), section, StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                int eq = line.IndexOf('=');
                if (!inSection || eq <= 0) continue;
                d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Replace("\\n", "\n");
            }
            return d;
        }
    }
}
