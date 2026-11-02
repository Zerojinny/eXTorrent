// 실행파일 생성: bin\launcher.exe 뒤에 torrent, aria2.conf.*, settings.ini, lang.ini, engine.exe를 붙인다.
// 형식은 src\launcher\launcher.c 머리말 참고.
using System;
using System.IO;
using System.Text;

namespace EXTorrent
{
    class BuildException : Exception
    {
        public BuildException(string message) : base(message) { }
    }

    // 받는 PC에서의 다운로드 설정 (실행파일 안 settings.ini로 들어감)
    class DownloadOptions
    {
        public string SavePath = @"C:\Temp";
        public bool AskSavePath; // true면 실행할 때 폴더 선택 창을 띄우고, SavePath는 시작 위치로 쓴다
        public string Title = "eXTorrent";
        public string RunAfter = "";
        public bool OpenFolder = true;
        public string Language = Lang.Default; // 진행 창 언어 (lang 폴더의 파일 이름)
        public string Engine = Engines.Aria2c; // 다운로드 엔진 (Engines.All)

        public string ToIni()
        {
            var sb = new StringBuilder();
            sb.Append("[eXTorrent]\r\n");
            sb.Append("Engine=").Append(Engines.Normalize(Engine)).Append("\r\n");
            sb.Append("SavePath=").Append(OneLine(SavePath)).Append("\r\n");
            sb.Append("AskSavePath=").Append(AskSavePath ? "1" : "0").Append("\r\n");
            sb.Append("Title=").Append(OneLine(Title)).Append("\r\n");
            sb.Append("RunAfter=").Append(OneLine(RunAfter)).Append("\r\n");
            sb.Append("OpenFolder=").Append(OpenFolder ? "1" : "0").Append("\r\n");
            return sb.ToString();
        }

        static string OneLine(string s)
        {
            return (s ?? "").Replace("\r", "").Replace("\n", "").Trim();
        }
    }

    static class Packager
    {
        public const string LauncherName = "launcher.exe";
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        // 성공하면 만들어진 exe 경로를 돌려준다. 실패하면 BuildException
        // enginePath: 받는 PC에서 쓸 엔진 실행파일 (Engines.Ensure로 준비)
        public static string Build(string binDir, string torrentPath, string outDir,
            string conf100, string conf1G, string enginePath, DownloadOptions opt, Action<string> log)
        {
            string launcher = Path.Combine(binDir, LauncherName);
            if (!File.Exists(launcher))
                throw new BuildException(Lang.F("err.noLauncher", launcher));
            if (!File.Exists(enginePath))
                throw new BuildException(Lang.F("err.noEngine", enginePath));
            if (!File.Exists(torrentPath))
                throw new BuildException(Lang.F("err.noTorrent", torrentPath));

            byte[] torrent = File.ReadAllBytes(torrentPath);
            if (!LooksLikeTorrent(torrent))
                throw new BuildException(Lang.F("err.badTorrent", torrentPath));
            if (!opt.AskSavePath && string.IsNullOrWhiteSpace(opt.SavePath))
                throw new BuildException(Lang.T("err.noSavePath"));

            string outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(torrentPath) + ".exe");
            string self = System.Reflection.Assembly.GetEntryAssembly().Location;
            if (string.Equals(Path.GetFullPath(outPath), Path.GetFullPath(self), StringComparison.OrdinalIgnoreCase))
                throw new BuildException(Lang.T("err.selfOverwrite"));

            log(Lang.T("log.compose"));
            byte[] payload;
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                AddEntry(w, Path.GetFileName(torrentPath), torrent);
                AddEntry(w, "aria2.conf.100", Utf8.GetBytes(conf100 ?? ""));
                AddEntry(w, "aria2.conf.1G", Utf8.GetBytes(conf1G ?? ""));
                AddEntry(w, "settings.ini", Utf8.GetBytes(opt.ToIni()));
                AddEntry(w, "lang.ini", Utf8.GetBytes(Lang.LauncherSection(opt.Language)));
                AddEntry(w, "engine.exe", File.ReadAllBytes(enginePath));
                w.Flush();
                payload = ms.ToArray();
            }

            byte[] stub = File.ReadAllBytes(launcher);
            Directory.CreateDirectory(outDir);

            log(Lang.T("log.write"));
            string tmp = outPath + ".tmp";
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                using (var w = new BinaryWriter(fs))
                {
                    w.Write(stub);
                    w.Write(payload);
                    w.Write(Encoding.ASCII.GetBytes("EXTR"));
                    w.Write((uint)stub.Length);
                    w.Write((uint)payload.Length);
                    w.Write(Crc32(payload));
                }
                if (File.Exists(outPath)) File.Delete(outPath);
                File.Move(tmp, outPath);
            }
            catch (IOException ex)
            {
                if (File.Exists(tmp)) File.Delete(tmp);
                throw new BuildException(Lang.F("err.writeFail", outPath, ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                if (File.Exists(tmp)) File.Delete(tmp);
                throw new BuildException(Lang.F("err.writeDenied", outPath, ex.Message));
            }
            return outPath;
        }

        static void AddEntry(BinaryWriter w, string name, byte[] data)
        {
            byte[] n = Utf8.GetBytes(name);
            if (n.Length == 0 || n.Length > ushort.MaxValue)
                throw new BuildException(Lang.F("err.nameTooLong", name));
            w.Write((ushort)n.Length);
            w.Write(n);
            w.Write((uint)data.Length);
            w.Write(data);
        }

        // bencode 사전으로 시작하고 info 키가 있으면 torrent로 본다
        static bool LooksLikeTorrent(byte[] d)
        {
            if (d.Length < 10 || d[0] != (byte)'d') return false;
            byte[] key = Encoding.ASCII.GetBytes("4:info");
            for (int i = 0; i + key.Length <= d.Length; i++)
            {
                int k = 0;
                while (k < key.Length && d[i + k] == key[k]) k++;
                if (k == key.Length) return true;
            }
            return false;
        }

        static uint[] crcTable;

        static uint Crc32(byte[] data)
        {
            if (crcTable == null)
            {
                crcTable = new uint[256];
                for (uint i = 0; i < 256; i++)
                {
                    uint v = i;
                    for (int k = 0; k < 8; k++) v = (v & 1) != 0 ? 0xEDB88320 ^ (v >> 1) : v >> 1;
                    crcTable[i] = v;
                }
            }
            uint c = 0xFFFFFFFF;
            foreach (byte b in data) c = crcTable[(c ^ b) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFF;
        }
    }
}
