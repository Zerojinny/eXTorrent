// 다운로드 엔진 (aria2c / aria2-next)
// bin\<엔진>.exe를 쓰고, 없으면 GitHub 최신 릴리스에서 공식 Windows x64 바이너리를 내려받아 저장한다.
//   aria2c     : aria2/aria2 의 *-win-64bit-build*.zip 안의 aria2c.exe
//   aria2-next : AnInsomniacy/aria2-next 의 *-windows-x86_64.exe (같은 릴리스의 checksums.sha256으로 확인)
using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace EXTorrent
{
    static class Engines
    {
        public const string Aria2c = "aria2c";
        public const string Next = "aria2-next";
        public static readonly string[] All = { Aria2c, Next };

        // config.ini / settings.ini 값 정리. 모르는 값이면 aria2c
        public static string Normalize(string id)
        {
            return string.Equals(id, Next, StringComparison.OrdinalIgnoreCase) ? Next : Aria2c;
        }

        public static string PathOf(string binDir, string id)
        {
            return Path.Combine(binDir, Normalize(id) + ".exe");
        }

        static string Repo(string id)
        {
            return id == Next ? "AnInsomniacy/aria2-next" : "aria2/aria2";
        }

        // 엔진 경로를 돌려준다. 없으면 내려받는다. progress(받은 바이트, 전체 바이트 또는 0)
        public static string Ensure(string binDir, string id, Action<string> log, Action<long, long> progress)
        {
            id = Normalize(id);
            string path = PathOf(binDir, id);
            if (File.Exists(path)) return path;

            log(Lang.F("log.engineDownload", id));
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            string tmp = path + ".download";
            try
            {
                Directory.CreateDirectory(binDir);
                string release = GetString("https://api.github.com/repos/" + Repo(id) + "/releases/latest");
                if (id == Next)
                {
                    string url = FindAsset(release, id, @"-windows-x86_64\.exe$");
                    string sums = GetString(FindAsset(release, id, @"checksums\.sha256$"));
                    Download(url, tmp, progress);
                    string expect = ExpectedHash(sums, Path.GetFileName(new Uri(url).LocalPath));
                    if (expect == null || !string.Equals(Sha256(tmp), expect, StringComparison.OrdinalIgnoreCase))
                        throw new BuildException(Lang.F("err.engineHash", id));
                }
                else
                {
                    string zip = tmp + ".zip";
                    Download(FindAsset(release, id, @"-win-64bit-build\d+\.zip$"), zip, progress);
                    try { ExtractFromZip(zip, "aria2c.exe", tmp, id); }
                    finally { File.Delete(zip); }
                }
                File.Move(tmp, path);
            }
            catch (BuildException) { throw; }
            catch (Exception ex) { throw new BuildException(Lang.F("err.engineDownload", id, ex.Message)); }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            return path;
        }

        static HttpWebRequest Request(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "eXTorrent"; // GitHub API는 User-Agent가 없으면 거부한다
            req.Timeout = 30000;
            req.ReadWriteTimeout = 60000;
            return req;
        }

        static string GetString(string url)
        {
            using (var res = Request(url).GetResponse())
            using (var r = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                return r.ReadToEnd();
        }

        static void Download(string url, string dest, Action<long, long> progress)
        {
            using (var res = (HttpWebResponse)Request(url).GetResponse())
            using (Stream src = res.GetResponseStream())
            using (var dst = new FileStream(dest, FileMode.Create, FileAccess.Write))
            {
                long total = Math.Max(res.ContentLength, 0), done = 0;
                var buf = new byte[81920];
                int n;
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                {
                    dst.Write(buf, 0, n);
                    done += n;
                    progress(done, total);
                }
            }
        }

        // 릴리스 JSON의 browser_download_url 중 파일 이름이 pattern에 맞는 것
        static string FindAsset(string releaseJson, string id, string pattern)
        {
            foreach (Match m in Regex.Matches(releaseJson, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\""))
                if (Regex.IsMatch(m.Groups[1].Value, pattern, RegexOptions.IgnoreCase)) return m.Groups[1].Value;
            throw new BuildException(Lang.F("err.engineAsset", id));
        }

        // "해시  파일이름" 형식의 체크섬 목록에서 name의 해시
        static string ExpectedHash(string sums, string name)
        {
            foreach (string line in sums.Split('\n'))
            {
                string[] p = line.Trim().Split(new[] { ' ', '\t', '*' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 2 && p[1] == name) return p[0];
            }
            return null;
        }

        static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var f = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "");
        }

        static void ExtractFromZip(string zip, string name, string dest, string id)
        {
            using (ZipArchive a = ZipFile.OpenRead(zip))
            {
                foreach (ZipArchiveEntry e in a.Entries)
                {
                    if (!string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                    e.ExtractToFile(dest, true);
                    return;
                }
            }
            throw new BuildException(Lang.F("err.engineAsset", id));
        }
    }
}
