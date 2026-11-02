// XAML 불러오기, 대화 상자, 폴더 선택 창
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;

namespace EXTorrent
{
    static class Ui
    {
        // exe 안에 리소스로 들어 있는 XAML을 읽는다 (build.bat의 /resource 이름: EXTorrent.파일명)
        public static T Load<T>(string name)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("EXTorrent." + name))
                return (T)XamlReader.Load(s);
        }

        public static T Find<T>(FrameworkElement root, string name) where T : class
        {
            var e = root.FindName(name) as T;
            if (e == null) throw new InvalidOperationException("XAML에 " + name + " 이(가) 없습니다.");
            return e;
        }
    }

    enum DialogKind { Info, Success, Warning, Error, Question }

    static class Dialogs
    {
        // 누른 버튼의 번호 (0부터), 창을 닫으면 -1. 첫 번째 버튼이 강조 버튼
        public static int Show(Window owner, DialogKind kind, string heading, string body, params string[] buttons)
        {
            var w = Ui.Load<Window>("Dialog.xaml");
            w.Title = owner != null ? owner.Title : "eXTorrent";
            w.Owner = owner;
            if (owner == null) w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Theme.Attach(w);

            var icon = Ui.Find<TextBlock>(w, "Icon");
            string brush;
            switch (kind)
            {
                case DialogKind.Success: icon.Text = "\uE930"; brush = "SuccessBrush"; break;
                case DialogKind.Warning: icon.Text = "\uE7BA"; brush = "ErrorBrush"; break;
                case DialogKind.Error: icon.Text = "\uEA39"; brush = "ErrorBrush"; break;
                case DialogKind.Question: icon.Text = "\uE9CE"; brush = "AccentBrush"; break;
                default: icon.Text = "\uE946"; brush = "AccentBrush"; break;
            }
            icon.SetResourceReference(TextBlock.ForegroundProperty, brush);
            Ui.Find<TextBlock>(w, "Heading").Text = heading;
            var bodyText = Ui.Find<TextBlock>(w, "Body");
            bodyText.Text = body ?? "";
            if (string.IsNullOrEmpty(body)) bodyText.Visibility = Visibility.Collapsed;

            if (buttons.Length == 0) buttons = new[] { Lang.T("btn.ok") };
            int result = -1;
            var panel = Ui.Find<StackPanel>(w, "Buttons");
            for (int i = 0; i < buttons.Length; i++)
            {
                int index = i;
                var b = new Button { Content = buttons[i], MinWidth = 96, Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0) };
                if (i == 0)
                {
                    b.Style = (Style)Application.Current.FindResource("AccentButton");
                    b.IsDefault = true;
                }
                b.Click += delegate { result = index; w.Close(); };
                panel.Children.Add(b);
            }
            w.KeyDown += (s, e) => { if (e.Key == Key.Escape) w.Close(); };
            w.ShowDialog();
            return result;
        }

        public static void Info(Window owner, string heading, string body)
        {
            Show(owner, DialogKind.Info, heading, body);
        }

        public static void Error(Window owner, string heading, string body)
        {
            Show(owner, DialogKind.Error, heading, body);
        }
    }

    // 완료 후 실행할 파일 고르기
    static class FilePicker
    {
        static readonly string[] RunnableExts = { ".exe", ".bat", ".cmd", ".msi", ".vbs", ".lnk" };

        public class Row
        {
            public string Path { get; set; }
            public string Icon { get; set; }
        }

        static bool IsRunnable(string path)
        {
            return Array.IndexOf(RunnableExts, System.IO.Path.GetExtension(path).ToLowerInvariant()) >= 0;
        }

        // 고른 상대 경로, 취소하면 null
        public static string Pick(Window owner, List<string> files, string current)
        {
            var w = Ui.Load<Window>("FilePicker.xaml");
            w.Owner = owner;
            Theme.Attach(w);
            var search = Ui.Find<TextBox>(w, "Search");
            var hint = Ui.Find<TextBlock>(w, "SearchHint");
            var chk = Ui.Find<CheckBox>(w, "ChkRunnable");
            var list = Ui.Find<ListBox>(w, "List");
            var count = Ui.Find<TextBlock>(w, "Count");
            var ok = Ui.Find<Button>(w, "BtnOk");

            var all = new List<string>(files);
            all.Sort(StringComparer.OrdinalIgnoreCase);
            // 실행 파일이 하나도 없으면 전체 목록을 보여준다
            if (!all.Exists(IsRunnable)) chk.IsChecked = false;

            Action fill = delegate
            {
                string keep = list.SelectedItem != null ? ((Row)list.SelectedItem).Path : current;
                string q = search.Text.Trim();
                var rows = new List<Row>();
                foreach (string f in all)
                {
                    bool run = IsRunnable(f);
                    if (chk.IsChecked == true && !run) continue;
                    if (q.Length > 0 && f.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    rows.Add(new Row { Path = f, Icon = run ? "\uE756" : "\uE8A5" });
                }
                list.ItemsSource = rows;
                list.SelectedIndex = rows.FindIndex(r => string.Equals(r.Path, keep, StringComparison.OrdinalIgnoreCase));
                if (list.SelectedIndex < 0 && rows.Count > 0) list.SelectedIndex = 0;
                if (list.SelectedItem != null) list.ScrollIntoView(list.SelectedItem);
                count.Text = Lang.F("picker.count", rows.Count, all.Count);
                ok.IsEnabled = list.SelectedItem != null;
                hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            };
            search.TextChanged += delegate { fill(); };
            chk.Checked += delegate { fill(); };
            chk.Unchecked += delegate { fill(); };
            list.SelectionChanged += delegate { ok.IsEnabled = list.SelectedItem != null; };
            list.MouseDoubleClick += delegate { if (list.SelectedItem != null) w.DialogResult = true; };
            ok.Click += delegate { if (list.SelectedItem != null) w.DialogResult = true; };
            w.Loaded += delegate { search.Focus(); };
            fill();
            return w.ShowDialog() == true ? ((Row)list.SelectedItem).Path : null;
        }
    }

    // Windows 기본 폴더 선택 창 (IFileOpenDialog)
    static class FolderPicker
    {
        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        class FileOpenDialogCoClass { }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem item);
            void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
            void GetAttributes(uint mask, out uint attrs);
            void Compare(IShellItem item, uint hint, out int order);
        }

        [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count, IntPtr specs);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr sink, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint fos);
            void GetOptions(out uint fos);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem item, int place);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string ext);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
            void GetResults(out IntPtr items);
            void GetSelectedItems(out IntPtr items);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItem item);

        const uint FOS_PICKFOLDERS = 0x20, FOS_FORCEFILESYSTEM = 0x40, FOS_PATHMUSTEXIST = 0x800;
        const uint SIGDN_FILESYSPATH = 0x80058000;

        // 고른 폴더, 취소하면 null
        public static string Pick(Window owner, string title, string initial)
        {
            var dlg = (IFileOpenDialog)new FileOpenDialogCoClass();
            try
            {
                uint opt;
                dlg.GetOptions(out opt);
                dlg.SetOptions(opt | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
                dlg.SetTitle(title);
                if (!string.IsNullOrEmpty(initial))
                {
                    string dir = Environment.ExpandEnvironmentVariables(initial);
                    if (Directory.Exists(dir))
                    {
                        try
                        {
                            IShellItem start;
                            SHCreateItemFromParsingName(dir, IntPtr.Zero, typeof(IShellItem).GUID, out start);
                            dlg.SetFolder(start);
                        }
                        catch (COMException) { }
                    }
                }
                IntPtr hwnd = owner != null ? new WindowInteropHelper(owner).Handle : IntPtr.Zero;
                if (dlg.Show(hwnd) != 0) return null;
                IShellItem item;
                dlg.GetResult(out item);
                string path;
                item.GetDisplayName(SIGDN_FILESYSPATH, out path);
                return path;
            }
            finally
            {
                Marshal.ReleaseComObject(dlg);
            }
        }
    }
}
