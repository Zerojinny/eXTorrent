// 라이트/다크 색상과 창 장식 (다크 제목 표시줄, Windows 11 Mica 배경)
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace EXTorrent
{
    static class Theme
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("dwmapi.dll")]
        static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

        [StructLayout(LayoutKind.Sequential)]
        struct Margins { public int Left, Right, Top, Bottom; }

        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        const int DWMSBT_MAINWINDOW = 2; // Mica

        public static bool Dark { get; private set; }

        // Windows 11 22H2 이상이면 Mica를 쓰고, 카드는 반투명하게 그린다
        static readonly bool MicaSupported = Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22621;

        public static bool SystemPrefersDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k != null ? k.GetValue("AppsUseLightTheme") : null;
                    return v is int && (int)v == 0;
                }
            }
            catch (Exception) { return false; }
        }

        public static void Apply(bool dark)
        {
            Dark = dark;
            var r = Application.Current.Resources;
            bool m = MicaSupported;
            if (dark)
            {
                Set(r, "WindowBrush", m ? 0x00000000 : 0xFF202020);
                Set(r, "LayerBrush", m ? 0x4D3A3A3A : 0xFF272727);
                Set(r, "CardBrush", m ? 0x0DFFFFFF : 0xFF2B2B2B);
                Set(r, "CardBorderBrush", 0x19FFFFFF);
                Set(r, "PopupBrush", 0xFF2C2C2C);
                Set(r, "FooterBrush", m ? 0x33000000 : 0xFF202020);
                Set(r, "ControlBrush", 0x0FFFFFFF);
                Set(r, "ControlBorderBrush", 0x17FFFFFF);
                Set(r, "InputBrush", 0x0FFFFFFF);
                Set(r, "InputHoverBrush", 0x15FFFFFF);
                Set(r, "InputFocusBrush", 0xB31E1E1E);
                Set(r, "TextBrush", 0xFFFFFFFF);
                Set(r, "SubTextBrush", 0xFFC5C5C5);
                Set(r, "AccentBrush", 0xFF60CDFF);
                Set(r, "AccentSoftBrush", 0x2660CDFF);
                Set(r, "AccentTextBrush", 0xFF000000);
                Set(r, "HoverBrush", 0x0FFFFFFF);
                Set(r, "PressedBrush", 0x08FFFFFF);
                Set(r, "NavSelectedBrush", 0x0FFFFFFF);
                Set(r, "ScrollThumbBrush", 0x5CFFFFFF);
                Set(r, "DropStrokeBrush", 0x40FFFFFF);
                Set(r, "SuccessBrush", 0xFF6CCB5F);
                Set(r, "SuccessBgBrush", 0xFF393D1B);
                Set(r, "ErrorBrush", 0xFFFF99A4);
                Set(r, "ErrorBgBrush", 0xFF442726);
            }
            else
            {
                Set(r, "WindowBrush", m ? 0x00000000 : 0xFFF3F3F3);
                Set(r, "LayerBrush", m ? 0x80FFFFFF : 0xFFF9F9F9);
                Set(r, "CardBrush", m ? 0xB3FFFFFF : 0xFFFFFFFF);
                Set(r, "CardBorderBrush", 0x0F000000);
                Set(r, "PopupBrush", 0xFFF9F9F9);
                Set(r, "FooterBrush", m ? 0x0A000000 : 0xFFF3F3F3);
                Set(r, "ControlBrush", 0xB3FFFFFF);
                Set(r, "ControlBorderBrush", 0x24000000);
                Set(r, "InputBrush", 0xB3FFFFFF);
                Set(r, "InputHoverBrush", 0xFFF9F9F9);
                Set(r, "InputFocusBrush", 0xFFFFFFFF);
                Set(r, "TextBrush", 0xE4000000);
                Set(r, "SubTextBrush", 0x9E000000);
                Set(r, "AccentBrush", 0xFF005FB8);
                Set(r, "AccentSoftBrush", 0x1A005FB8);
                Set(r, "AccentTextBrush", 0xFFFFFFFF);
                Set(r, "HoverBrush", 0x09000000);
                Set(r, "PressedBrush", 0x06000000);
                Set(r, "NavSelectedBrush", 0x09000000);
                Set(r, "ScrollThumbBrush", 0x72000000);
                Set(r, "DropStrokeBrush", 0x40000000);
                Set(r, "SuccessBrush", 0xFF0F7B0F);
                Set(r, "SuccessBgBrush", 0xFFDFF6DD);
                Set(r, "ErrorBrush", 0xFFC42B1C);
                Set(r, "ErrorBgBrush", 0xFFFDE7E9);
            }
            foreach (Window w in Application.Current.Windows) ApplyWindow(w);
        }

        static void Set(ResourceDictionary r, string key, uint argb)
        {
            var b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
            b.Freeze();
            r[key] = b;
        }

        // 창마다 한 번 (SourceInitialized 이후) 그리고 테마가 바뀔 때마다 부른다
        public static void ApplyWindow(Window w)
        {
            IntPtr h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) return;
            int dark = Dark ? 1 : 0;
            DwmSetWindowAttribute(h, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

            bool mica = false;
            if (MicaSupported)
            {
                int backdrop = DWMSBT_MAINWINDOW;
                if (DwmSetWindowAttribute(h, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) == 0)
                {
                    var src = HwndSource.FromHwnd(h);
                    if (src != null && src.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;
                    var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                    mica = DwmExtendFrameIntoClientArea(h, ref margins) == 0;
                }
            }
            // Mica를 못 쓰면 단색 배경으로
            if (MicaSupported && !mica)
                w.Background = new SolidColorBrush(Dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3));
        }

        // XAML 창을 띄우기 전에 부른다
        public static void Attach(Window w)
        {
            w.SourceInitialized += delegate { ApplyWindow(w); };
        }
    }
}
