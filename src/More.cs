using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Island
{
    partial class IslandWindow
    {
        const double CallW = 430, CallH = 76;

        Action noticeAction;
        Windows.UI.Notifications.Management.UserNotificationListener toastListener;

        // ---------- calls ----------

        StackPanel callButtons;
        uint callToastId;
        static readonly string[] CallWords = { "звонок", "звонит", "вызов", "incoming call", "is calling", "calling you", "входящий" };

        void InitMore()
        {
            var row = (DockPanel)noticeIcon.Parent;
            callButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, Margin = new Thickness(8, 0, -8, 0) };
            DockPanel.SetDock(callButtons, Dock.Right);
            callButtons.Children.Add(RoundButton("", UI.Red, "Отклонить", DeclineCall));
            callButtons.Children.Add(RoundButton("", UI.Green, "Принять - открыть звонок", AcceptCall));
            row.Children.Insert(1, callButtons);

            BuildPrivacy();
            LoadClips();
            HookWheel();
            WatchDownloads();
            System.Threading.ThreadPool.QueueUserWorkItem(_ => PingLoop());
            Deactivated += (s, e) => EndTyping();
        }

        Border RoundButton(string glyph, Color color, string tip, Action click)
        {
            var g = UI.Glyph(glyph, 16, Brushes.White);
            var b = UI.Pressable(g, 44, 44, 22, click);
            b.Background = new SolidColorBrush(color);
            b.ToolTip = tip; b.Margin = new Thickness(5, 0, 0, 0);
            // keep the call colour on hover instead of the grey card colour
            b.MouseLeave += (s, e) => b.Background = new SolidColorBrush(color);
            b.MouseEnter += (s, e) => b.Background = new SolidColorBrush(Color.FromArgb(220, color.R, color.G, color.B));
            return b;
        }

        static bool LooksLikeCall(string title, string body)
        {
            var t = (title + " " + body).ToLowerInvariant();
            return CallWords.Any(w => t.Contains(w));
        }

        void ShowCall(string app, string who, string text, string exePath, IntPtr target, uint toastId)
        {
            callToastId = toastId;
            if (mode == Mode.Expanded || mode == Mode.Drop) mode = Mode.Collapsed;
            ImageSource icon = exePath != null ? UI.FileIcon(exePath) : null;
            Notify("", UI.Green, who, app + "  ·  " + text, icon, 30000);
            if (noticeIcon.Child is Image) { noticeIcon.Background = Brushes.Transparent; ((Image)noticeIcon.Child).Margin = new Thickness(4); }
            noticeTarget = target;
            callButtons.Visibility = Visibility.Visible;
            SetMode(Mode.Call);
            Shake();
        }

        void AcceptCall()
        {
            var t = noticeTarget;
            EndCall();
            if (t != IntPtr.Zero) { if (Native.IsIconic(t)) Native.ShowWindow(t, 9); Native.SetForegroundWindow(t); }
        }

        void DeclineCall()
        {
            try { if (toastListener != null && callToastId != 0) toastListener.RemoveNotification(callToastId); } catch { }
            EndCall();
        }

        void EndCall()
        {
            callButtons.Visibility = Visibility.Collapsed;
            noticeTarget = IntPtr.Zero; callToastId = 0;
            noticeTimer.Stop();
            SetMode(hovering ? Mode.Expanded : Mode.Collapsed);
        }

        // ---------- mouse wheel ----------
        // The island never takes focus, so Windows sends the wheel to the focused app instead.
        // A low-level hook catches wheel turns over the pill and replays them as normal WPF events.

        delegate IntPtr LowLevelProc(int code, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, LowLevelProc proc, IntPtr mod, uint thread);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
        [StructLayout(LayoutKind.Sequential)] struct MSLL { public int X, Y; public uint Data, Flags, Time; public IntPtr Extra; }

        LowLevelProc wheelProc; IntPtr wheelHook;

        void HookWheel()
        {
            wheelProc = (code, w, l) =>
            {
                if (code >= 0 && w.ToInt32() == 0x0202 && Settings.Get("translator") == "1" && Settings.Get("translateAuto") == "1")
                {
                    var click = (MSLL)Marshal.PtrToStructure(l, typeof(MSLL));
                    Dispatcher.BeginInvoke(new Action(() => SelectionMouseUp(click.X, click.Y)));
                }
                if (code >= 0 && w.ToInt32() == 0x020A && pill.IsHitTestVisible && !typing)
                {
                    var info = (MSLL)Marshal.PtrToStructure(l, typeof(MSLL));                    try
                    {
                        var p = pill.PointFromScreen(new Point(info.X, info.Y));
                        if (p.X >= 0 && p.Y >= 0 && p.X <= pill.ActualWidth && p.Y <= pill.ActualHeight)
                        {
                            int delta = (short)(info.Data >> 16);
                            if (ReplayWheel(p, delta)) return (IntPtr)1;
                        }
                    }
                    catch { }
                }
                return CallNextHookEx(wheelHook, code, w, l);
            };
            wheelHook = SetWindowsHookEx(14, wheelProc, GetModuleHandle(null), 0);
        }

        bool ReplayWheel(Point p, int delta)
        {
            // handlers may call COM (volume), which is not allowed inside the hook callback itself
            Dispatcher.BeginInvoke(new Action(() => RaiseWheel(p, delta)));
            return true; // the island is on top; the app below shouldn't scroll too
        }

        void RaiseWheel(Point p, int delta)
        {
            var target = pill.InputHitTest(p) as UIElement ?? pill;            var pre = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
            target.RaiseEvent(pre);
            if (pre.Handled) return;
            var ev = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = UIElement.MouseWheelEvent };
            target.RaiseEvent(ev);
        }

        // ---------- camera / microphone in use ----------

        TextBlock micDot, camDot, privacyText;
        HashSet<string> micApps = new HashSet<string>(), camApps = new HashSet<string>();
        bool privacyFirst = true;

        void BuildPrivacy()
        {
            var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            // small icons instead of dots, so they can't be confused with the battery dot
            micDot = UI.Glyph("", 11, new SolidColorBrush(UI.Orange)); micDot.Margin = new Thickness(3, 0, 3, 0); micDot.Visibility = Visibility.Collapsed;
            camDot = UI.Glyph("", 11, new SolidColorBrush(UI.Green)); camDot.Margin = new Thickness(3, 0, 3, 0); camDot.Visibility = Visibility.Collapsed;
            dots.Children.Add(camDot); dots.Children.Add(micDot);
            layerCollapsed.Children.Add(dots);

            privacyText = UI.Text("", 12, new SolidColorBrush(UI.Orange));
            privacyText.Margin = new Thickness(0, 3, 0, 0); privacyText.Visibility = Visibility.Collapsed;
            clockPanel.Children.Add(privacyText);

            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            t.Tick += (s, e) => CheckPrivacy();
            t.Start();
            CheckPrivacy();
        }

        void CheckPrivacy()
        {
            var mic = InUse("microphone"); var cam = InUse("webcam");
            if (!privacyFirst)
            {
                foreach (var a in mic.Except(micApps)) Notify("", UI.Orange, "Микрофон включён", a, null, 1800);
                foreach (var a in cam.Except(camApps)) Notify("", UI.Green, "Камера включена", a, null, 1800);
            }
            privacyFirst = false;
            micApps = mic; camApps = cam;
            micDot.Visibility = mic.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            camDot.Visibility = cam.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            // each device in its own colour: icon tinted, app names in plain grey
            privacyText.Inlines.Clear();
            Action<string, Color, HashSet<string>> part = (glyph, c, apps) =>
            {
                if (apps.Count == 0) return;
                if (privacyText.Inlines.Count > 0) privacyText.Inlines.Add(new System.Windows.Documents.Run("    "));
                privacyText.Inlines.Add(new System.Windows.Documents.Run(glyph) { FontFamily = UI.Icons, Foreground = new SolidColorBrush(c), FontSize = 11 });
                privacyText.Inlines.Add(new System.Windows.Documents.Run(" " + string.Join(", ", apps)) { Foreground = UI.Gray });
            };
            part("", UI.Green, cam);
            part("", UI.Orange, mic);
            privacyText.Visibility = cam.Count + mic.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // Windows records who is using the device in the capability consent store
        static HashSet<string> InUse(string cap)
        {
            var res = new HashSet<string>();
            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\" + cap))
                {
                    if (root == null) return res;
                    Scan(root, res, 0);
                }
            }
            catch { }
            return res;
        }

        static void Scan(RegistryKey key, HashSet<string> res, int depth)
        {
            foreach (var name in key.GetSubKeyNames())
            {
                using (var k = key.OpenSubKey(name))
                {
                    if (k == null) continue;
                    var stop = k.GetValue("LastUsedTimeStop");
                    var start = k.GetValue("LastUsedTimeStart");
                    if (stop is long && start is long && (long)stop == 0 && (long)start > 0)
                    {
                        var n = name.Contains("#") ? System.IO.Path.GetFileNameWithoutExtension(name.Replace('#', '\\')) : name.Split('_')[0].Split('.').Last();
                        res.Add(n.Length > 0 ? char.ToUpper(n[0]) + n.Substring(1) : n);
                    }
                    if (depth == 0 && name == "NonPackaged") Scan(k, res, 1);
                }
            }
        }

        // ---------- notes ----------

        class Note { public string File; public string Text; }

        TextBox noteBox;
        bool typing;
        readonly List<Note> notes = new List<Note>();
        Note editing;
        StackPanel noteList; Grid noteBrowse, noteEdit;
        static readonly string NotesDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island", "notes");

        FrameworkElement BuildNotePage()
        {
            LoadNotes();
            var g = new Grid();

            // list of notes: cards + a "new" card
            noteBrowse = new Grid();
            noteList = new StackPanel { Orientation = Orientation.Horizontal };
            noteBrowse.Children.Add(UI.HScroll(noteList));
            g.Children.Add(noteBrowse);

            // editor: back · text · delete
            noteEdit = new Grid { Visibility = Visibility.Collapsed, Opacity = 0 };
            noteEdit.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            noteEdit.ColumnDefinitions.Add(new ColumnDefinition());
            noteEdit.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var back = UI.Pressable(UI.Glyph("", 13, UI.Fg), 34, 34, 17, CloseNote);
            back.ToolTip = L.T("К списку (Esc)"); back.VerticalAlignment = VerticalAlignment.Top; back.Margin = new Thickness(0, 0, 8, 0);
            var del = UI.Pressable(UI.Glyph("", 13, new SolidColorBrush(UI.Red)), 34, 34, 17, () => { if (editing != null) { editing.Text = ""; CloseNote(); } });
            del.ToolTip = L.T("Удалить заметку"); del.VerticalAlignment = VerticalAlignment.Top; del.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(del, 2);
            var card = new Border { CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(UI.Card), Padding = new Thickness(10, 6, 10, 6) };
            Grid.SetColumn(card, 1);
            noteBox = new TextBox
            {
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = UI.Fg, CaretBrush = UI.Fg,
                FontSize = 13, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                SelectionBrush = new SolidColorBrush(UI.Blue)
            };
            card.Child = noteBox;
            noteEdit.Children.Add(back); noteEdit.Children.Add(card); noteEdit.Children.Add(del);
            g.Children.Add(noteEdit);

            var save = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            save.Tick += (s, e) => { save.Stop(); SaveNote(editing); };
            noteBox.TextChanged += (s, e) => { if (editing == null) return; editing.Text = noteBox.Text; save.Stop(); save.Start(); };
            noteBox.PreviewMouseLeftButtonDown += (s, e) => BeginTyping();
            noteBox.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { CloseNote(); e.Handled = true; } };

            RenderNotes();
            return g;
        }

        void LoadNotes()
        {
            try
            {
                Directory.CreateDirectory(NotesDir);
                // the single note from older versions becomes the first of many
                var old = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(NotesDir), "note.txt");
                if (File.Exists(old)) { File.Move(old, System.IO.Path.Combine(NotesDir, DateTime.Now.Ticks + ".txt")); }
                foreach (var f in Directory.GetFiles(NotesDir, "*.txt").OrderByDescending(File.GetLastWriteTime))
                    notes.Add(new Note { File = f, Text = File.ReadAllText(f) });
            }
            catch { }
        }

        void SaveNote(Note n)
        {
            if (n == null) return;
            try
            {
                if (string.IsNullOrWhiteSpace(n.Text)) { if (File.Exists(n.File)) File.Delete(n.File); }
                else File.WriteAllText(n.File, n.Text);
            }
            catch { }
        }

        public void AddNote(string text)
        {
            var n = new Note { File = System.IO.Path.Combine(NotesDir, DateTime.Now.Ticks + ".txt"), Text = text };
            notes.Insert(0, n);
            SaveNote(n);
            RenderNotes();
        }

        void RenderNotes()
        {
            noteList.Children.Clear();
            var plus = UI.Pressable(UI.Glyph("", 18, UI.Fg), 64, 64, 16, () => { AddNote(""); OpenNote(notes[0]); });
            plus.ToolTip = L.T("Новая заметка"); plus.Margin = new Thickness(0, 0, 8, 0);
            noteList.Children.Add(plus);
            foreach (var n in notes)
            {
                var note = n;
                var lines = note.Text.Trim().Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
                var st = new StackPanel { Margin = new Thickness(12, 9, 12, 9) };
                st.Children.Add(UI.Text(lines.Length > 0 ? lines[0] : "Пустая заметка", 12.5, UI.Fg, FontWeights.SemiBold));
                var rest = new TextBlock { Text = string.Join(" ", lines.Skip(1)), Foreground = UI.Gray, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 30 };
                st.Children.Add(rest);
                var card = UI.Pressable(st, 140, 64, 16, () => OpenNote(note));
                card.Margin = new Thickness(0, 0, 8, 0);
                card.ToolTip = L.T("Открыть · ПКМ - удалить");
                card.MouseRightButtonUp += (s, e) => { e.Handled = true; note.Text = ""; SaveNote(note); notes.Remove(note); RenderNotes(); };
                noteList.Children.Add(card);
            }
        }

        void OpenNote(Note n)
        {
            editing = n;
            noteBox.Text = n.Text;
            noteBox.CaretIndex = noteBox.Text.Length;
            noteEdit.Visibility = Visibility.Visible;
            UI.Ease(noteBrowse, OpacityProperty, 0, 140);
            UI.Ease(noteEdit, OpacityProperty, 1, 220, 60);
            noteBrowse.IsHitTestVisible = false;
            BeginTyping();
        }

        void CloseNote()
        {
            if (editing != null)
            {
                SaveNote(editing);
                if (string.IsNullOrWhiteSpace(editing.Text)) notes.Remove(editing);
                else { notes.Remove(editing); notes.Insert(0, editing); } // last edited goes first
                editing = null;
            }
            RenderNotes();
            UI.Ease(noteEdit, OpacityProperty, 0, 120);
            UI.Ease(noteBrowse, OpacityProperty, 1, 220, 60);
            noteBrowse.IsHitTestVisible = true;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(130) };
            t.Tick += (s, e) => { t.Stop(); if (editing == null) noteEdit.Visibility = Visibility.Collapsed; };
            t.Start();
            EndTyping();
        }

        // the island never takes focus, except while the note is being edited
        void BeginTyping(TextBox box = null)
        {
            box = box ?? noteBox;
            if (!typing)
            {
                typing = true;
                var h = new WindowInteropHelper(this).Handle;
                Native.SetWindowLong(h, -20, Native.GetWindowLong(h, -20) & ~0x08000000);
                // Windows refuses to hand focus to a background window; a synthetic Alt tap lifts that lock
                Native.keybd_event(0x12, 0, 0, UIntPtr.Zero);
                Native.keybd_event(0x12, 0, 2, UIntPtr.Zero);
                Native.SetForegroundWindow(h);
                Activate();
            }
            box.Focus();
            Keyboard.Focus(box);
        }

        void EndTyping()
        {
            if (!typing) return;
            typing = false;
            var h = new WindowInteropHelper(this).Handle;
            Native.SetWindowLong(h, -20, Native.GetWindowLong(h, -20) | 0x08000000);
            Keyboard.ClearFocus();
            if (mode == Mode.Search || (!hovering && mode == Mode.Expanded)) SetMode(Mode.Collapsed);
        }

        // ---------- downloads ----------

        FileSystemWatcher dlWatcher;
        readonly HashSet<string> dlSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly string[] PartialExt = { ".crdownload", ".part", ".partial", ".tmp", ".download", ".opdownload" };

        [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

        static string DownloadsDir()
        {
            IntPtr p;
            if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out p) == 0)
            {
                var s = Marshal.PtrToStringUni(p); Marshal.FreeCoTaskMem(p); return s;
            }
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        void WatchDownloads()
        {
            try
            {
                dlWatcher = new FileSystemWatcher(DownloadsDir()) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName };
                dlWatcher.Created += (s, e) => Dispatcher.BeginInvoke(new Action(() => OnDownload(e.FullPath)));
                dlWatcher.Renamed += (s, e) => Dispatcher.BeginInvoke(new Action(() => OnDownload(e.FullPath)));
                dlWatcher.EnableRaisingEvents = true;
            }
            catch { }
        }

        void OnDownload(string path)
        {
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            var name = System.IO.Path.GetFileName(path);
            if (PartialExt.Contains(ext) || name.StartsWith("~") || name.StartsWith(".") || !dlSeen.Add(path)) return;
            // wait until the file stops growing
            long last = -1; int checks = 0;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            t.Tick += (s, e) =>
            {
                long size;
                try { size = new FileInfo(path).Length; } catch { t.Stop(); return; }
                if (size == last && size > 0 || ++checks > 200)
                {
                    t.Stop();
                    if (!File.Exists(path)) return;
                    if (!shelf.Any(x => x.Path == path)) { shelf.Add(new ShelfItem { Path = path }); RenderShelf(); }
                    Notify(null, UI.Blue, "Загружено", name + "  ·  " + HumanSize(size), UI.FileIcon(path), 4000);
                    if (noticeIcon.Child is Image) { noticeIcon.Background = Brushes.Transparent; ((Image)noticeIcon.Child).Margin = new Thickness(4); ((Image)noticeIcon.Child).Stretch = Stretch.Uniform; }
                    else noticeIcon.Child = UI.Glyph("", 18, new SolidColorBrush(UI.Blue));
                    noticeAction = () => Log.Reveal(path);
                    layerNotice.Cursor = Cursors.Hand;
                }
                last = size;
            };
            t.Start();
        }

        static string HumanSize(long b)
        {
            if (b < 1024) return b + " Б";
            if (b < 1024 * 1024) return (b / 1024.0).ToString("0.#") + " КБ";
            if (b < 1024L * 1024 * 1024) return (b / 1048576.0).ToString("0.#") + " МБ";
            return (b / 1073741824.0).ToString("0.##") + " ГБ";
        }

        // ---------- ping / network ----------

        int pingMs = -1, pingFails;
        bool online = true;

        // A VPN in TUN mode answers ICMP and TCP handshakes itself in a few ms, so those say nothing.
        // An HTTP request on a kept-alive connection has to cross the tunnel and come back: one real round trip.
        void PingLoop()
        {
            System.Net.ServicePointManager.Expect100Continue = false;
            var icmp = new Ping();
            while (true)
            {
                long ms = HttpPing("http://cp.cloudflare.com/generate_204");
                if (ms < 0) try { var r = icmp.Send("1.1.1.1", 1500); if (r.Status == IPStatus.Success) ms = r.RoundtripTime; } catch { }
                Dispatcher.BeginInvoke(new Action(() => OnPing((int)ms)));
                System.Threading.Thread.Sleep(2000);
            }
        }

        static bool pingWarm;

        static long HttpPing(string url)
        {
            try
            {
                // the first request also opens the connection, so it is not counted
                for (int i = pingWarm ? 1 : 0; i < 2; i++)
                {
                    var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                    req.KeepAlive = true; req.Timeout = 2500; req.Proxy = null; req.AllowAutoRedirect = false;
                    var sw = Stopwatch.StartNew();
                    using (var resp = req.GetResponse()) { }
                    if (i == 1) { pingWarm = true; return sw.ElapsedMilliseconds; }
                }
            }
            catch { pingWarm = false; }
            return -1;
        }

        void OnPing(int ms)
        {
            pingMs = ms;
            if (ms < 0) { if (++pingFails == 3 && online) { online = false; Notify("", UI.Red, "Нет интернета", "Соединение потеряно", null, 4000); Shake(); } }
            else
            {
                pingFails = 0;
                if (!online) { online = true; Notify("", UI.Green, "Интернет вернулся", "Пинг " + ms + " мс", null, 2500); }
            }
            if (mode == Mode.Expanded) gPing.Set(ms < 0 ? 0.001 : Math.Min(1, ms / 150.0), ms < 0 ? "-" : ms + "");
            gPing.SetColor(ms < 0 ? UI.Red : ms < 60 ? UI.Green : ms < 120 ? UI.Orange : UI.Red);
        }
    }
}
