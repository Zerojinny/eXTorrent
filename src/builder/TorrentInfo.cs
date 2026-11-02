// .torrent 파일에서 이름, 받게 될 파일 목록, 전체 크기 읽기
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EXTorrent
{
    class TorrentInfo
    {
        public string Name = "";
        public List<string> Files = new List<string>(); // 다운로드 폴더 기준 상대 경로 (예: "폴더\setup.exe")
        public long TotalSize;

        // 읽을 수 없으면 BuildException
        public static TorrentInfo Load(string torrentPath)
        {
            byte[] data = File.ReadAllBytes(torrentPath);
            int pos = 0;
            var root = Decode(data, ref pos, 0) as Dictionary<string, object>;
            var info = root != null ? Get(root, "info") as Dictionary<string, object> : null;
            if (info == null) throw new BuildException(Lang.F("err.torrentInfo", torrentPath));

            var t = new TorrentInfo();
            t.Name = Str(Get(info, "name.utf-8") ?? Get(info, "name"));
            var files = Get(info, "files") as List<object>;
            if (files == null)
            {
                t.Files.Add(t.Name); // 파일 하나짜리 토렌트
                t.TotalSize = Num(Get(info, "length"));
                return t;
            }
            foreach (object f in files)
            {
                var fd = f as Dictionary<string, object>;
                if (fd == null) continue;
                t.TotalSize += Num(Get(fd, "length"));
                var parts = (Get(fd, "path.utf-8") ?? Get(fd, "path")) as List<object>;
                if (parts == null || parts.Count == 0) continue;
                var sb = new StringBuilder(t.Name);
                foreach (object p in parts) sb.Append('\\').Append(Str(p));
                t.Files.Add(sb.ToString());
            }
            return t;
        }

        public static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int u = 0;
            while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
            return (u == 0 || v >= 100 ? v.ToString("0") : v.ToString("0.0")) + " " + units[u];
        }

        static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) ? v : null;
        }

        static string Str(object o)
        {
            var b = o as byte[];
            return b != null ? Encoding.UTF8.GetString(b) : "";
        }

        static long Num(object o)
        {
            return o is long ? (long)o : 0;
        }

        // bencode: 정수 → long, 문자열 → byte[], 리스트 → List<object>, 사전 → Dictionary<string, object>
        static object Decode(byte[] d, ref int pos, int depth)
        {
            if (pos >= d.Length || depth > 64) throw new BuildException(Lang.T("err.torrentFormat"));
            byte c = d[pos];
            if (c == 'i')
            {
                int end = Array.IndexOf(d, (byte)'e', pos);
                long v;
                if (end < 0 || !long.TryParse(Encoding.ASCII.GetString(d, pos + 1, end - pos - 1), out v))
                    throw new BuildException(Lang.T("err.torrentFormat"));
                pos = end + 1;
                return v;
            }
            if (c == 'l' || c == 'd')
            {
                pos++;
                var list = c == 'l' ? new List<object>() : null;
                var dict = c == 'd' ? new Dictionary<string, object>() : null;
                while (pos < d.Length && d[pos] != 'e')
                {
                    if (list != null)
                    {
                        list.Add(Decode(d, ref pos, depth + 1));
                    }
                    else
                    {
                        string key = Encoding.UTF8.GetString(ReadString(d, ref pos));
                        dict[key] = Decode(d, ref pos, depth + 1);
                    }
                }
                if (pos >= d.Length) throw new BuildException(Lang.T("err.torrentFormat"));
                pos++;
                return list != null ? (object)list : dict;
            }
            return ReadString(d, ref pos);
        }

        static byte[] ReadString(byte[] d, ref int pos)
        {
            int colon = Array.IndexOf(d, (byte)':', pos);
            int len;
            if (colon < 0 || !int.TryParse(Encoding.ASCII.GetString(d, pos, colon - pos), out len) || len < 0 || len > d.Length - colon - 1)
                throw new BuildException(Lang.T("err.torrentFormat"));
            var s = new byte[len];
            Buffer.BlockCopy(d, colon + 1, s, 0, len);
            pos = colon + 1 + len;
            return s;
        }
    }
}
