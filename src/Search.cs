using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Island
{
    class SearchResult
    {
        public string Glyph, Title, Sub;
        public ImageSource Icon;
        public Action Run;
    }

    partial class IslandWindow
    {
        const double SW = 520, SH = 58, SHR = 124;

        Grid layerSearch;
        TextBox searchBox;
        Border resultRow, resultIcon;
        TextBlock resultTitle, resultSub, searchHint;
        SearchResult current;
        List<string> shortcuts = new List<string>();

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc, int x, int y);
        struct POINT { public int X, Y; }

        void InitSearch(Grid root)
        {
            layerSearch = new Grid { Width = SW, Height = SHR, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Opacity = 0, IsHitTestVisible = false };
            var stack = new StackPanel();

            var input = new DockPanel { Height = SH, Margin = new Thickness(20, 0, 20, 0) };
            var lens = UI.Glyph("", 16, UI.Gray); lens.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(lens, Dock.Left);
            searchBox = new TextBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = UI.Fg, CaretBrush = UI.Fg, FontSize = 17, VerticalContentAlignment = VerticalAlignment.Center, SelectionBrush = new SolidColorBrush(UI.Blue) };
            searchHint = UI.Text("Посчитать, перевести единицы, открыть программу…", 17, new SolidColorBrush(UI.Dim));
            searchHint.IsHitTestVisible = false; searchHint.VerticalAlignment = VerticalAlignment.Center; searchHint.Margin = new Thickness(2, 0, 0, 0);
            var field = new Grid(); field.Children.Add(searchBox); field.Children.Add(searchHint);
            input.Children.Add(lens); input.Children.Add(field);
            stack.Children.Add(input);

            resultRow = new Border { Margin = new Thickness(10, 0, 10, 10), Height = 56, CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(UI.Card), Cursor = Cursors.Hand, Opacity = 0 };
            var rr = new DockPanel { Margin = new Thickness(10, 0, 14, 0) };
            resultIcon = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(UI.Track), Margin = new Thickness(0, 0, 12, 0) };
            DockPanel.SetDock(resultIcon, Dock.Left);
            var enter = UI.Text("Enter ↵", 12, UI.Gray); enter.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(enter, Dock.Right);
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            resultTitle = UI.Text("", 15, UI.Fg, FontWeights.SemiBold);
            resultSub = UI.Text("", 12, UI.Gray);
            texts.Children.Add(resultTitle); texts.Children.Add(resultSub);
            rr.Children.Add(resultIcon); rr.Children.Add(enter); rr.Children.Add(texts);
            resultRow.Child = rr;
            resultRow.MouseLeftButtonUp += (s, e) => RunResult();
            stack.Children.Add(resultRow);
            layerSearch.Children.Add(stack);
            root.Children.Add(layerSearch);

            searchBox.TextChanged += (s, e) => UpdateSearch();
            searchBox.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { CloseSearch(); e.Handled = true; }
                else if (e.Key == Key.Enter) { RunResult(); e.Handled = true; }
            };

            SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                RegisterHotKey(h, 0x1A01, 0x0001 | 0x4000, 0x20); // Alt+Space, no repeat
                HwndSource.FromHwnd(h).AddHook((IntPtr hw, int msg, IntPtr w, IntPtr l, ref bool handled) =>
                {
                    if (msg == 0x0312 && w.ToInt32() == 0x1A01) { if (mode == Mode.Search) CloseSearch(); else OpenSearch(); handled = true; }
                    return IntPtr.Zero;
                });
            };
            System.Threading.ThreadPool.QueueUserWorkItem(_ => IndexShortcuts());
        }

        public void OpenSearch()
        {
            if (fullscreen) return;
            if (mode == Mode.Call) return;
            noticeTimer.Stop();
            searchBox.Text = "";
            SetMode(Mode.Search);
            BeginTyping(searchBox);
        }

        void CloseSearch()
        {
            if (mode != Mode.Search) return;
            typing = false; // EndTyping below must not try to keep the old panel
            var h = new WindowInteropHelper(this).Handle;
            Native.SetWindowLong(h, -20, Native.GetWindowLong(h, -20) | 0x08000000);
            Keyboard.ClearFocus();
            SetMode(Mode.Collapsed);
        }

        void UpdateSearch()
        {
            var q = searchBox.Text.Trim();
            searchHint.Visibility = searchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            current = q.Length == 0 ? null : Evaluate(q);
            bool has = current != null;
            UI.Ease(pill, HeightProperty, has ? SHR : SH, 260);
            UI.Ease(resultRow, OpacityProperty, has ? 1 : 0, 180);
            if (!has) return;
            resultTitle.Text = L.T(current.Title);
            resultSub.Text = L.T(current.Sub ?? "");
            resultIcon.Child = current.Icon != null
                ? (UIElement)new Image { Source = current.Icon, Width = 26, Height = 26 }
                : UI.Glyph(current.Glyph ?? "", 16, UI.Fg);
        }

        void RunResult()
        {
            if (current == null || current.Run == null) return;
            var run = current.Run;
            CloseSearch();
            try { run(); } catch { }
        }

        // ---------- what the line understands ----------

        SearchResult Evaluate(string q)
        {
            var lower = q.ToLowerInvariant();

            var tm = Regex.Match(lower, @"^(таймер|timer)\s+(.+)$");
            var dur = tm.Success ? ParseDuration(tm.Groups[2].Value) : null;
            if (dur.HasValue)
            {
                var d = dur.Value;
                return new SearchResult { Glyph = "\uE916", Title = "Таймер на " + HumanDur(d), Sub = "Запустить", Run = () => StartTimer(d) };
            }

            var nm = Regex.Match(q, @"^(заметка|note)\s+(.+)$", RegexOptions.IgnoreCase);
            if (nm.Success)
            {
                var text = nm.Groups[2].Value;
                return new SearchResult { Glyph = "", Title = text, Sub = "Создать заметку", Run = () => { AddNote(text); Notify("", UI.Orange, "Новая заметка", text, null, 1600); } };
            }

            var unit = ConvertUnits(lower);
            if (unit != null) return CopyResult("", unit, q);

            var math = Calc(q);
            if (math != null) return CopyResult("", "= " + math, q, math);

            // a matching program always wins over the layout guess
            var app = FindApp(lower);
            if (app != null)
                return new SearchResult { Icon = UI.FileIcon(app), Title = System.IO.Path.GetFileNameWithoutExtension(app), Sub = "Открыть программу", Run = () => Process.Start(app) };

            var fixedLayout = FixLayout(q);
            if (fixedLayout != null)
                return new SearchResult { Glyph = "", Title = fixedLayout, Sub = "Исправить раскладку · скопировать", Run = () => { suppressClip++; Clipboard.SetText(fixedLayout); Notify("", UI.Blue, "Раскладка исправлена", fixedLayout, null, 1800); } };

            return new SearchResult { Glyph = "", Title = q, Sub = "Найти в интернете", Run = () => Process.Start("https://www.google.com/search?q=" + Uri.EscapeDataString(q)) };
        }

        SearchResult CopyResult(string glyph, string title, string q, string value = null)
        {
            var v = value ?? title;
            return new SearchResult { Glyph = glyph, Title = title, Sub = q + " · Enter - скопировать", Run = () => { Clipboard.SetText(v); } };
        }

        static string Calc(string q)
        {
            var e = q.Replace(',', '.').Replace('×', '*').Replace('x', '*').Replace('х', '*').Replace(':', '/').Replace('÷', '/');
            if (!Regex.IsMatch(e, @"^[\d\s+\-*/().%]+$") || !Regex.IsMatch(e, @"\d\s*[+\-*/%]") ) return null;
            // "2400 * 15%" -> 2400 * 0.15,  "2400 + 15%" -> 2400 * 1.15
            e = Regex.Replace(e, @"([\d.]+)\s*([+\-])\s*([\d.]+)\s*%", m => m.Groups[1].Value + "*(1" + m.Groups[2].Value + m.Groups[3].Value + "/100)");
            e = Regex.Replace(e, @"([\d.]+)\s*%", "($1/100)");
            try
            {
                var r = Convert.ToDouble(new DataTable().Compute(e, null), CultureInfo.InvariantCulture);
                if (double.IsNaN(r) || double.IsInfinity(r)) return null;
                return Math.Round(r, 6).ToString("#,0.######", CultureInfo.GetCultureInfo("ru-RU")).Replace(' ', ' ');
            }
            catch { return null; }
        }

        // unit -> (group, factor to base)
        static readonly Dictionary<string, Tuple<string, double>> Units = new Dictionary<string, Tuple<string, double>>
        {
            { "мм", T("len", 0.001) }, { "см", T("len", 0.01) }, { "км", T("len", 1000) }, { "м", T("len", 1) },
            { "mm", T("len", 0.001) }, { "cm", T("len", 0.01) }, { "km", T("len", 1000) }, { "m", T("len", 1) },
            { "дюйм", T("len", 0.0254) }, { "inch", T("len", 0.0254) }, { "фут", T("len", 0.3048) }, { "ft", T("len", 0.3048) },
            { "мил", T("len", 1609.344) }, { "mi", T("len", 1609.344) }, { "ярд", T("len", 0.9144) },
            { "мг", T("mass", 0.001) }, { "кг", T("mass", 1000) }, { "г", T("mass", 1) }, { "kg", T("mass", 1000) }, { "g", T("mass", 1) },
            { "т", T("mass", 1000000) }, { "фунт", T("mass", 453.592) }, { "lb", T("mass", 453.592) }, { "унц", T("mass", 28.3495) }, { "oz", T("mass", 28.3495) },
            { "кб", T("data", 1024) }, { "мб", T("data", 1048576) }, { "гб", T("data", 1073741824) }, { "тб", T("data", 1099511627776) }, { "б", T("data", 1) },
            { "kb", T("data", 1024) }, { "mb", T("data", 1048576) }, { "gb", T("data", 1073741824) }, { "tb", T("data", 1099511627776) },
            { "мин", T("time", 60) }, { "сек", T("time", 1) }, { "час", T("time", 3600) }, { "ч", T("time", 3600) }, { "дн", T("time", 86400) }, { "сут", T("time", 86400) },
            { "л", T("vol", 1) }, { "мл", T("vol", 0.001) }, { "галлон", T("vol", 3.78541) },
            { "км/ч", T("speed", 1 / 3.6) }, { "м/с", T("speed", 1) }, { "mph", T("speed", 0.44704) },
        };
        static Tuple<string, double> T(string g, double f) { return Tuple.Create(g, f); }

        static string Unit(string word)
        {
            word = word.Trim('.');
            if (Units.ContainsKey(word)) return word;
            // longest prefix wins: "милях" -> "мил", "килограммах" -> "кг" is not a prefix, so map full words too
            var full = new Dictionary<string, string> { { "километр", "км" }, { "сантиметр", "см" }, { "миллиметр", "мм" }, { "метр", "м" }, { "килограмм", "кг" }, { "грамм", "г" }, { "тонн", "т" }, { "литр", "л" }, { "миллилитр", "мл" }, { "минут", "мин" }, { "секунд", "сек" }, { "день", "дн" }, { "дней", "дн" }, { "дня", "дн" } };
            foreach (var kv in full.OrderByDescending(k => k.Key.Length)) if (word.StartsWith(kv.Key)) return kv.Value;
            foreach (var k in Units.Keys.Where(k => k.Length >= 3).OrderByDescending(k => k.Length)) if (word.StartsWith(k)) return k;
            return null;
        }

        static string ConvertUnits(string q)
        {
            var m = Regex.Match(q, @"^([\d.,]+)\s*([^\d\s]+)\s+(в|во|in|to)\s+(\S+)$");
            if (!m.Success) return null;
            double v;
            if (!double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return null;
            string a = m.Groups[2].Value, b = m.Groups[4].Value.Trim('.');
            var ru = CultureInfo.GetCultureInfo("ru-RU");
            // temperature is not a plain factor
            Func<string, char> temp = s => s.StartsWith("°") ? char.ToLower(s.Length > 1 ? s[1] : 'c') : s == "c" || s.StartsWith("цел") || s == "с" ? 'c' : s == "f" || s.StartsWith("фар") ? 'f' : s == "k" || s.StartsWith("кел") ? 'k' : ' ';
            char ta = temp(a), tb = temp(b);
            if (ta != ' ' && tb != ' ')
            {
                double c = ta == 'c' ? v : ta == 'f' ? (v - 32) * 5 / 9 : v - 273.15;
                double r = tb == 'c' ? c : tb == 'f' ? c * 9 / 5 + 32 : c + 273.15;
                return Math.Round(r, 2).ToString("0.##", ru) + " °" + char.ToUpper(tb == 'k' ? 'K' : tb);
            }
            string ua = Unit(a), ub = Unit(b);
            if (ua == null || ub == null || Units[ua].Item1 != Units[ub].Item1) return null;
            double res = v * Units[ua].Item2 / Units[ub].Item2;
            return (Math.Abs(res) >= 100 ? Math.Round(res, 1) : Math.Round(res, 4)).ToString("#,0.####", ru).Replace(' ', ' ') + " " + (ub == "мил" ? "миль" : ub == "унц" ? "унц." : ub);
        }

        const string LayoutEn = "`qwertyuiop[]asdfghjkl;'zxcvbnm,.";
        const string LayoutRu = "ёйцукенгшщзхъфывапролджэячсмитьбю";

        // "ghbdtn" -> "привет"
        static string FixLayout(string q)
        {
            if (q.Length < 3 || !Regex.IsMatch(q, @"^[A-Za-z\[\];',.`\s]+$")) return null;
            var sb = new StringBuilder();
            foreach (var ch in q)
            {
                int i = LayoutEn.IndexOf(char.ToLower(ch));
                if (i < 0) { sb.Append(ch); continue; }
                sb.Append(char.IsUpper(ch) ? char.ToUpper(LayoutRu[i]) : LayoutRu[i]);
            }
            var r = sb.ToString();
            // only offer it when the Latin text does not look like a real English word
            // English words have vowels, Russian typed on the wrong layout mostly doesn't;
            // and the result has to look like Russian (enough Russian vowels)
            int letters = q.Count(char.IsLetter);
            int enVowels = q.Count(c => "aeiouyAEIOUY".IndexOf(c) >= 0);
            int ruVowels = r.ToLowerInvariant().Count(c => "аеёиоуыэюя".IndexOf(c) >= 0);
            bool englishLike = enVowels * 5 >= letters;
            bool russianLike = ruVowels * 10 >= r.Count(char.IsLetter) * 3;
            return !englishLike && russianLike ? r : null;
        }

        void IndexShortcuts()
        {
            var list = new List<string>();
            foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) })
                try { list.AddRange(Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories).Where(f => !f.ToLowerInvariant().Contains("uninstall") && !f.Contains("Удал"))); } catch { }
            shortcuts = list;
        }

        string FindApp(string q)
        {
            if (q.Length < 2) return null;
            return shortcuts
                .Select(f => new { f, n = System.IO.Path.GetFileNameWithoutExtension(f).ToLowerInvariant() })
                .Where(x => x.n.Contains(q) || Initials(x.n).StartsWith(q))
                .OrderBy(x => x.n.StartsWith(q) ? 0 : 1).ThenBy(x => x.n.Length)
                .Select(x => x.f).FirstOrDefault();
        }

        static string Initials(string s) { return new string(s.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries).Select(w => w[0]).ToArray()); }

        // ---------- colour picker ----------

        Grid layerPick; Ellipse pickSwatch; TextBlock pickHex;
        DispatcherTimer pickTimer; DateTime pickArmed;

        void InitPicker(Grid root)
        {
            layerPick = new Grid { Width = LW, Height = LH, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Opacity = 0, IsHitTestVisible = false };
            var row = new DockPanel { Margin = new Thickness(10, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            pickSwatch = new Ellipse { Width = 24, Height = 24, Stroke = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), StrokeThickness = 1, Margin = new Thickness(0, 0, 10, 0) };
            DockPanel.SetDock(pickSwatch, Dock.Left);
            var hint = UI.Text("клик - взять · Esc", 11.5, UI.Gray); hint.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(hint, Dock.Right);
            pickHex = UI.Text("", 14, UI.Fg, FontWeights.SemiBold); pickHex.VerticalAlignment = VerticalAlignment.Center;
            pickHex.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            row.Children.Add(pickSwatch); row.Children.Add(hint); row.Children.Add(pickHex);
            layerPick.Children.Add(row);
            root.Children.Add(layerPick);
            pickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            pickTimer.Tick += (s, e) => PickTick();
        }

        void StartPicker()
        {
            SetMode(Mode.Pick);
            pickArmed = DateTime.Now.AddMilliseconds(350); // ignore the click that started it
            pickTimer.Start();
        }

        void PickTick()
        {
            POINT p; GetCursorPos(out p);
            var dc = GetDC(IntPtr.Zero); uint px = GetPixel(dc, p.X, p.Y); ReleaseDC(IntPtr.Zero, dc);
            var c = Color.FromRgb((byte)px, (byte)(px >> 8), (byte)(px >> 16));
            pickSwatch.Fill = new SolidColorBrush(c);
            pickHex.Text = string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
            if (DateTime.Now < pickArmed) return;
            if ((GetAsyncKeyState(0x1B) & 0x8000) != 0) { pickTimer.Stop(); SetMode(Mode.Collapsed); return; }
            if ((GetAsyncKeyState(0x01) & 0x8000) != 0)
            {
                pickTimer.Stop();
                mode = Mode.Collapsed; // the clipboard notice takes over from here
                try { Clipboard.SetText(pickHex.Text); } catch { SetMode(Mode.Collapsed); }
            }
        }
    }
}
