// 빌더 메인 창 (레이아웃은 MainWindow.xaml)
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace EXTorrent
{
    class MainWindow
    {
        public readonly Window W;

        readonly RadioButton navBuild, navAria2, navHistory, rdoFixed, rdoAsk, rdoConf100, rdoConf1G, rdoEngineAria2c, rdoEngineNext;
        readonly FrameworkElement pageBuild, pageAria2, pageHistory;
        readonly TextBox txtSavePath, txtRunAfter, txtTitle, txtOutDir, txtConf;
        readonly TextBlock lblSavePath, savePathHint, engineHint, torrentName, torrentMeta, resultIcon, resultText, themeIcon, themeText, confDirty, confPath, historyEmpty;
        readonly CheckBox chkOpenFolder;
        readonly Grid dropZone;
        readonly Rectangle dropBorder;
        readonly FrameworkElement dropEmpty, dropFilled;
        readonly Border resultBar;
        readonly Button resultOpen, btnBuild, btnSaveConf, btnRevertConf;
        readonly ListBox historyList;

        readonly Panel uiLangPanel, dlLangPanel;

        string torrentPath = "";
        TorrentInfo torrent;
        string lastExe;
        string dlLang = Lang.Default; // 진행 창 언어

        // aria2 설정: 편집 중인 내용과 마지막으로 저장한 내용
        readonly Dictionary<string, string> confText = new Dictionary<string, string>();
        readonly Dictionary<string, string> confSaved = new Dictionary<string, string>();
        string confShown = "100";
        bool switchingConf;

        public MainWindow(string dropped)
        {
            W = Ui.Load<Window>("MainWindow.xaml");
            Theme.Attach(W);

            navBuild = F<RadioButton>("NavBuild");
            navAria2 = F<RadioButton>("NavAria2");
            navHistory = F<RadioButton>("NavHistory");
            pageBuild = F<FrameworkElement>("PageBuild");
            pageAria2 = F<FrameworkElement>("PageAria2");
            pageHistory = F<FrameworkElement>("PageHistory");
            rdoFixed = F<RadioButton>("RdoFixed");
            rdoAsk = F<RadioButton>("RdoAsk");
            rdoConf100 = F<RadioButton>("RdoConf100");
            rdoConf1G = F<RadioButton>("RdoConf1G");
            rdoEngineAria2c = F<RadioButton>("RdoEngineAria2c");
            rdoEngineNext = F<RadioButton>("RdoEngineNext");
            txtSavePath = F<TextBox>("TxtSavePath");
            txtRunAfter = F<TextBox>("TxtRunAfter");
            txtTitle = F<TextBox>("TxtTitle");
            txtOutDir = F<TextBox>("TxtOutDir");
            txtConf = F<TextBox>("TxtConf");
            lblSavePath = F<TextBlock>("LblSavePath");
            savePathHint = F<TextBlock>("SavePathHint");
            engineHint = F<TextBlock>("EngineHint");
            torrentName = F<TextBlock>("TorrentName");
            torrentMeta = F<TextBlock>("TorrentMeta");
            resultIcon = F<TextBlock>("ResultIcon");
            resultText = F<TextBlock>("ResultText");
            themeIcon = F<TextBlock>("ThemeIcon");
            themeText = F<TextBlock>("ThemeText");
            confDirty = F<TextBlock>("ConfDirty");
            confPath = F<TextBlock>("ConfPath");
            historyEmpty = F<TextBlock>("HistoryEmpty");
            chkOpenFolder = F<CheckBox>("ChkOpenFolder");
            dropZone = F<Grid>("DropZone");
            dropBorder = F<Rectangle>("DropBorder");
            dropEmpty = F<FrameworkElement>("DropEmpty");
            dropFilled = F<FrameworkElement>("DropFilled");
            resultBar = F<Border>("ResultBar");
            resultOpen = F<Button>("ResultOpen");
            btnBuild = F<Button>("BtnBuild");
            btnSaveConf = F<Button>("BtnSaveConf");
            btnRevertConf = F<Button>("BtnRevertConf");
            historyList = F<ListBox>("HistoryList");
            uiLangPanel = F<Panel>("UiLangPanel");
            dlLangPanel = F<Panel>("DlLangPanel");

            W.Icon = AppIcon(32);
            F<Image>("Logo").Source = AppIcon(64);
            BuildLangButtons();

            // 메뉴
            navBuild.Checked += delegate { ShowPage(pageBuild); };
            navAria2.Checked += delegate { ShowPage(pageAria2); };
            navHistory.Checked += delegate { LoadHistory(); ShowPage(pageHistory); };
            F<Button>("BtnTheme").Click += delegate { ToggleTheme(); };
            UpdateThemeButton();

            // 만들기
            ApplyConfig(Config.Load(Program.IniPath, Program.Root));
            rdoFixed.Checked += delegate { UpdateSaveMode(); };
            rdoAsk.Checked += delegate { UpdateSaveMode(); };
            rdoEngineAria2c.Checked += delegate { UpdateEngineHint(); };
            rdoEngineNext.Checked += delegate { UpdateEngineHint(); };
            F<Button>("BtnSavePath").Click += delegate { PickSavePath(); };
            F<Button>("BtnRunAfter").Click += delegate { PickRunAfter(); };
            F<Button>("BtnOutDir").Click += delegate { PickOutDir(); };
            F<Button>("BtnChangeTorrent").Click += delegate { BrowseTorrent(); };
            F<Button>("BtnLoadIni").Click += delegate { LoadExternalIni(); };
            F<Button>("BtnSaveIni").Click += delegate { SaveIni(); };
            btnBuild.Click += delegate { Build(); };
            resultOpen.Click += delegate { if (lastExe != null) RevealInExplorer(lastExe); };
            dropZone.MouseLeftButtonUp += (s, e) => { if (torrent == null) BrowseTorrent(); };
            dropZone.DragEnter += OnDragOver;
            dropZone.DragOver += OnDragOver;
            dropZone.DragLeave += delegate { dropBorder.SetResourceReference(Shape.StrokeProperty, "DropStrokeBrush"); };
            dropZone.Drop += OnDrop;
            W.DragOver += OnDragOver;
            W.Drop += OnDrop;
            // TextBox는 기본으로 파일 끌어다 놓기를 가로채므로 미리 처리한다
            foreach (var t in new[] { txtSavePath, txtRunAfter, txtTitle, txtOutDir })
            {
                t.PreviewDragOver += OnDragOver;
                t.PreviewDrop += OnDrop;
            }

            // aria2 설정
            LoadConfs();
            rdoConf100.Checked += delegate { SwitchConf("100"); };
            rdoConf1G.Checked += delegate { SwitchConf("1G"); };
            txtConf.TextChanged += delegate { if (!switchingConf) { confText[confShown] = txtConf.Text; UpdateConfDirty(); } };
            btnSaveConf.Click += delegate { SaveConfs(); };
            btnRevertConf.Click += delegate { RevertConf(); };
            W.KeyDown += (s, e) =>
            {
                if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control && pageAria2.Visibility == Visibility.Visible)
                {
                    SaveConfs();
                    e.Handled = true;
                }
            };

            // 기록
            historyList.AddHandler(Button.ClickEvent, new RoutedEventHandler(OnHistoryClick));
            F<Button>("BtnClearHistory").Click += delegate { ClearHistory(); };

            W.Closing += OnClosing;
            RefreshTexts();
            if (dropped != "") SetTorrent(dropped);
        }

        T F<T>(string name) where T : class
        {
            return Ui.Find<T>(W, name);
        }

        static BitmapSource AppIcon(int size)
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("EXTorrent.app.ico"))
                {
                    var dec = new IconBitmapDecoder(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    BitmapFrame best = dec.Frames[0];
                    foreach (BitmapFrame f in dec.Frames)
                        if (f.PixelWidth >= size && (best.PixelWidth < size || f.PixelWidth < best.PixelWidth)) best = f;
                    return best;
                }
            }
            catch (Exception) { return null; }
        }

        void ShowPage(FrameworkElement page)
        {
            foreach (var p in new[] { pageBuild, pageAria2, pageHistory })
                p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
        }

        // --- 테마 ---

        void ToggleTheme()
        {
            Theme.Apply(!Theme.Dark);
            Config.SaveTheme(Program.IniPath, Theme.Dark ? "dark" : "light");
            UpdateThemeButton();
        }

        void UpdateThemeButton()
        {
            themeIcon.Text = Theme.Dark ? "\uE706" : "\uE708";
            themeText.Text = Lang.T(Theme.Dark ? "theme.light" : "theme.dark");
        }

        // --- 언어 ---

        // lang 폴더의 언어마다 세그먼트 버튼을 만든다 (빌더 화면용, 진행 창용)
        void BuildLangButtons()
        {
            foreach (LangInfo l in Lang.Available())
            {
                string code = l.Code;
                var ui = new RadioButton { Content = l.Name, GroupName = "uilang", IsChecked = code == Lang.Code };
                ui.SetResourceReference(FrameworkElement.StyleProperty, "Segment");
                ui.Checked += delegate { ChangeUiLanguage(code); };
                uiLangPanel.Children.Add(ui);

                var dl = new RadioButton { Content = l.Name, GroupName = "dllang", Tag = code };
                dl.SetResourceReference(FrameworkElement.StyleProperty, "Segment");
                dl.Checked += delegate { dlLang = code; };
                dlLangPanel.Children.Add(dl);
            }
        }

        void SelectDlLang(string code)
        {
            dlLang = string.IsNullOrEmpty(code) ? Lang.Default : code;
            foreach (RadioButton b in dlLangPanel.Children)
                b.IsChecked = string.Equals((string)b.Tag, dlLang, StringComparison.OrdinalIgnoreCase);
        }

        void ChangeUiLanguage(string code)
        {
            Lang.Load(code);
            Config.SaveLanguage(Program.IniPath, Lang.Code);
            RefreshTexts();
        }

        // 코드에서 넣는 문자열 (XAML의 L.* 리소스는 자동으로 바뀐다)
        void RefreshTexts()
        {
            F<TextBlock>("VersionText").Text = Lang.F("version", Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
            UpdateThemeButton();
            UpdateSaveMode();
            UpdateEngineHint();
            SwitchConf(confShown);
            if (torrent != null) ShowTorrentMeta();
            resultBar.Visibility = Visibility.Collapsed;
        }

        // --- 토렌트 선택 ---

        void BrowseTorrent()
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = Lang.T("dialog.pickTorrent"), Filter = Lang.T("filter.torrent") };
            if (torrentPath != "") d.InitialDirectory = System.IO.Path.GetDirectoryName(torrentPath);
            if (d.ShowDialog(W) == true) SetTorrent(d.FileName);
        }

        void SetTorrent(string path)
        {
            TorrentInfo info;
            try
            {
                info = TorrentInfo.Load(path);
            }
            catch (Exception ex)
            {
                ShowResult(false, Lang.F("msg.torrentReadFail", ex.Message));
                return;
            }
            torrentPath = path;
            torrent = info;
            torrentName.Text = info.Name != "" ? info.Name : System.IO.Path.GetFileNameWithoutExtension(path);
            ShowTorrentMeta();
            dropEmpty.Visibility = Visibility.Collapsed;
            dropFilled.Visibility = Visibility.Visible;
            dropZone.Cursor = null;
            resultBar.Visibility = Visibility.Collapsed;
        }

        void ShowTorrentMeta()
        {
            torrentMeta.Text = Lang.F("torrent.meta", torrent.Files.Count, TorrentInfo.FormatSize(torrent.TotalSize), torrentPath);
        }

        static string DroppedTorrent(DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return null;
            return files[0].EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) ? files[0] : null;
        }

        void OnDragOver(object sender, DragEventArgs e)
        {
            bool ok = DroppedTorrent(e) != null;
            e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            if (ok) dropBorder.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
            e.Handled = true;
        }

        void OnDrop(object sender, DragEventArgs e)
        {
            dropBorder.SetResourceReference(Shape.StrokeProperty, "DropStrokeBrush");
            string path = DroppedTorrent(e);
            if (path != null)
            {
                SetTorrent(path);
                navBuild.IsChecked = true;
            }
            e.Handled = true;
        }

        // --- 다운로드 설정 ---

        void ApplyConfig(Config c)
        {
            txtOutDir.Text = c.OutputDir;
            txtSavePath.Text = c.Download.SavePath;
            txtTitle.Text = c.Download.Title;
            txtRunAfter.Text = c.Download.RunAfter;
            chkOpenFolder.IsChecked = c.Download.OpenFolder;
            rdoAsk.IsChecked = c.Download.AskSavePath;
            rdoFixed.IsChecked = !c.Download.AskSavePath;
            SelectDlLang(c.Download.Language);
            rdoEngineNext.IsChecked = c.Download.Engine == Engines.Next;
            rdoEngineAria2c.IsChecked = c.Download.Engine != Engines.Next;
            UpdateSaveMode();
            UpdateEngineHint();
        }

        Config CurrentConfig()
        {
            var c = new Config();
            c.OutputDir = txtOutDir.Text.Trim();
            c.Download.SavePath = txtSavePath.Text.Trim();
            c.Download.AskSavePath = rdoAsk.IsChecked == true;
            c.Download.Title = txtTitle.Text.Trim();
            c.Download.RunAfter = txtRunAfter.Text.Trim();
            c.Download.OpenFolder = chkOpenFolder.IsChecked == true;
            c.Download.Language = dlLang;
            c.Download.Engine = SelectedEngine();
            return c;
        }

        void UpdateSaveMode()
        {
            bool ask = rdoAsk.IsChecked == true;
            lblSavePath.Text = Lang.T(ask ? "dl.startPath" : "dl.savePath");
            savePathHint.Text = Lang.T(ask ? "dl.ask.hint" : "dl.fixed.hint");
        }

        string SelectedEngine()
        {
            return rdoEngineNext.IsChecked == true ? Engines.Next : Engines.Aria2c;
        }

        // 선택한 엔진 파일이 bin에 있는지 보여준다 (없으면 만들 때 내려받는다)
        void UpdateEngineHint()
        {
            string path = Engines.PathOf(Program.BinDir, SelectedEngine());
            engineHint.Text = File.Exists(path)
                ? Lang.F("dl.engine.found", path, TorrentInfo.FormatSize(new FileInfo(path).Length))
                : Lang.F("dl.engine.missing", System.IO.Path.GetFileName(path));
        }

        // 받는 PC의 경로를 입력하는 칸이지만, 같은 경로를 쓰는 경우가 많아 이 PC에서 고를 수 있게 한다
        void PickSavePath()
        {
            string p = FolderPicker.Pick(W, Lang.T("dialog.pickSavePath"), txtSavePath.Text.Trim());
            if (p != null) txtSavePath.Text = p;
        }

        void PickOutDir()
        {
            string p = FolderPicker.Pick(W, Lang.T("dialog.pickOutDir"), txtOutDir.Text.Trim());
            if (p == null) return;
            txtOutDir.Text = p;
            Config.SaveOutputDir(Program.IniPath, p);
        }

        void PickRunAfter()
        {
            if (torrent == null)
            {
                Dialogs.Info(W, Lang.T("msg.selectTorrentFirst"), Lang.T("msg.selectTorrentFirst.body"));
                return;
            }
            string picked = FilePicker.Pick(W, torrent.Files, txtRunAfter.Text.Trim());
            if (picked != null) txtRunAfter.Text = picked;
        }

        void SaveIni()
        {
            if (CurrentConfig().Save(Program.IniPath))
                ShowResult(true, Lang.T("msg.iniSaved"), false);
            else
                ShowResult(false, Lang.F("msg.iniSaveFail", Program.IniPath));
        }

        void LoadExternalIni()
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = Lang.T("dialog.loadIni"), Filter = Lang.T("filter.ini"), InitialDirectory = Program.Root };
            if (d.ShowDialog(W) != true) return;
            Config c = Config.Load(d.FileName, txtOutDir.Text);
            if (!c.Found)
            {
                Dialogs.Show(W, DialogKind.Warning, Lang.T("msg.iniNotFound"), Lang.T("msg.iniNotFound.body"));
                return;
            }
            ApplyConfig(c);
            ShowResult(true, Lang.T("msg.iniLoaded"), false);
        }

        // --- 만들기 ---

        async void Build()
        {
            string outDir = txtOutDir.Text.Trim();
            if (torrent == null || !File.Exists(torrentPath))
            {
                ShowResult(false, Lang.T("msg.selectTorrentFirst"));
                return;
            }
            if (outDir == "" || !Directory.Exists(outDir))
            {
                ShowResult(false, Lang.T("msg.badOutDir"));
                return;
            }
            string path = torrentPath, conf100 = ConfFor("100"), conf1G = ConfFor("1G");
            DownloadOptions opt = CurrentConfig().Download;
            try
            {
                btnBuild.IsEnabled = false;
                W.Cursor = Cursors.Wait;
                // 엔진이 bin에 없으면 내려받는다 (진행률은 결과 줄에 표시)
                string engine = Engines.PathOf(Program.BinDir, opt.Engine);
                if (!File.Exists(engine))
                {
                    ShowBusy(Lang.F("msg.engineDownloading", opt.Engine, ""));
                    int lastPct = -1;
                    engine = await Task.Run(() => Engines.Ensure(Program.BinDir, opt.Engine, delegate { }, (done, total) =>
                    {
                        int pct = total > 0 ? (int)(done * 100 / total) : -1;
                        if (pct == lastPct) return;
                        lastPct = pct;
                        string text = Lang.F("msg.engineDownloading", opt.Engine, pct >= 0 ? pct + "%" : TorrentInfo.FormatSize(done));
                        W.Dispatcher.BeginInvoke(new Action(() => ShowBusy(text)));
                    }));
                    UpdateEngineHint();
                }
                string exe = await Task.Run(() => Packager.Build(Program.BinDir, path, outDir, conf100, conf1G, engine, opt, delegate { }));
                History.Add(path, exe);
                lastExe = exe;
                ShowResult(true, Lang.F("msg.built", System.IO.Path.GetFileName(exe), TorrentInfo.FormatSize(new FileInfo(exe).Length)), true);
            }
            catch (Exception ex)
            {
                ShowResult(false, ex.Message);
            }
            finally
            {
                W.Cursor = null;
                btnBuild.IsEnabled = true;
            }
        }

        void ShowBusy(string message)
        {
            if (btnBuild.IsEnabled) return; // 이미 끝났으면 늦게 도착한 진행률은 버린다
            resultIcon.Text = "";
            resultIcon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            resultBar.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
            resultText.Text = message;
            resultOpen.Visibility = Visibility.Collapsed;
            resultBar.Visibility = Visibility.Visible;
        }

        void ShowResult(bool ok, string message, bool canOpen = false)
        {
            resultIcon.Text = ok ? "\uE930" : "\uEA39";
            resultIcon.SetResourceReference(TextBlock.ForegroundProperty, ok ? "SuccessBrush" : "ErrorBrush");
            resultBar.SetResourceReference(Border.BackgroundProperty, ok ? "SuccessBgBrush" : "ErrorBgBrush");
            resultText.Text = message;
            resultOpen.Visibility = canOpen ? Visibility.Visible : Visibility.Collapsed;
            resultBar.Visibility = Visibility.Visible;
        }

        static void RevealInExplorer(string file)
        {
            if (File.Exists(file))
                Process.Start("explorer.exe", "/select,\"" + file + "\"");
            else if (Directory.Exists(System.IO.Path.GetDirectoryName(file)))
                Process.Start(System.IO.Path.GetDirectoryName(file));
        }

        // --- aria2 설정 ---

        static string Crlf(string s)
        {
            return s.Replace("\r\n", "\n").Replace("\n", "\r\n");
        }

        string ConfFor(string profile)
        {
            return confText[profile];
        }

        void LoadConfs()
        {
            foreach (string p in new[] { "100", "1G" })
            {
                string text = Crlf(Program.ReadText(Program.ConfPath(p)));
                confText[p] = text;
                confSaved[p] = text;
            }
            SwitchConf(confShown);
        }

        void SwitchConf(string profile)
        {
            confShown = profile;
            switchingConf = true;
            txtConf.Text = confText[profile];
            switchingConf = false;
            string path = Program.ConfPath(profile);
            confPath.Text = path + (File.Exists(path) ? "" : Lang.T("aria2.path.missing")) + Lang.T("aria2.path.note");
            UpdateConfDirty();
        }

        bool ConfsDirty()
        {
            return confText["100"] != confSaved["100"] || confText["1G"] != confSaved["1G"];
        }

        void UpdateConfDirty()
        {
            bool dirty = ConfsDirty();
            confDirty.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
            btnSaveConf.IsEnabled = dirty;
            btnRevertConf.IsEnabled = dirty;
        }

        bool SaveConfs()
        {
            try
            {
                Directory.CreateDirectory(Program.BinDir);
                var utf8 = new UTF8Encoding(false);
                foreach (string p in new[] { "100", "1G" })
                {
                    File.WriteAllText(Program.ConfPath(p), confText[p], utf8);
                    confSaved[p] = confText[p];
                }
            }
            catch (Exception ex)
            {
                Dialogs.Error(W, Lang.T("msg.confSaveFail"), ex.Message);
                return false;
            }
            SwitchConf(confShown);
            return true;
        }

        void RevertConf()
        {
            confText["100"] = confSaved["100"];
            confText["1G"] = confSaved["1G"];
            SwitchConf(confShown);
        }

        void OnClosing(object sender, CancelEventArgs e)
        {
            if (!ConfsDirty()) return;
            int ans = Dialogs.Show(W, DialogKind.Question, Lang.T("ask.saveConf"), Lang.T("ask.saveConf.body"),
                Lang.T("btn.save"), Lang.T("btn.dontSave"), Lang.T("btn.cancel"));
            if (ans == 0 ? !SaveConfs() : ans != 1) e.Cancel = true;
        }

        // --- 기록 ---

        void LoadHistory()
        {
            List<HistoryItem> items = History.Load();
            historyList.ItemsSource = items;
            historyEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void OnHistoryClick(object sender, RoutedEventArgs e)
        {
            var b = e.OriginalSource as Button;
            var item = b != null ? b.DataContext as HistoryItem : null;
            if (item == null) return;
            if ((string)b.Tag == "open")
            {
                RevealInExplorer(item.ExePath);
            }
            else if ((string)b.Tag == "rebuild")
            {
                SetTorrent(item.TorrentPath);
                txtOutDir.Text = System.IO.Path.GetDirectoryName(item.ExePath);
                navBuild.IsChecked = true;
            }
        }

        void ClearHistory()
        {
            if (Dialogs.Show(W, DialogKind.Question, Lang.T("ask.clearHistory"), Lang.T("ask.clearHistory.body"),
                    Lang.T("btn.clear"), Lang.T("btn.cancel")) != 0) return;
            History.Clear();
            LoadHistory();
        }
    }
}
