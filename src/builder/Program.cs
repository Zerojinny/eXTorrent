// eXTorrent 빌더 진입점
//   eXTorrent.exe                       GUI
//   eXTorrent.exe "토렌트파일"           GUI (파일을 아이콘에 끌어다 놓은 경우)
//   eXTorrent.exe "토렌트파일" "저장경로" [/q]
//       창 없이 바로 변환. 종료 코드 0=성공 / 1=실패, /q면 결과 메시지 창도 띄우지 않음
//       다운로드 설정은 config.ini, aria2 설정은 bin\aria2.conf.* 를 사용
//       엔진(config.ini [download] Engine)이 bin에 없으면 내려받는다
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Windows;

[assembly: AssemblyTitle("eXTorrent Builder")]
[assembly: AssemblyDescription("eXTorrent Builder")]
[assembly: AssemblyCompany("eXTorrent")]
[assembly: AssemblyProduct("eXTorrent")]
[assembly: AssemblyVersion("3.2.0.0")]
[assembly: AssemblyFileVersion("3.2.0.0")]
// 4.x 최신 동작(고해상도 화면 대응 등)을 쓰도록 대상 프레임워크를 밝힌다
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace EXTorrent
{
    static class Program
    {
        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int processId);

        public static string Root;
        public static string BinDir;
        public static string IniPath;

        [STAThread]
        static int Main(string[] args)
        {
            Root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            BinDir = Path.Combine(Root, "bin");
            IniPath = Path.Combine(Root, "config.ini");

            if (args.Length >= 2)
            {
                Lang.Load(Config.LoadLanguage(IniPath));
                AttachConsole(-1); // 명령 프롬프트에서 실행했으면 그 콘솔에 출력
                bool quiet = args.Length >= 3 && args[2].Equals("/q", StringComparison.OrdinalIgnoreCase);
                return RunCli(args[0], args[1], quiet);
            }

            string dropped = "";
            if (args.Length == 1 && File.Exists(args[0]) && args[0].EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
                dropped = args[0];

            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.Resources.MergedDictionaries.Add(Ui.Load<ResourceDictionary>("Styles.xaml"));
            Lang.Load(Config.LoadLanguage(IniPath));
            string theme = Config.LoadTheme(IniPath);
            Theme.Apply(theme == "dark" || (theme != "light" && Theme.SystemPrefersDark()));
            app.DispatcherUnhandledException += (s, e) =>
            {
                Dialogs.Error(app.MainWindow, Lang.T("msg.unexpected"), e.Exception.Message);
                e.Handled = true;
            };
            var main = new MainWindow(dropped);
            return app.Run(main.W);
        }

        public static string ConfPath(string profile)
        {
            return Path.Combine(BinDir, "aria2.conf." + profile);
        }

        public static string ReadText(string path)
        {
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
        }

        // 따옴표 제거 + 전체 경로화 + 끝의 역슬래시 제거 (드라이브 루트는 유지)
        static string CleanPath(string p)
        {
            p = Path.GetFullPath(p.Replace("\"", ""));
            if (p.Length > 3 && p.EndsWith("\\")) p = p.TrimEnd('\\');
            return p;
        }

        static int RunCli(string torrent, string outDir, bool quiet)
        {
            bool ok = false;
            string msg;
            try
            {
                torrent = CleanPath(torrent);
                outDir = CleanPath(outDir);
                if (!torrent.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
                    throw new BuildException(Lang.F("err.badTorrent", torrent));

                Config cfg = Config.Load(IniPath, Root);
                string engine = Engines.Ensure(BinDir, cfg.Download.Engine, Console.WriteLine, delegate { });
                string exe = Packager.Build(BinDir, torrent, outDir,
                    ReadText(ConfPath("100")), ReadText(ConfPath("1G")), engine, cfg.Download, Console.WriteLine);
                History.Add(torrent, exe);
                msg = Lang.F("cli.done", exe);
                ok = true;
            }
            catch (Exception ex) { msg = Lang.F("cli.fail", ex.Message); }

            Console.WriteLine(msg);
            if (!quiet)
                MessageBox.Show(msg, "eXTorrent", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Error);
            return ok ? 0 : 1;
        }
    }
}
