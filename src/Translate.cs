using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Island
{
    static class Online
    {
        public static string Post(string url, string form)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST"; request.ContentType = "application/x-www-form-urlencoded; charset=utf-8";
            request.UserAgent = "Island/" + Updates.Current;
            request.Timeout = 12000; request.ReadWriteTimeout = 12000;
            var bytes = Encoding.UTF8.GetBytes(form); request.ContentLength = bytes.Length;
            using (var stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
            using (var response = request.GetResponse())
            using (var reader = new System.IO.StreamReader(response.GetResponseStream(), Encoding.UTF8)) return reader.ReadToEnd();
        }

        public static string Get(string url)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "Island/" + Updates.Current;
            request.Timeout = 12000; request.ReadWriteTimeout = 12000;
            using (var response = request.GetResponse())
            using (var reader = new System.IO.StreamReader(response.GetResponseStream(), Encoding.UTF8)) return reader.ReadToEnd();
        }
    }

    static class Translator
    {
        static readonly Dictionary<string, string> cache = new Dictionary<string, string>();
        internal static string ParseTranslation(string json)
        {
            var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            object sentences;
            if (data == null || !data.TryGetValue("sentences", out sentences) || !(sentences is IEnumerable))
                throw new InvalidOperationException(L.T("Сервис не вернул перевод"));
            var result = new StringBuilder();
            foreach (var item in (IEnumerable)sentences)
            {
                var sentence = item as Dictionary<string, object>; object text;
                if (sentence != null && sentence.TryGetValue("trans", out text) && text is string) result.Append((string)text);
            }
            if (string.IsNullOrWhiteSpace(result.ToString())) throw new InvalidOperationException(L.T("Сервис не вернул перевод"));
            return result.ToString();
        }

        public static string Translate(string text, out string pair)
        {
            string source = Regex.IsMatch(text, @"[а-яёА-ЯЁ]") ? "ru" : "en";
            string target = Settings.Get("translateTarget");
            if (target == "auto") target = source == "ru" ? "en" : "ru";
            pair = source.ToUpperInvariant() + " → " + target.ToUpperInvariant() + " · Google Translate";
            if (source == target) return text;
            string key = source + "|" + target + "|" + text, result;
            if (cache.TryGetValue(key, out result)) return result;
            // Send the complete selection, preserving sentence context. Never fall back
            // to translation-memory matches that can silently substitute unrelated words.
            result = ParseTranslation(Online.Post("https://translate.googleapis.com/translate_a/single?client=gtx&sl=" + source + "&tl=" + target + "&dt=t&dj=1", "q=" + Uri.EscapeDataString(text)));
            if (cache.Count >= 24) cache.Clear();
            cache[key] = result; return result;
        }
    }

    partial class IslandWindow
    {
        bool selectionBusy, translateBusy;
        DispatcherTimer selectionDelay;
        Point? selectionPointer;
        int selectionRequest;
        int selectionWorkers;
        string previousSelection = "";
        int translationRequest;

        void InitExtras()
        {
            SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                if (Settings.Get("translator") == "1" && !RegisterHotKey(h, 0x1A03, 0x0001 | 0x0002 | 0x4000, 0x54))
                    Dispatcher.BeginInvoke(new Action(() => Notify("\uE8C1", UI.Orange, "Переводчик", "Ctrl+Alt+T занято другой программой")));
                HwndSource.FromHwnd(h).AddHook((IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) =>
                {
                    if (msg == 0x0312 && w.ToInt32() == 0x1A03) { ReadSelection(false); handled = true; }
                    return IntPtr.Zero;
                });
            };
            if (Settings.Get("translator") == "1" && Settings.Get("translateAuto") == "1")
            {
                selectionDelay = Timer(350, null);
                selectionDelay.Tick += (s, e) => { selectionDelay.Stop(); ReadSelection(true); };
                Closed += (s, e) => selectionDelay.Stop();
            }
            Updates.Ready = () =>
            {
                Notify("\uE895", UI.Green, "Обновление готово", "Нажмите, чтобы перезапустить Island");
                noticeAction = Updates.Install;
            };
            var updateTimer = Timer(15000, null);
            updateTimer.Tick += (s, e) =>
            {
                updateTimer.Interval = TimeSpan.FromHours(6);
                if (Settings.Get("updates") == "1") Updates.Check(false, null);
            };
            updateTimer.Start();
            Closed += (s, e) => { updateTimer.Stop(); Updates.Ready = null; };
        }

        void SelectionMouseUp(int x, int y)
        {
            if (selectionDelay == null || IsMouseOver) return;
            selectionPointer = new Point(x, y);
            selectionDelay.Stop(); selectionDelay.Start();
        }

        void ReadSelection(bool automatic)
        {
            if (Settings.Get("translator") != "1" || selectionBusy || translateBusy) return;
            var foreground = Native.GetForegroundWindow();
            if (foreground == new WindowInteropHelper(this).Handle) return;
            if (!automatic) TranslationPanel(L.T("Читаем выделение…"), "", false);
            if (selectionWorkers >= 2)
            {
                if (!automatic) TranslationPanel(L.T("Приложение не отвечает на запрос выделения. Скопируйте текст и нажмите «Перевести буфер»."), "", false);
                return;
            }
            selectionBusy = true;
            int request = ++selectionRequest;
            Point? pointer = automatic ? selectionPointer : null;
            selectionWorkers++;
            var timeout = Timer(2500, null);
            timeout.Tick += (s, e) =>
            {
                timeout.Stop();
                if (request != selectionRequest) return;
                selectionRequest++; selectionBusy = false;
                if (!automatic && translationVisible) TranslationPanel(L.T("Приложение не отвечает на запрос выделения. Скопируйте текст и нажмите «Перевести буфер»."), "", false);
            };
            timeout.Start();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string text = null;
                try
                {
                    text = SelectionReader.Read(foreground, pointer);
                }
                catch (Exception e) { Log.Write("selection", e.GetType().Name); }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    timeout.Stop(); selectionWorkers--;
                    if (request != selectionRequest) return;
                    selectionBusy = false;
                    // Discard a result if the user switched windows during UI Automation.
                    var currentWindow = Native.GetForegroundWindow();
                    if (currentWindow != foreground && currentWindow != new WindowInteropHelper(this).Handle)
                    {
                        if (!automatic && translationVisible) TranslationPanel(L.T("Активное окно изменилось. Выделите текст и повторите Ctrl+Alt+T."), "", false);
                        return;
                    }
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        if (!automatic) TranslationPanel(L.T("Не удалось прочитать выделение. Скопируйте текст и нажмите «Перевести буфер»."), "", false);
                        return;
                    }
                    text = text.Trim();
                    if (automatic && translationVisible && text == previousSelection) return;
                    if (translateBusy) return;
                    previousSelection = text; TranslateText(text);
                }));
            });
        }

        void TranslateClipboard()
        {
            if (Settings.Get("translator") != "1") { TranslationPanel(L.T("Переводчик выключен в настройках"), "", false); return; }
            try
            {
                if (System.Windows.Clipboard.ContainsText()) TranslateText(System.Windows.Clipboard.GetText());
                else TranslationPanel(L.T("Сначала скопируйте текст"), "", false);
            }
            catch { TranslationPanel(L.T("Буфер обмена занят. Попробуйте ещё раз."), "", false); }
        }

        void TranslateText(string text)
        {
            if (translateBusy || string.IsNullOrWhiteSpace(text)) return;
            if (text.Length > 4000) { TranslationPanel(L.T("Выделите не более 4000 символов"), "", false); return; }
            translateBusy = true; int request = ++translationRequest;
            TranslationPanel(L.T("Переводим…"), text, false);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string result, pair = ""; bool success = false;
                try { result = Translator.Translate(text, out pair); success = true; }
                catch (WebException) { result = L.T("Нет связи с сервисом перевода. Проверьте интернет и попробуйте ещё раз."); }
                catch (Exception e) { result = e is InvalidOperationException ? e.Message : L.T("Не удалось перевести текст"); Log.Write("translate", e.Message); }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    translateBusy = false;
                    if (request == translationRequest && SettingsOpen && translationVisible) TranslationPanel(result, success ? pair : "", success);
                }));
            });
        }

        bool translationVisible;
        void TranslationPanel(string result, string detail, bool canCopy)
        {
            if (settingsHost == null)
            {
                settingsHost = new Grid { Background = UI.Bg, Visibility = Visibility.Collapsed, RenderTransform = new TranslateTransform() };
                layerExpanded.Children.Add(settingsHost);
            }
            translationVisible = true;
            var panel = new DockPanel { Margin = new Thickness(22, 16, 22, 14) };
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var close = UI.Pressable(UI.Glyph("\uE711", 11, UI.Fg), 30, 30, 15, HideSettings);
            DockPanel.SetDock(close, Dock.Right); head.Children.Add(close);
            var title = UI.Text("Переводчик", 20, UI.Fg, FontWeights.SemiBold); head.Children.Add(title);
            DockPanel.SetDock(head, Dock.Top); panel.Children.Add(head);
            var foot = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var copyLabel = UI.Text(canCopy ? "Скопировать" : "Перевести буфер", 12, UI.Fg);
            copyLabel.HorizontalAlignment = HorizontalAlignment.Center;
            copyLabel.VerticalAlignment = VerticalAlignment.Center;
            var copy = UI.Pressable(copyLabel, 145, 30, 15, () =>
            {
                if (!canCopy) { TranslateClipboard(); return; }
                try { System.Windows.Clipboard.SetText(result); Flash("Скопировано"); } catch { }
            });
            DockPanel.SetDock(copy, Dock.Right); foot.Children.Add(copy);
            var sub = UI.Text(string.IsNullOrEmpty(detail) ? "Ctrl+Alt+T · RU ↔ EN" : detail, 11.5, UI.Gray);
            sub.TextTrimming = TextTrimming.CharacterEllipsis; sub.VerticalAlignment = VerticalAlignment.Center;
            foot.Children.Add(sub); DockPanel.SetDock(foot, Dock.Bottom); panel.Children.Add(foot);
            var text = UI.Text(result, 15, UI.Fg); text.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(new ScrollViewer { Content = text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            settingsHost.Children.Clear(); settingsHost.Children.Add(panel); settingsHost.Visibility = Visibility.Visible;
            settingsHost.Opacity = 1;
            ((TranslateTransform)settingsHost.RenderTransform).Y = 0;
            if (hidden) ToggleHidden();
            fullscreen = false;
            UI.Ease(pill, OpacityProperty, 1, 160); pill.IsHitTestVisible = true;
            leaveTimer.Stop();
            SetMode(Mode.Expanded);
        }
    }
}
