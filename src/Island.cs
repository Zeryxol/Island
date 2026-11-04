using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Path = System.Windows.Shapes.Path;
using WF = System.Windows.Forms;

namespace Island
{
    static class Native
    {
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] public static extern bool LockWorkStation();
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
        [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr h);
        [DllImport("user32.dll")] public static extern bool RegisterShellHookWindow(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterWindowMessage(string s);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

        [StructLayout(LayoutKind.Sequential)]
        public class MEMSTATUS
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMSTATUS));
            public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx([In, Out] MEMSTATUS m);

        public static void Key(byte vk)
        {
            keybd_event(vk, 0, 1, UIntPtr.Zero);
            keybd_event(vk, 0, 3, UIntPtr.Zero);
        }
    }

    static class UI
    {
        // theme colors, set once at startup by SetTheme
        public static Brush Bg, Fg, Gray;
        public static Color BgColor, FgColor, Card, CardHover, Track, Line, Dim;
        public static readonly Color Blue = Color.FromRgb(10, 132, 255);
        public static readonly Color Orange = Color.FromRgb(255, 159, 10);
        public static readonly Color Green = Color.FromRgb(48, 209, 88);
        public static readonly Color Red = Color.FromRgb(255, 69, 58);
        public static readonly Color Purple = Color.FromRgb(191, 90, 242);
        public static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        static Brush Freeze(Brush b) { b.Freeze(); return b; }
        static UI() { SetTheme("dark"); }

        public static void SetTheme(string name)
        {
            byte[] v;
            if (name == "light") v = new byte[] { 246, 246, 248, 20, 20, 22, 110, 110, 116, 228, 228, 233, 212, 212, 218, 216, 216, 222, 200, 200, 207, 180, 180, 188 };
            else if (name == "gray") v = new byte[] { 46, 46, 50, 255, 255, 255, 163, 163, 170, 62, 62, 67, 80, 80, 86, 72, 72, 78, 88, 88, 95, 104, 104, 111 };
            else v = new byte[] { 0, 0, 0, 255, 255, 255, 142, 142, 147, 28, 28, 30, 50, 50, 53, 44, 44, 46, 58, 58, 60, 72, 72, 74 };
            Func<int, Color> c = i => Color.FromRgb(v[i], v[i + 1], v[i + 2]);
            BgColor = c(0); FgColor = c(3); Card = c(9); CardHover = c(12); Track = c(15); Line = c(18); Dim = c(21);
            Bg = Freeze(new SolidColorBrush(BgColor)); Fg = Freeze(new SolidColorBrush(FgColor)); Gray = Freeze(new SolidColorBrush(c(6)));
        }

        public static TextBlock Text(string s, double size, Brush fg, FontWeight? w = null)
        {
            return new TextBlock { Text = L.T(s), FontSize = size, Foreground = fg, FontWeight = w ?? FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis };
        }

        public static TextBlock Glyph(string g, double size, Brush fg)
        {
            return new TextBlock { Text = g, FontFamily = Icons, FontSize = size, Foreground = fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }

        public static void Ease(DependencyObject o, DependencyProperty p, double to, int ms, int delay = 0)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, BeginTime = TimeSpan.FromMilliseconds(delay) };
            ((IAnimatable)o).BeginAnimation(p, a);
        }

        // rounded surface with hover lift / press squash
        public static Border Pressable(UIElement child, double w, double h, double radius, Action click)
        {
            var bg = new SolidColorBrush(Card);
            var scale = new ScaleTransform(1, 1);
            var b = new Border
            {
                Width = w, Height = h, CornerRadius = new CornerRadius(radius), Background = bg, Cursor = Cursors.Hand,
                RenderTransform = scale, RenderTransformOrigin = new Point(0.5, 0.5), Child = child
            };
            Action<double, Color, int> to = (s, c, ms) =>
            {
                Ease(scale, ScaleTransform.ScaleXProperty, s, ms);
                Ease(scale, ScaleTransform.ScaleYProperty, s, ms);
                bg.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(c, TimeSpan.FromMilliseconds(ms)));
            };
            b.MouseEnter += (s, e) => to(1.06, CardHover, 160);
            b.MouseLeave += (s, e) => to(1.0, Card, 220);
            b.MouseLeftButtonDown += (s, e) => to(0.92, CardHover, 90);
            b.MouseLeftButtonUp += (s, e) => { to(1.06, CardHover, 160); if (click != null) try { click(); } catch { } };
            return b;
        }

        public static ScrollViewer HScroll(UIElement content)
        {
            var sv = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = content };
            sv.PreviewMouseWheel += (s, e) => { sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta * 0.6); e.Handled = true; };
            return sv;
        }

        public static ImageSource FileIcon(string path)
        {
            try
            {
                var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif")
                {
                    var bi = new BitmapImage();
                    bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.DecodePixelWidth = 80; bi.UriSource = new Uri(path); bi.EndInit();
                    return bi;
                }
                if (File.Exists(path))
                    using (var ic = System.Drawing.Icon.ExtractAssociatedIcon(path))
                        return Imaging.CreateBitmapSourceFromHIcon(ic.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            catch { }
            return null;
        }
    }

    class Gauge : Grid
    {
        readonly Path arc;
        readonly TextBlock value;
        const double S = 50, R = 21, T = 5;

        public Gauge(string caption, Color color)
        {
            var stack = new StackPanel();
            var canvas = new Grid { Width = S, Height = S };
            canvas.Children.Add(new Ellipse { Width = R * 2 + T, Height = R * 2 + T, Stroke = new SolidColorBrush(UI.Track), StrokeThickness = T });
            arc = new Path { Stroke = new SolidColorBrush(color), StrokeThickness = T, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            canvas.Children.Add(arc);
            value = new TextBlock { Foreground = UI.Fg, FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            canvas.Children.Add(value);
            stack.Children.Add(canvas);
            var cap = UI.Text(caption, 11, UI.Gray); cap.HorizontalAlignment = HorizontalAlignment.Center; cap.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(cap);
            Children.Add(stack);
        }

        public void Set(double v, string text)
        {
            v = Math.Max(0.001, Math.Min(0.999, v));
            double c = S / 2, a = v * 2 * Math.PI;
            var fig = new PathFigure { StartPoint = new Point(c, c - R) };
            fig.Segments.Add(new ArcSegment(new Point(c + R * Math.Sin(a), c - R * Math.Cos(a)), new Size(R, R), 0, v > 0.5, SweepDirection.Clockwise, true));
            var g = new PathGeometry(); g.Figures.Add(fig);
            arc.Data = g;
            value.Text = text;
        }

        public void SetColor(Color c) { arc.Stroke = new SolidColorBrush(c); }
    }

    enum Mode { Collapsed, Notice, Expanded, Drop, Level, Call, Search, Pick }

    class ClipItem { public string Text; public ImageSource Image; public string ImagePath; }
    class ShelfItem { public string Path; public string Text; }

    partial class IslandWindow : Window
    {
        const double CW = 156, CH = 36, NW = 360, NH = 60, EW = 560, EH = 272, DW = 420, DH = 110;

        Border pill;
        TranslateTransform shake = new TranslateTransform();
        Grid layerCollapsed, layerNotice, layerExpanded, layerDrop;
        ScaleTransform expScale = new ScaleTransform(0.94, 0.94);
        Mode mode = Mode.Collapsed;
        bool hovering, dragOut;

        TextBlock miniTime, miniRight, bigTime, bigDate, weatherLine;
        StackPanel clockPanel, gaugePanel;
        string weatherShown;
        Ellipse miniDot;
        Gauge gCpu, gRam, gBat, gPing;

        Border noticeIcon; TextBlock noticeTitle, noticeSub;
        DispatcherTimer noticeTimer, leaveTimer, dropLeaveTimer, slow, fast;

        // tabs
        Grid pageHost; Border tabIndicator; readonly List<TextBlock> tabLabels = new List<TextBlock>();
        readonly List<FrameworkElement> pages = new List<FrameworkElement>(); int tab;

        // clipboard
        readonly List<ClipItem> clips = new List<ClipItem>();
        StackPanel clipList; TextBlock clipEmpty; int suppressClip;

        // shelf
        readonly List<ShelfItem> shelf = new List<ShelfItem>();
        StackPanel shelfList; TextBlock shelfEmpty;

        // timer
        DateTime timerEnd; TimeSpan timerTotal; bool timerOn;
        TextBlock timerText; Border timerBar;

        long lIdle, lKernel, lUser;
        bool lastCaps, lastAc; bool powerKnown;
        static readonly CultureInfo Ru = new CultureInfo("ru-RU");

        public IslandWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Width = EW + 80; Height = EH + 50;
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = 0;
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");

            pill = new Border
            {
                Background = UI.Bg, Width = CW, Height = CH, CornerRadius = new CornerRadius(CH / 2),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 7, 0, 0), ClipToBounds = true, RenderTransform = shake, AllowDrop = true,
                Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 4, Direction = 270, Opacity = 0.35, Color = Colors.Black }
            };
            pill.SizeChanged += (s, e) => pill.CornerRadius = new CornerRadius(Math.Min(pill.ActualHeight / 2, 32));
            var root = new Grid();
            pill.Child = root;

            root.Children.Add(layerCollapsed = BuildCollapsed());
            root.Children.Add(layerNotice = BuildNotice());
            root.Children.Add(layerExpanded = BuildExpanded());
            root.Children.Add(layerDrop = BuildDrop());
            foreach (var l in new[] { layerNotice, layerExpanded, layerDrop }) { l.Opacity = 0; l.IsHitTestVisible = false; }
            InitFeatures(root);
            InitMore();
            InitSearch(root);
            InitPicker(root);
            InitPlacement();
            Weather.Start();
            Content = pill;

            pill.MouseEnter += (s, e) =>
            {
                hovering = true; leaveTimer.Stop();
                // a message notice stays put under the cursor so it can be clicked
                if (mode == Mode.Call || mode == Mode.Search || mode == Mode.Pick) return;
                if (mode == Mode.Notice && (noticeTarget != IntPtr.Zero || noticeAction != null)) { noticeTimer.Stop(); return; }
                if (mode != Mode.Expanded && mode != Mode.Drop) SetMode(Mode.Expanded);
            };
            pill.MouseLeave += (s, e) =>
            {
                hovering = false;
                if (mode == Mode.Notice) { noticeTimer.Interval = TimeSpan.FromMilliseconds(1200); noticeTimer.Start(); }
                else leaveTimer.Start();
            };
            pill.DragEnter += OnDragEnter;
            pill.DragOver += OnDragEnter;
            pill.DragLeave += (s, e) => dropLeaveTimer.Start();
            pill.Drop += OnDrop;

            leaveTimer = Timer(380, () => { leaveTimer.Stop(); if (mode == Mode.Expanded && !typing && !SettingsOpen) SetMode(Mode.Collapsed); });
            dropLeaveTimer = Timer(150, () => { dropLeaveTimer.Stop(); if (mode == Mode.Drop) SetMode(Mode.Collapsed); });
            noticeTimer = Timer(2200, () => { noticeTimer.Stop(); if (mode == Mode.Notice || mode == Mode.Level || mode == Mode.Call) { callButtons.Visibility = Visibility.Collapsed; noticeTarget = IntPtr.Zero; SetMode(hovering ? Mode.Expanded : Mode.Collapsed); } });
            slow = Timer(1000, () => { UpdateStats(); SlowFeatures(); }); slow.Start();
            fast = Timer(200, () => { FastTick(); FastFeatures(); }); fast.Start();

            Native.GetSystemTimes(out lIdle, out lKernel, out lUser);
            lastCaps = (Native.GetKeyState(0x14) & 1) != 0;
            UpdateStats();
            FastTick();

            SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE: no alt-tab, no focus stealing
                Native.SetWindowLong(h, -20, Native.GetWindowLong(h, -20) | 0x80 | 0x08000000);
                Native.AddClipboardFormatListener(h);
                HwndSource.FromHwnd(h).AddHook(WndProc);
                WatchToasts();
            };
            layerNotice.MouseLeftButtonUp += (s, e) =>
            {
                if (mode == Mode.Call) return; // calls are answered with their buttons
                if (noticeAction != null) { var a = noticeAction; noticeAction = null; try { a(); } catch { } SetMode(Mode.Collapsed); return; }
                if (noticeTarget == IntPtr.Zero) return;
                if (Native.IsIconic(noticeTarget)) Native.ShowWindow(noticeTarget, 9);
                Native.SetForegroundWindow(noticeTarget);
                noticeTarget = IntPtr.Zero;
                SetMode(Mode.Collapsed);
            };
            Loaded += (s, e) => Intro();
        }

        static DispatcherTimer Timer(int ms, Action a)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            if (a != null) t.Tick += (s, e) => a();
            return t;
        }

        // ---------- layers ----------

        Grid BuildCollapsed()
        {
            var g = new Grid { Margin = new Thickness(16, 0, 14, 0), Height = CH, VerticalAlignment = VerticalAlignment.Top };
            miniTime = UI.Text("", 13, UI.Fg, FontWeights.SemiBold); miniTime.VerticalAlignment = VerticalAlignment.Center;
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            miniDot = new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(UI.Green), Margin = new Thickness(0, 1, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            miniRight = UI.Text("", 12, UI.Fg, FontWeights.SemiBold);
            right.Children.Add(miniDot); right.Children.Add(miniRight);
            g.Children.Add(miniTime); g.Children.Add(right);
            return g;
        }

        Grid BuildNotice()
        {
            var g = new Grid { Width = NW, Height = NH, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
            var row = new DockPanel { Margin = new Thickness(12, 10, 22, 10) };
            noticeIcon = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), ClipToBounds = true, Margin = new Thickness(0, 0, 12, 0) };
            DockPanel.SetDock(noticeIcon, Dock.Left);
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            noticeTitle = UI.Text("", 14, UI.Fg, FontWeights.SemiBold);
            noticeSub = UI.Text("", 12, UI.Gray);
            texts.Children.Add(noticeTitle); texts.Children.Add(noticeSub);
            row.Children.Add(noticeIcon); row.Children.Add(texts);
            g.Children.Add(row);
            return g;
        }

        TranslateTransform dropBob;

        Grid BuildDrop()
        {
            var g = new Grid { Width = DW, Height = DH, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
            var frame = new Border { Margin = new Thickness(12), CornerRadius = new CornerRadius(22) };
            var dash = new Rectangle { RadiusX = 22, RadiusY = 22, Stroke = new SolidColorBrush(UI.Dim), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 } };
            var inner = new Grid(); inner.Children.Add(dash);
            var st = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var arrow = UI.Glyph("", 22, new SolidColorBrush(UI.Blue));
            dropBob = new TranslateTransform(); arrow.RenderTransform = dropBob;
            var txt = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
            txt.Children.Add(UI.Text("Положить на полку", 15, UI.Fg, FontWeights.SemiBold));
            txt.Children.Add(UI.Text("Отпустите файлы или текст", 12, UI.Gray));
            st.Children.Add(arrow); st.Children.Add(txt);
            inner.Children.Add(st);
            frame.Child = inner;
            g.Children.Add(frame);
            return g;
        }

        Grid BuildExpanded()
        {
            var outer = new Grid { Width = EW, Height = EH, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, RenderTransform = expScale, RenderTransformOrigin = new Point(0.5, 0) };
            var g = new Grid { Margin = new Thickness(26, 20, 26, 18) };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition());

            var clock = new StackPanel();
            bigTime = UI.Text("", 44, UI.Fg, FontWeights.Light); bigTime.Margin = new Thickness(-2, -8, 0, 0);
            bigDate = UI.Text("", 14, UI.Gray); bigDate.Margin = new Thickness(0, -2, 0, 0);
            weatherLine = new TextBlock { FontSize = 13, Margin = new Thickness(0, 3, 0, 0), Visibility = Visibility.Collapsed };
            clock.Children.Add(bigTime); clock.Children.Add(bigDate); clock.Children.Add(weatherLine);
            clockPanel = clock;
            g.Children.Add(clock);

            var gauges = gaugePanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            gCpu = new Gauge("ЦП", UI.Blue); gRam = new Gauge("ОЗУ", UI.Orange); gBat = new Gauge("Питание", UI.Green);
            gRam.Margin = new Thickness(14, 0, 14, 0);
            gPing = new Gauge("Пинг", UI.Green); gPing.Margin = new Thickness(14, 0, 0, 0);
            var gaugeById = new Dictionary<string, Gauge> { { "cpu", gCpu }, { "ram", gRam }, { "bat", gBat }, { "ping", gPing } };
            foreach (var id in Settings.Shown("gauges")) { var ga = gaugeById[id]; ga.Margin = new Thickness(gauges.Children.Count > 0 ? 14 : 0, 0, 0, 0); gauges.Children.Add(ga); }
            g.Children.Add(gauges);

            // segmented tabs with sliding indicator
            const double TabW = 96;
            var tabIds = Settings.Shown("tabs");
            if (tabIds.Count == 0) tabIds.Add("actions");
            var tabs = new Grid { Height = 30, Width = TabW * tabIds.Count + 6, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 12) };
            tabs.Children.Add(new Border { CornerRadius = new CornerRadius(15), Background = new SolidColorBrush(UI.Card) });
            tabIndicator = new Border { Width = TabW, Margin = new Thickness(3), CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(UI.Line), HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = new TranslateTransform() };
            tabs.Children.Add(tabIndicator);
            var labels = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(3, 0, 3, 0) };
            var names = tabIds.Select(id => Settings.Name(id)).ToArray();
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                var t = UI.Text(names[i], 12.5, i == 0 ? UI.Fg : UI.Gray, FontWeights.SemiBold);
                t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center;
                var hit = new Border { Width = TabW, Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = t };
                hit.MouseLeftButtonUp += (s, e) => SelectTab(idx);
                tabLabels.Add(t);
                labels.Children.Add(hit);
            }
            tabs.Children.Add(labels);
            Grid.SetRow(tabs, 1);
            g.Children.Add(tabs);

            pageHost = new Grid { ClipToBounds = true };
            Grid.SetRow(pageHost, 2);
            // every page is built so the features behind it keep working; only the chosen ones get a tab
            var pageById = new Dictionary<string, FrameworkElement> { { "actions", BuildActions() }, { "clip", BuildClipPage() }, { "shelf", BuildShelfPage() }, { "timer", BuildTimerPage() }, { "note", BuildNotePage() } };
            foreach (var id in tabIds) pages.Add(pageById[id]);
            for (int i = 0; i < pages.Count; i++)
            {
                pages[i].RenderTransform = new TranslateTransform();
                if (i > 0) { pages[i].Opacity = 0; pages[i].Visibility = Visibility.Hidden; }
                pageHost.Children.Add(pages[i]);
            }
            g.Children.Add(pageHost);
            outer.Children.Add(g);
            return outer;
        }

        void SelectTab(int idx)
        {
            if (idx == tab) return;
            int dir = idx > tab ? 1 : -1;
            var oldP = pages[tab]; var newP = pages[idx];
            UI.Ease(tabIndicator.RenderTransform, TranslateTransform.XProperty, idx * 96, 320);
            tabLabels[tab].Foreground = UI.Gray; tabLabels[idx].Foreground = UI.Fg;

            UI.Ease(oldP, OpacityProperty, 0, 160);
            UI.Ease(oldP.RenderTransform, TranslateTransform.XProperty, -30 * dir, 220);
            var hide = oldP;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(170) };
            t.Tick += (s, e) => { t.Stop(); if (pages[tab] != hide) hide.Visibility = Visibility.Hidden; };
            t.Start();

            newP.Visibility = Visibility.Visible;
            ((TranslateTransform)newP.RenderTransform).X = 30 * dir;
            UI.Ease(newP, OpacityProperty, 1, 260, 60);
            UI.Ease(newP.RenderTransform, TranslateTransform.XProperty, 0, 360, 40);
            tab = idx;
        }

        FrameworkElement BuildActions()
        {
            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var media = BuildMediaCard();
            Action keepOpen = ShowSettings;
            Func<string, string, Action, UIElement> make = (glyph, tip, act) =>
            {
                var b = UI.Pressable(UI.Glyph(glyph, 17, UI.Fg), 46, 46, 23, () => { act(); if (act != keepOpen) SetMode(Mode.Collapsed); });
                b.ToolTip = L.T(tip); b.Margin = new Thickness(4, 0, 4, 0);
                return b;
            };
            var items = new Dictionary<string, Func<UIElement>>
            {
                { "media", () => media },
                { "picker", () => make("\uEF3C", "Пипетка - взять цвет с экрана", () => Dispatcher.BeginInvoke(new Action(StartPicker), DispatcherPriority.Background)) },
                { "shot", () => make("\uE722", "Скриншот области", () => Process.Start("ms-screenclip:")) },
                { "winset", () => make("\uE770", "Параметры Windows", () => Process.Start("ms-settings:")) },
                { "lock", () => make("\uE72E", "Заблокировать", () => Native.LockWorkStation()) },
                { "island", () => make("\uE713", "Настройки", keepOpen) },
            };
            foreach (var id in Settings.Shown("buttons")) bar.Children.Add(items[id]());
            return bar;
        }
        FrameworkElement BuildClipPage()
        {
            var g = new Grid();
            clipList = new StackPanel { Orientation = Orientation.Horizontal };
            g.Children.Add(UI.HScroll(clipList));
            clipEmpty = UI.Text("Скопируйте что-нибудь - история появится здесь", 13, UI.Gray);
            clipEmpty.HorizontalAlignment = HorizontalAlignment.Center; clipEmpty.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(clipEmpty);
            return g;
        }

        FrameworkElement BuildShelfPage()
        {
            var g = new Grid();
            shelfList = new StackPanel { Orientation = Orientation.Horizontal };
            g.Children.Add(UI.HScroll(shelfList));
            shelfEmpty = UI.Text("Перетащите файлы на островок · ПКМ по элементу - убрать", 13, UI.Gray);
            shelfEmpty.HorizontalAlignment = HorizontalAlignment.Center; shelfEmpty.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(shelfEmpty);
            return g;
        }

        FrameworkElement BuildTimerPage()
        {
            var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 172 };
            // idle: three editable fields hh:mm:ss with fixed colons; running: the countdown text
            timerText = UI.Text("", 34, UI.Fg, FontWeights.Light);
            timerText.Visibility = Visibility.Collapsed;
            timerFields = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < 3; i++)
            {
                if (i > 0) { var colon = UI.Text(":", 34, UI.Gray, FontWeights.Light); colon.Margin = new Thickness(1, -3, 1, 0); timerFields.Children.Add(colon); }
                var box = TimeField(i);
                timerBoxes[i] = box;
                timerFields.Children.Add(box);
            }
            var timeHost = new Grid(); timeHost.Children.Add(timerText); timeHost.Children.Add(timerFields);
            var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(UI.Track), Margin = new Thickness(0, 4, 0, 0) };
            timerBar = new Border { Height = 4, Width = 0, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(UI.Orange), HorizontalAlignment = HorizontalAlignment.Left };
            var trackGrid = new Grid { Width = 110, HorizontalAlignment = HorizontalAlignment.Left }; trackGrid.Children.Add(track); trackGrid.Children.Add(timerBar);
            left.Children.Add(timeHost); left.Children.Add(trackGrid);
            g.Children.Add(left);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(btns, 1);
            var play = UI.Pressable(UI.Glyph("", 14, new SolidColorBrush(UI.Orange)), 40, 40, 20, () => { if (!timerOn) StartTimer(timerDraft); });
            play.ToolTip = L.T("Запустить выставленное время"); play.Margin = new Thickness(4, 0, 8, 0);
            btns.Children.Add(play);
            foreach (var m in new[] { 5, 15, 25 })
            {
                int mins = m;
                var b = UI.Pressable(UI.Text(mins + " мин", 13, UI.Fg, FontWeights.SemiBold), 64, 40, 20, () => StartTimer(mins));
                ((TextBlock)b.Child).HorizontalAlignment = HorizontalAlignment.Center; ((TextBlock)b.Child).VerticalAlignment = VerticalAlignment.Center;
                b.Margin = new Thickness(4, 0, 4, 0);
                btns.Children.Add(b);
            }
            var stop = UI.Pressable(UI.Glyph("", 14, new SolidColorBrush(UI.Red)), 40, 40, 20, StopTimer);
            stop.ToolTip = L.T("Сбросить"); stop.Margin = new Thickness(8, 0, 0, 0);
            btns.Children.Add(stop);
            g.Children.Add(btns);
            return g;
        }

        // ---------- state machine ----------

        void SetMode(Mode m)
        {
            if (m == mode) return;
            var prev = mode; mode = m;
            dropBob.BeginAnimation(TranslateTransform.YProperty, m != Mode.Drop ? null : new DoubleAnimation(-3, 3, TimeSpan.FromMilliseconds(600)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            if (m != Mode.Expanded) HideSettings();
            double w = CW, h = CH; int ms = 380; IEasingFunction ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
            if (m == Mode.Expanded) { UpdateStats(); w = EW; h = EH; ms = 560; ease = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 6 }; }
            if (m == Mode.Notice) { w = NW; h = NH; ms = 520; ease = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 5 }; }
            if (m == Mode.Drop) { w = DW; h = DH; ms = 420; ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 }; }
            if (m == Mode.Level) { w = LW; h = LH; ms = 420; ease = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 5 }; }
            if (m == Mode.Call) { w = CallW; h = CallH; ms = 560; ease = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 5 }; }
            if (m == Mode.Search) { w = SW; h = SH; ms = 480; ease = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 6 }; }
            if (m == Mode.Pick) { w = LW; h = LH; ms = 420; ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 }; }
            Show(layerLevel, m == Mode.Level, 100);
            if (prev == Mode.Expanded && m == Mode.Collapsed) ms = 380;
            pill.BeginAnimation(WidthProperty, new DoubleAnimation(w, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
            pill.BeginAnimation(HeightProperty, new DoubleAnimation(h, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });

            Show(layerCollapsed, m == Mode.Collapsed, 120);
            Show(layerSearch, m == Mode.Search, 120);
            Show(layerPick, m == Mode.Pick, 100);
            Show(layerNotice, m == Mode.Notice || m == Mode.Call, 140);
            Show(layerDrop, m == Mode.Drop, 120);
            Show(layerExpanded, m == Mode.Expanded, 160);
            UI.Ease(expScale, ScaleTransform.ScaleXProperty, m == Mode.Expanded ? 1 : 0.94, 420);
            UI.Ease(expScale, ScaleTransform.ScaleYProperty, m == Mode.Expanded ? 1 : 0.94, 420);
        }

        static void Show(UIElement el, bool on, int delay)
        {
            el.IsHitTestVisible = on;
            UI.Ease(el, OpacityProperty, on ? 1 : 0, on ? 260 : 110, on ? delay : 0);
        }

        void Notify(string glyph, Color color, string title, string sub, ImageSource img = null, int ms = 2200)
        {
            if (mode == Mode.Expanded || mode == Mode.Drop || fullscreen) return;
            if (mode == Mode.Call || mode == Mode.Search || mode == Mode.Pick) return; // don't interrupt a call or typing
            noticeTarget = IntPtr.Zero;
            noticeAction = null;
            layerNotice.Cursor = null;
            if (img != null)
                noticeIcon.Child = new Image { Source = img, Stretch = Stretch.UniformToFill };
            else
                noticeIcon.Child = UI.Glyph(glyph, 18, new SolidColorBrush(color));
            noticeIcon.Background = new SolidColorBrush(img != null ? UI.Card : Color.FromArgb(46, color.R, color.G, color.B));
            noticeTitle.Text = L.T(title);
            noticeSub.Text = L.T(sub ?? "");
            noticeSub.Visibility = string.IsNullOrEmpty(sub) ? Visibility.Collapsed : Visibility.Visible;

            // pop the icon in
            var sc = new ScaleTransform(0.4, 0.4); noticeIcon.RenderTransform = sc; noticeIcon.RenderTransformOrigin = new Point(0.5, 0.5);
            var pop = new ElasticEase { Oscillations = 1, Springiness = 4, EasingMode = EasingMode.EaseOut };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(600)) { EasingFunction = pop, BeginTime = TimeSpan.FromMilliseconds(80) });
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(600)) { EasingFunction = pop, BeginTime = TimeSpan.FromMilliseconds(80) });

            SetMode(Mode.Notice);
            noticeTimer.Stop(); noticeTimer.Interval = TimeSpan.FromMilliseconds(ms); noticeTimer.Start();
        }

        void Shake()
        {
            var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(520) };
            double[] xs = { 0, -10, 9, -7, 5, -3, 1, 0 };
            for (int i = 0; i < xs.Length; i++) a.KeyFrames.Add(new EasingDoubleKeyFrame(xs[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(i * 65))));
            shake.BeginAnimation(TranslateTransform.XProperty, a);
        }

        void Intro()
        {
            pill.Opacity = 0;
            pill.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(300)));
            var e = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 5 };
            pill.BeginAnimation(WidthProperty, new DoubleAnimation(40, CW, TimeSpan.FromMilliseconds(800)) { EasingFunction = e });
            pill.BeginAnimation(HeightProperty, new DoubleAnimation(20, CH, TimeSpan.FromMilliseconds(800)) { EasingFunction = e });
        }

        // ---------- clipboard ----------

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == 0x031D)
                Dispatcher.BeginInvoke(new Action(() => ReadClipboard(3)), DispatcherPriority.Background);
            return IntPtr.Zero;
        }

        // ---------- app notifications ----------

        IntPtr noticeTarget;
        readonly Dictionary<string, ImageSource> appIcons = new Dictionary<string, ImageSource>();

        static readonly Dictionary<string, string> KnownApps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "discord", "Discord" }, { "discordcanary", "Discord" }, { "discordptb", "Discord" },
            { "telegram", "Telegram" }, { "ayugram", "Telegram" }, { "whatsapp", "WhatsApp" }, { "whatsapp.root", "WhatsApp" },
            { "slack", "Slack" }, { "teams", "Teams" }, { "ms-teams", "Teams" }, { "viber", "Viber" }, { "skype", "Skype" },
            { "steamwebhelper", "Steam" }, { "vk", "VK Мессенджер" }, { "signal", "Signal" }, { "zoom", "Zoom" }
        };

        void ShowAppNotice(string app, string text, string exePath, IntPtr target)
        {
            if (mode == Mode.Drop || dnd) return;
            if (mode == Mode.Expanded && hovering) return;
            ImageSource icon = null;
            if (exePath != null && !appIcons.TryGetValue(exePath, out icon)) { icon = UI.FileIcon(exePath); appIcons[exePath] = icon; }
            if (mode == Mode.Expanded) mode = Mode.Collapsed;
            Notify("", UI.Blue, app, text, icon, 4500);
            noticeTarget = target;
            layerNotice.Cursor = target != IntPtr.Zero ? Cursors.Hand : null;
            if (noticeIcon.Child is Image) { noticeIcon.Background = Brushes.Transparent; ((Image)noticeIcon.Child).Margin = new Thickness(4); ((Image)noticeIcon.Child).Stretch = Stretch.Uniform; }        }

        // mirror Windows toasts (Discord, Telegram, ...) via the system notification listener
        void WatchToasts()
        {
            var thread = new System.Threading.Thread(() =>
            {
                Windows.UI.Notifications.Management.UserNotificationListener listener;
                try
                {
                    listener = Windows.UI.Notifications.Management.UserNotificationListener.Current;
                    toastListener = listener;
                    if (listener.GetAccessStatus() != Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Allowed)
                        Await(listener.RequestAccessAsync());
                }
                catch { return; }
                var seen = new HashSet<uint>();
                bool first = true;
                while (true)
                {
                    try
                    {
                        var list = Await(listener.GetNotificationsAsync(Windows.UI.Notifications.NotificationKinds.Toast));
                        foreach (var n in list)
                        {
                            if (!seen.Add(n.Id) || first) continue;
                            string app = "Уведомление";
                            try { app = n.AppInfo.DisplayInfo.DisplayName; } catch { }
                            var texts = new List<string>();
                            var binding = n.Notification.Visual.GetBinding(Windows.UI.Notifications.KnownNotificationBindings.ToastGeneric);
                            if (binding != null)
                                foreach (var t in binding.GetTextElements())
                                    if (!string.IsNullOrWhiteSpace(t.Text)) texts.Add(t.Text.Trim().Replace("\n", " "));
                            if (texts.Count == 0) continue;
                            // first line is usually the sender / chat, the rest is the message
                            string title = texts.Count > 1 ? texts[0] : app;
                            string body = texts.Count > 1 ? string.Join(" ", texts.Skip(1)) : texts[0];
                            var appName = app;
                            if (dnd)
                            {
                                // Windows already keeps it silent; remember it for the summary
                                Dispatcher.BeginInvoke(new Action(() => missed.Add(new[] { appName, title, body })));
                                continue;
                            }
                            var nid = n.Id;
                            Dispatcher.BeginInvoke(new Action(() => OnToast(appName, title, body, nid)));
                        }
                    }
                    catch { }
                    first = false;
                    System.Threading.Thread.Sleep(700);
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        static T Await<T>(Windows.Foundation.IAsyncOperation<T> op)
        {
            while (op.Status == Windows.Foundation.AsyncStatus.Started) System.Threading.Thread.Sleep(15);
            return op.GetResults();
        }

        void OnToast(string app, string title, string body, uint id)
        {
            IntPtr target = IntPtr.Zero; string path = null;
            foreach (var kv in KnownApps)
                if (app.IndexOf(kv.Value, StringComparison.OrdinalIgnoreCase) >= 0)
                    foreach (var p in Process.GetProcessesByName(kv.Key))
                        if (p.MainWindowHandle != IntPtr.Zero) { target = p.MainWindowHandle; try { path = p.MainModule.FileName; } catch { } break; }
            if (LooksLikeCall(title, body)) { ShowCall(app, title, body, path, target, id); return; }
            ShowAppNotice(title, title == app ? body : app + "  ·  " + body, path, target);
        }

        void ReadClipboard(int tries)
        {
            if (suppressClip > 0) { suppressClip--; return; }
            try
            {
                if (Clipboard.ContainsText())
                {
                    var t = Clipboard.GetText();
                    if (string.IsNullOrWhiteSpace(t)) return;
                    AddClip(new ClipItem { Text = t });
                    if (SmartCopy(t)) return;
                    var oneLine = t.Trim().Replace("\r", " ").Replace("\n", " ");
                    Notify("", UI.Blue, "Скопировано", (oneLine.Length > 60 ? oneLine.Substring(0, 60) + "…" : oneLine) + "   ·   " + t.Length + " симв.");
                }
                else if (Clipboard.ContainsImage())
                {
                    var img = Clipboard.GetImage();
                    if (img == null) return;
                    // keep the full picture on disk and only a small preview in memory
                    var clip = SaveClipImage(img);
                    if (clip == null) return;
                    AddClip(clip);
                    Notify(null, UI.Blue, "Изображение скопировано", img.PixelWidth + " × " + img.PixelHeight, clip.Image);
                }
                else if (Clipboard.ContainsFileDropList())
                {
                    var files = Clipboard.GetFileDropList();
                    Notify("", UI.Blue, files.Count == 1 ? "Файл скопирован" : "Скопировано файлов: " + files.Count, files.Count > 0 ? System.IO.Path.GetFileName(files[0]) : null);
                }
            }
            catch
            {
                if (tries > 0)
                {
                    var t = Timer(120, null); t.Tick += (s, e) => { t.Stop(); ReadClipboard(tries - 1); }; t.Start();
                }
            }
        }

        void AddClip(ClipItem c)
        {
            if (c.Text != null) clips.RemoveAll(x => x.Text == c.Text);
            clips.Insert(0, c);
            while (clips.Count > 30) { DeleteClipFile(clips[clips.Count - 1]); clips.RemoveAt(clips.Count - 1); }
            RenderClips();
            SaveClips();
        }

        void RenderClips()
        {
            clipList.Children.Clear();
            clipEmpty.Visibility = clips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var c in clips)
            {
                var item = c;
                UIElement inner;
                if (item.Image != null)
                    inner = new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Margin = new Thickness(6), Child = new Image { Source = item.Image, Stretch = Stretch.UniformToFill } };
                else
                {
                    var tb = new TextBlock { Text = item.Text.Trim(), Foreground = UI.Fg, FontSize = 12, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 9, 12, 9) };
                    inner = tb;
                }
                var card = UI.Pressable(inner, 132, 76, 16, () =>
                {
                    suppressClip++;
                    try { if (item.ImagePath != null) Clipboard.SetImage(LoadFull(item.ImagePath)); else Clipboard.SetText(item.Text); }
                    catch { suppressClip--; }
                    clips.Remove(item); clips.Insert(0, item); RenderClips(); SaveClips();
                    Flash("Снова в буфере");
                });
                card.ToolTip = L.T("Нажмите, чтобы скопировать снова");
                card.Margin = new Thickness(0, 0, 8, 0);
                card.MouseRightButtonUp += (s, e) => { e.Handled = true; clips.Remove(item); DeleteClipFile(item); RenderClips(); SaveClips(); };
                clipList.Children.Add(card);
            }
        }

        // short label over the tabs page when we're already expanded
        Border flashBadge; DispatcherTimer flashTimer;

        // one badge reused, so rapid updates (volume wheel) just change its text
        void Flash(string text)
        {
            if (flashBadge == null)
            {
                flashBadge = new Border { Background = new SolidColorBrush(UI.Line), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -42, 0, 0), Opacity = 0, IsHitTestVisible = false };
                flashBadge.Child = UI.Text("", 12, UI.Fg, FontWeights.SemiBold);
                pageHost.Children.Add(flashBadge);
                pageHost.ClipToBounds = false;
                flashTimer = Timer(1100, () => { flashTimer.Stop(); UI.Ease(flashBadge, OpacityProperty, 0, 250); });
            }
            ((TextBlock)flashBadge.Child).Text = text;
            UI.Ease(flashBadge, OpacityProperty, 1, 120);
            flashTimer.Stop(); flashTimer.Start();
        }

        // ---------- shelf ----------

        void OnDragEnter(object sender, DragEventArgs e)
        {
            dropLeaveTimer.Stop();
            if (dragOut || !(e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText)))
            { e.Effects = DragDropEffects.None; e.Handled = true; return; }
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            if (mode != Mode.Drop) { noticeTimer.Stop(); SetMode(Mode.Drop); }
        }

        void OnDrop(object sender, DragEventArgs e)
        {
            dropLeaveTimer.Stop();
            if (dragOut) return;
            int added = 0; string first = null;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null)
                foreach (var f in files)
                    if (!shelf.Any(x => x.Path == f)) { shelf.Add(new ShelfItem { Path = f }); added++; if (first == null) first = System.IO.Path.GetFileName(f); }
            if (files == null && e.Data.GetDataPresent(DataFormats.UnicodeText))
            {
                var t = (string)e.Data.GetData(DataFormats.UnicodeText);
                if (!string.IsNullOrWhiteSpace(t)) { shelf.Add(new ShelfItem { Text = t }); added++; first = "Текст"; }
            }
            RenderShelf();
            mode = Mode.Collapsed; // let Notify reshape from the drop zone
            if (added > 0)
                Notify("", UI.Purple, added == 1 ? "На полке: " + first : "На полку добавлено: " + added, "Всего на полке: " + shelf.Count + " · наведите → «Полка»");
            else SetMode(Mode.Collapsed);
        }

        void RenderShelf()
        {
            shelfList.Children.Clear();
            shelfEmpty.Visibility = shelf.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var s in shelf)
            {
                var item = s;
                var st = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
                bool isDir = item.Path != null && Directory.Exists(item.Path);
                var icon = item.Path != null && !isDir ? UI.FileIcon(item.Path) : null;
                if (icon != null) st.Children.Add(new Image { Source = icon, Width = 32, Height = 32, Stretch = Stretch.Uniform });
                else st.Children.Add(UI.Glyph(isDir ? "" : item.Text != null ? "" : "", 24, new SolidColorBrush(isDir ? Color.FromRgb(255, 204, 0) : UI.Purple)));
                var name = UI.Text(item.Path != null ? System.IO.Path.GetFileName(item.Path.TrimEnd('\\')) : item.Text.Trim(), 11, UI.Fg);
                name.HorizontalAlignment = HorizontalAlignment.Center; name.Margin = new Thickness(0, 5, 0, 0);
                st.Children.Add(name);

                var card = UI.Pressable(st, 92, 76, 16, null);
                card.ToolTip = (item.Path ?? item.Text) + "\nПеретащите наружу · двойной клик - открыть · ПКМ - убрать";
                card.Margin = new Thickness(0, 0, 8, 0);
                Point down = new Point(); bool pressed = false;
                card.PreviewMouseLeftButtonDown += (a, e) =>
                {
                    if (e.ClickCount == 2) { try { if (item.Path != null) Process.Start(item.Path); else { suppressClip++; Clipboard.SetText(item.Text); Flash("Текст скопирован"); } } catch { } return; }
                    pressed = true; down = e.GetPosition(this);
                };
                card.PreviewMouseLeftButtonUp += (a, e) => pressed = false;
                card.MouseMove += (a, e) =>
                {
                    if (!pressed || e.LeftButton != MouseButtonState.Pressed) return;
                    var p = e.GetPosition(this);
                    if (Math.Abs(p.X - down.X) + Math.Abs(p.Y - down.Y) < 6) return;
                    pressed = false;
                    var data = item.Path != null ? new DataObject(DataFormats.FileDrop, new[] { item.Path }) : new DataObject(DataFormats.UnicodeText, item.Text);
                    dragOut = true;
                    card.Opacity = 0.4;
                    var result = DragDrop.DoDragDrop(card, data, DragDropEffects.Copy | DragDropEffects.Move);
                    dragOut = false;
                    card.Opacity = 1;
                    if (result == DragDropEffects.Move) { shelf.Remove(item); RenderShelf(); }
                };
                card.MouseRightButtonUp += (a, e) =>
                {
                    e.Handled = true;
                    UI.Ease(card, OpacityProperty, 0, 150);
                    var t = Timer(150, null); t.Tick += (x, y) => { t.Stop(); shelf.Remove(item); RenderShelf(); }; t.Start();
                };
                shelfList.Children.Add(card);
            }
        }

        // ---------- timer ----------

        StackPanel timerFields;
        readonly TextBox[] timerBoxes = new TextBox[3];
        TimeSpan timerDraft = TimeSpan.FromMinutes(5);

        // one two-digit cell of hh:mm:ss: type digits, it jumps to the next cell; wheel changes it
        TextBox TimeField(int idx)
        {
            var b = new TextBox
            {
                Width = 46, MaxLength = 2, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = UI.Fg, CaretBrush = UI.Fg,
                FontSize = 34, FontWeight = FontWeights.Light, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center,
                SelectionBrush = new SolidColorBrush(UI.Orange), Cursor = Cursors.IBeam, ToolTip = idx == 0 ? "Часы" : idx == 1 ? "Минуты" : "Секунды"
            };
            b.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (b.IsKeyboardFocused) return;
                BeginTyping(b); b.SelectAll(); e.Handled = true;
            };
            b.PreviewTextInput += (s, e) => { e.Handled = !e.Text.All(char.IsDigit); };
            b.TextChanged += (s, e) =>
            {
                if (!b.IsKeyboardFocused) return;
                if (b.Text.Length == 2 && b.CaretIndex == 2 && idx < 2) { timerBoxes[idx + 1].Focus(); timerBoxes[idx + 1].SelectAll(); }
                DraftFromFields();
            };
            b.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { DraftFromFields(); EndTimerEdit(); if (timerDraft.TotalSeconds >= 1) StartTimer(timerDraft); e.Handled = true; }
                else if (e.Key == Key.Escape) { EndTimerEdit(); e.Handled = true; }
                else if (e.Key == Key.Back && b.Text.Length == 0 && idx > 0) { var p = timerBoxes[idx - 1]; p.Focus(); p.CaretIndex = p.Text.Length; e.Handled = true; }
                else if ((e.Key == Key.Right || e.Key == Key.OemSemicolon || e.Key == Key.Space) && b.CaretIndex == b.Text.Length && idx < 2) { timerBoxes[idx + 1].Focus(); timerBoxes[idx + 1].SelectAll(); e.Handled = true; }
                else if (e.Key == Key.Left && b.CaretIndex == 0 && idx > 0) { var p = timerBoxes[idx - 1]; p.Focus(); p.CaretIndex = p.Text.Length; e.Handled = true; }
                else if (e.Key == Key.Up || e.Key == Key.Down) { Nudge(idx, e.Key == Key.Up ? 1 : -1); e.Handled = true; }
            };
            b.LostKeyboardFocus += (s, e) =>
            {
                b.Text = (b.Text.Length == 0 ? "0" : b.Text).PadLeft(2, '0');
                // leaving the whole hh:mm:ss editor ends typing
                Dispatcher.BeginInvoke(new Action(() => { if (!timerBoxes.Any(x => x.IsKeyboardFocused)) EndTimerEdit(); }), DispatcherPriority.Input);
            };
            b.MouseWheel += (s, e) => { Nudge(idx, e.Delta > 0 ? 1 : -1); e.Handled = true; };
            return b;
        }

        void Nudge(int idx, int dir)
        {
            if (timerOn) return;
            var step = idx == 0 ? TimeSpan.FromHours(1) : idx == 1 ? TimeSpan.FromMinutes(1) : TimeSpan.FromSeconds(1);
            var d = timerDraft + (dir > 0 ? step : -step);
            if (d < TimeSpan.Zero) d = TimeSpan.Zero;
            if (d > new TimeSpan(99, 59, 59)) d = new TimeSpan(99, 59, 59);
            timerDraft = d;
            FieldsFromDraft();
        }

        void DraftFromFields()
        {
            int h, m, s;
            int.TryParse(timerBoxes[0].Text, out h); int.TryParse(timerBoxes[1].Text, out m); int.TryParse(timerBoxes[2].Text, out s);
            // 90 seconds typed is fine too: TimeSpan normalises it
            timerDraft = new TimeSpan(h, m, s);
        }

        void FieldsFromDraft()
        {
            var d = timerDraft;
            var v = new[] { ((int)d.TotalHours).ToString("00"), d.Minutes.ToString("00"), d.Seconds.ToString("00") };
            for (int i = 0; i < 3; i++) if (!timerBoxes[i].IsKeyboardFocused && timerBoxes[i].Text != v[i]) timerBoxes[i].Text = v[i];
        }

        void StartTimer(int minutes) { StartTimer(TimeSpan.FromMinutes(minutes)); }

        void StartTimer(TimeSpan d)
        {
            timerTotal = d;
            timerEnd = DateTime.Now + timerTotal;
            timerOn = true;
            Flash("Таймер на " + HumanDur(d));
            FastTick();
        }

        void EndTimerEdit()
        {
            if (!typing || editing != null || mode == Mode.Search) return;
            DraftFromFields();
            FieldsFromDraft();
            EndTyping();
        }

        static string FormatDur(TimeSpan t)
        {
            return t.TotalHours >= 1 ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds) : string.Format("{0:00}:{1:00}", (int)t.TotalMinutes, t.Seconds);
        }

        static string HumanDur(TimeSpan t)
        {
            var parts = new List<string>();
            if ((int)t.TotalHours > 0) parts.Add((int)t.TotalHours + " ч");
            if (t.Minutes > 0) parts.Add(t.Minutes + " мин");
            if (t.Seconds > 0) parts.Add(t.Seconds + " сек");
            return parts.Count > 0 ? string.Join(" ", parts) : "0 сек";
        }

        // "90" = 90 min, "1:30" = 1 min 30 s, "1:00:00", "45 сек", "2ч", "1ч 30м"
        static TimeSpan? ParseDuration(string s)
        {
            s = s.Trim().ToLowerInvariant().Replace(',', '.');
            if (s.Length == 0) return null;
            TimeSpan r;
            var colon = System.Text.RegularExpressions.Regex.Match(s, @"^(\d+):(\d{1,2})(?::(\d{1,2}))?$");
            if (colon.Success)
            {
                int a = int.Parse(colon.Groups[1].Value), b = int.Parse(colon.Groups[2].Value);
                r = colon.Groups[3].Success ? new TimeSpan(a, b, int.Parse(colon.Groups[3].Value)) : new TimeSpan(0, a, b);
            }
            else
            {
                double total = 0; bool any = false;
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(s, @"(\d+(?:\.\d+)?)\s*([a-zа-я]*)"))
                {
                    double v = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    var u = m.Groups[2].Value;
                    total += u.StartsWith("ч") || u.StartsWith("h") ? v * 3600 : u.StartsWith("с") || u.StartsWith("s") ? v : v * 60;
                    any = true;
                }
                if (!any) return null;
                r = TimeSpan.FromSeconds(total);
            }
            return r.TotalSeconds >= 1 && r.TotalHours <= 24 ? r : (TimeSpan?)null;
        }

        void StopTimer()
        {
            timerOn = false;
            FastTick();
        }

        void FastTick()
        {
            if (timerOn)
            {
                var left = timerEnd - DateTime.Now;
                if (left <= TimeSpan.Zero)
                {
                    timerOn = false;
                    SystemSounds.Asterisk.Play();
                    Notify("", UI.Orange, "Время вышло", "Таймер на " + HumanDur(timerTotal) + " завершён", null, 5000);
                    Shake();
                    left = TimeSpan.Zero;
                }
                var s = FormatDur(left);
                timerText.Text = string.Format("{0:00}:{1:00}:{2:00}", (int)left.TotalHours, left.Minutes, left.Seconds);
                timerText.Visibility = Visibility.Visible; timerFields.Visibility = Visibility.Collapsed;
                timerBar.Width = 110 * (1 - left.TotalSeconds / timerTotal.TotalSeconds);
                miniRight.Text = s; miniRight.Foreground = new SolidColorBrush(UI.Orange);
                miniDot.Fill = new SolidColorBrush(UI.Orange);
                miniDot.Visibility = Visibility.Visible;
            }
            else
            {
                timerText.Visibility = Visibility.Collapsed; timerFields.Visibility = Visibility.Visible;
                FieldsFromDraft(); timerBar.Width = 0;
                miniRight.Foreground = UI.Fg;
            }

            bool caps = (Native.GetKeyState(0x14) & 1) != 0;
            if (caps != lastCaps)
            {
                lastCaps = caps;
                Notify("", caps ? UI.Green : Color.FromRgb(142, 142, 147),
                    caps ? "Caps Lock включён" : "Caps Lock выключен", null, null, 1200);
            }
        }

        // ---------- stats ----------

        void UpdateStats()
        {
            var now = DateTime.Now;
            var fmt = (Settings.Clock24 ? "HH:mm" : "h:mm") + (Settings.Seconds ? ":ss" : "") + (Settings.Clock24 ? "" : " tt");
            miniTime.Text = now.ToString(fmt, CultureInfo.InvariantCulture);
            bigTime.Text = now.ToString(fmt, CultureInfo.InvariantCulture);
            var date = Settings.English ? now.ToString("dddd, MMMM d", CultureInfo.InvariantCulture) : now.ToString("dddd, d MMMM", Ru);
            bigDate.Text = char.ToUpper(date[0]) + date.Substring(1);
            if (Weather.Text != weatherShown)
            {
                weatherShown = Weather.Text;
                weatherLine.Inlines.Clear();
                weatherLine.Inlines.Add(new System.Windows.Documents.Run(Weather.Short) { Foreground = UI.Fg, FontWeight = FontWeights.SemiBold });
                weatherLine.Inlines.Add(new System.Windows.Documents.Run("  " + Weather.Text) { Foreground = UI.Gray });
                weatherLine.ToolTip = Weather.Place;
                weatherLine.Visibility = Visibility.Visible;
            }

            long i, k, u;
            Native.GetSystemTimes(out i, out k, out u);
            long total = (k - lKernel) + (u - lUser);
            double cpu = total > 0 ? 1 - (double)(i - lIdle) / total : 0;
            lIdle = i; lKernel = k; lUser = u;
            bool open = mode == Mode.Expanded;
            if (open) gCpu.Set(cpu, Math.Round(cpu * 100) + "%");

            var m = new Native.MEMSTATUS();
            Native.GlobalMemoryStatusEx(m);
            if (open) gRam.Set(m.dwMemoryLoad / 100.0, m.dwMemoryLoad + "%");
            if (open) gPing.Set(pingMs < 0 ? 0.001 : Math.Min(1, pingMs / 150.0), pingMs < 0 ? "-" : pingMs + "");

            var ps = WF.SystemInformation.PowerStatus;
            bool noBattery = (ps.BatteryChargeStatus & WF.BatteryChargeStatus.NoSystemBattery) != 0 || ps.BatteryLifePercent > 1;
            bool ac = ps.PowerLineStatus == WF.PowerLineStatus.Online;
            int pct = (int)Math.Round(ps.BatteryLifePercent * 100);
            // on a desktop the battery ring says nothing; ping takes its place
            gBat.Visibility = noBattery || !Settings.Shown("gauges").Contains("bat") ? Visibility.Collapsed : Visibility.Visible;
            if (noBattery) gBat.Set(1, "AC");
            else if (open) gBat.Set(ps.BatteryLifePercent, pct + "%");
            if (!timerOn)
            {
                // a desktop has no battery to show, so the right side shows ping instead (no dot)
                var what = Settings.PillRight;
                if (what == "auto") what = noBattery ? "ping" : "battery";
                if (what == "battery" && noBattery) what = "none";
                if (what == "weather" && Weather.Short == null) what = "none";
                miniRight.Text = what == "battery" ? pct + "%" : what == "ping" ? (pingMs < 0 ? "-" : pingMs + L.T(" мс")) : what == "weather" ? Weather.Short : "";
                miniDot.Visibility = what == "battery" ? Visibility.Visible : Visibility.Collapsed;
                var dot = !noBattery && pct <= 20 && !ac ? UI.Red : UI.Green;
                if (((SolidColorBrush)miniDot.Fill).Color != dot) miniDot.Fill = new SolidColorBrush(dot);
            }
            if (!noBattery && powerKnown && ac != lastAc)
                Notify(ac ? "" : "", ac ? UI.Green : UI.Orange, ac ? "Зарядка подключена" : "Работа от батареи", "Заряд " + pct + "%");
            lastAc = ac; powerKnown = true;

            if (!Topmost) Topmost = true;
        }
    }

    static class Program
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            var mutex = new System.Threading.Mutex(true, "IslandApp_SingleInstance", out created);
            if (!created)
            {
                // after saving settings the old copy is still closing; wait for it
                if (!args.Contains("--restart")) return;
                try { if (!mutex.WaitOne(10000)) return; } catch (System.Threading.AbandonedMutexException) { }
            }
            Settings.Load();
            UI.SetTheme(Settings.Theme);

            string winVer = "?";
            try { using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")) winVer = k.GetValue("ProductName") + " " + k.GetValue("DisplayVersion") + " (сборка " + k.GetValue("CurrentBuild") + ")"; } catch { }
            Log.Write("start", "Island запущен · " + winVer + " · .NET " + Environment.Version + " · " + (Environment.Is64BitProcess ? "x64" : "x86"));
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("crash", e.ExceptionObject.ToString());
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (s, e) => { Log.Write("crash", e.Exception.ToString()); e.Handled = true; };
            var win = new IslandWindow();
            win.Show();

            var tray = new WF.NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule.FileName),
                Text = "Island",
                Visible = true
            };
            var menu = new WF.ContextMenuStrip();
            var hide = new WF.ToolStripMenuItem(L.T("Скрыть островок") + "   Ctrl+Alt+I");
            hide.Click += (s, e) => win.ToggleHidden();
            IslandWindow.VisibilityChanged = shown => hide.Text = L.T(shown ? "Скрыть островок" : "Показать островок") + "   Ctrl+Alt+I";
            tray.MouseDoubleClick += (s, e) => { if (e.Button == WF.MouseButtons.Left) win.ToggleHidden(); };
            menu.Items.Add(hide);
            menu.Items.Add(L.T("Настройки"), null, (s, e) => win.ShowSettings());
            menu.Items.Add(L.T("Вернуть наверх по центру"), null, (s, e) => win.ResetPosition());
            menu.Items.Add(new WF.ToolStripSeparator());
            var auto = new WF.ToolStripMenuItem(L.T("Запускать с Windows")) { Checked = IsAutostart() };
            auto.Click += (s, e) => { SetAutostart(!auto.Checked); auto.Checked = IsAutostart(); };
            menu.Items.Add(auto);
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add(L.T("Выход"), null, (s, e) => { tray.Visible = false; app.Shutdown(); });
            tray.ContextMenuStrip = menu;

            app.Run();
            tray.Dispose();
            GC.KeepAlive(mutex);
        }

        static bool IsAutostart()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue("Island") != null;
        }

        static void SetAutostart(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue("Island", "\"" + Process.GetCurrentProcess().MainModule.FileName + "\"");
                else k.DeleteValue("Island", false);
            }
        }
    }
}
