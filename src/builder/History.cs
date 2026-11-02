// 만든 실행파일 기록 (history.txt: 시각 \t 토렌트 경로 \t 실행파일 경로, 최신이 위)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace EXTorrent
{
    public class HistoryItem
    {
        public DateTime Time { get; set; }
        public string TorrentPath { get; set; }
        public string ExePath { get; set; }

        public string Name { get { return Path.GetFileName(ExePath); } }
        public bool ExeExists { get { return File.Exists(ExePath); } }
        public bool TorrentExists { get { return File.Exists(TorrentPath); } }
        public string Caption
        {
            get { return Time.ToString("yyyy-MM-dd HH:mm") + "  ·  " + Path.GetDirectoryName(ExePath) + "  ·  " + Path.GetFileName(TorrentPath); }
        }
    }

    static class History
    {
        const int MaxItems = 50;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        static string FilePath { get { return Path.Combine(Program.Root, "history.txt"); } }

        public static List<HistoryItem> Load()
        {
            var list = new List<HistoryItem>();
            try
            {
                if (!File.Exists(FilePath)) return list;
                foreach (string line in File.ReadAllLines(FilePath, Utf8))
                {
                    string[] p = line.Split('\t');
                    DateTime t;
                    if (p.Length != 3 || !DateTime.TryParse(p[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t)) continue;
                    list.Add(new HistoryItem { Time = t, TorrentPath = p[1], ExePath = p[2] });
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return list;
        }

        // 같은 실행파일 경로는 하나만 남긴다
        public static void Add(string torrentPath, string exePath)
        {
            var list = Load();
            list.RemoveAll(i => string.Equals(i.ExePath, exePath, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, new HistoryItem { Time = DateTime.Now, TorrentPath = torrentPath, ExePath = exePath });
            if (list.Count > MaxItems) list.RemoveRange(MaxItems, list.Count - MaxItems);
            Save(list);
        }

        public static void Clear()
        {
            Save(new List<HistoryItem>());
        }

        static void Save(List<HistoryItem> list)
        {
            var sb = new StringBuilder();
            foreach (var i in list)
                sb.Append(i.Time.ToString("o", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(i.TorrentPath).Append('\t').Append(i.ExePath).Append("\r\n");
            try { File.WriteAllText(FilePath, sb.ToString(), Utf8); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
